using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using GameNet;
using Apian;
using UniLog;
using UnityEngine;

namespace BeamBackend
{
    public class BeamApianTrusty : BeamApian 
    {
        public struct PlaceBikeData // for things related to a bike and a place (like claim, hit)
        {
            public int x;
            public int z;
            public string bikeId;
            public override string ToString() => $"({x}, {z}, {bikeId})";

            public PlaceBikeData(int _x, int _z, string _bid) { x = _x; z=_z; bikeId=_bid; }
        }

        protected const long kDefaultVoteTimeoutMs = 300;

        protected ApianVoteMachine<PlaceBikeData> placeClaimVoteMachine;
        protected ApianVoteMachine<PlaceBikeData> placeHitVoteMachine;        

        public BeamApianTrusty(IBeamGameNet _gn,  IBeamApianClient _client) : base(_gn, _client)
        {
            placeClaimVoteMachine = new ApianVoteMachine<PlaceBikeData>(kDefaultVoteTimeoutMs, logger);
            placeHitVoteMachine = new ApianVoteMachine<PlaceBikeData>(kDefaultVoteTimeoutMs, logger);   

            // Trusty messagees json/from/to/lagMs
            ApMsgHandlers[ApianMessage.kRequestGroups] = (j, f,t,l) => OnRequestGroupsMsg(j, f,t,l);             
            ApMsgHandlers[ApianMessage.kGroupAnnounce] = (j, f,t,l) => OnGroupAnnounceMsg(j, f,t,l);
            ApMsgHandlers[ApianMessage.kGroupJoinReq] = (j, f,t,l) => OnGroupJoinReq(j, f,t,l);            
            ApMsgHandlers[ApianMessage.kGroupJoinVote] = (j, f,t,l) => OnGroupJoinVote(j, f,t,l);             
            ApMsgHandlers[ApianMessage.kApianClockOffset] = (j, f,t,l) => OnApianClockOffsetMsg(j, f,t,l);            
        }

        // Apian Message Handlers
        public void OnRequestGroupsMsg(string msgJson, string fromId, string toId, long lagMs)
        {
            RequestGroupsMsg msg = JsonConvert.DeserializeObject<RequestGroupsMsg>(msgJson);
            ApianGroup.OnApianMsg(msg, fromId, toId);
        }        
        public void OnGroupAnnounceMsg(string msgJson, string fromId, string toId, long lagMs)
        {
            GroupAnnounceMsg msg = JsonConvert.DeserializeObject<GroupAnnounceMsg>(msgJson);
            ApianGroup.OnApianMsg(msg, fromId, toId);
        }

        public void OnGroupJoinReq(string msgJson, string fromId, string toId, long lagMs)
        {
            GroupJoinRequestMsg msg = JsonConvert.DeserializeObject<GroupJoinRequestMsg>(msgJson);
            ApianGroup.OnApianMsg(msg, fromId, toId);
        }

        public void OnGroupJoinVote(string msgJson, string fromId, string toId, long lagMs)
        {
            GroupJoinVoteMsg msg = JsonConvert.DeserializeObject<GroupJoinVoteMsg>(msgJson);
            ApianGroup.OnApianMsg(msg, fromId, toId);
        }

        public void OnApianClockOffsetMsg(string msgJson, string fromId, string toId, long lagMs)
        {
            ApianClockOffsetMsg msg = JsonConvert.DeserializeObject<ApianClockOffsetMsg>(msgJson);
            ApianClock.OnApianClockOffset(msg.peerId, msg.clockOffset);
        }


        //
        // IBeamApian  
        //
        public override void OnCreateBikeReq(BikeCreateDataMsg msg, string srcId, long msgDelay)
        {
            logger.Info($"OnCreateBikeReqData() - got req from {srcId}"); // &&&&&&&
            if ( gameData.GetBaseBike(msg.bikeId) != null)
            {
                logger.Verbose($"OnCreateBikeReqData() Bike already exists: {msg.bikeId}.");   
                return;
            }    

            if (srcId == msg.peerId)
                client.OnCreateBike(msg, msgDelay);
        }

        public override void OnBikeDataReq(BikeDataReqMsg msg, string srcId, long msgDelay)
        {
            logger.Debug($"OnBikeDataReq() - sending data for bike: {msg.bikeId}");            
            IBike ib = client.gameData.GetBaseBike(msg.bikeId);
            if (ib != null)
                client.PostBikeCreateData(ib, srcId); 
        }        

        public override void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay) 
        {
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                logger.Debug($"OnPlaceHitObs() - unknown bike: {msg.bikeId}");
                BeamGameNet.RequestBikeData(msg.bikeId, srcId);
                // TODO: think about what happens if we get the bike data before the vote is done.
                // Should we: go ahead and count the incoming votes, but just not call OnPlaceHit() <- doing this now
                //      while the bike isn't there
                // or:  Add the ability to create a "poison" vote for this event
                // or: Never add a bike while it's involved in a vote (keep the data "pending" and check for when it's ok) <- probably bad
                // or: do nothing. <- probably bad
            } 
            if (gameData.Ground.GetPlace(msg.xIdx, msg.zIdx) == null)
            {
                logger.Warn($"OnPlaceHitObs() - unclaimed place: msg.xIdx, msg.zIdx");
                return;
            }
       
            logger.Debug($"OnPlaceHitObs() - Got HitObs from {srcId}. PeerCount: {client.gameData.Peers.Count}");
            PlaceBikeData newPd = new PlaceBikeData(msg.xIdx, msg.zIdx, msg.bikeId);
            if (placeHitVoteMachine.AddVote(newPd, srcId, client.gameData.Peers.Count, true) == VoteStatus.kWon && bb != null)
            {
                logger.Verbose($"OnPlaceHitObs() - Calling OnPlaceHit()");                
                client.OnPlaceHit(msg, msgDelay);                
            }
   
        }

        public override void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay) 
        {
           BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                logger.Debug($"OnPlaceClaimObs() - unknown bike: {msg.bikeId}");
                BeamGameNet.RequestBikeData(msg.bikeId, srcId);
            }              
         
            logger.Debug($"OnPlaceClaimObs() - Got ClaimObs from {srcId}. PeerCount: {client.gameData.Peers.Count}");
            PlaceBikeData newPd = new PlaceBikeData(msg.xIdx, msg.zIdx, msg.bikeId);            
            if (placeClaimVoteMachine.AddVote(newPd, srcId, client.gameData.Peers.Count, true) == VoteStatus.kWon && bb != null)
            {
                logger.Debug($"OnPlaceClaimObs() - Calling OnPlaceClaim()");                
                client.OnPlaceClaim(msg, msgDelay);                
            }
            
        }
   
        public override void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay) 
        {
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                logger.Debug($"OnBikeCommandReq() - unknown bike: {msg.bikeId}");
                BeamGameNet.RequestBikeData(msg.bikeId, srcId);
            } else {
                if (bb.peerId == srcId)
                    client.OnBikeCommand(msg, msgDelay);
            }
        }

        public override void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay)
        {
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                logger.Debug($"OnBikeTurnReq() - unknown bike: {msg.bikeId}");
                BeamGameNet.RequestBikeData(msg.bikeId, srcId);
            } else {            
                if ( bb.peerId == srcId)
                    client.OnBikeTurn(msg, msgDelay);
            }
        }    

        public override void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay) 
        {
            BaseBike b = gameData.GetBaseBike(msg.bikeId);            
            if (b != null && srcId == b.peerId)
                client.OnRemoteBikeUpdate(msg, srcId,  msgDelay);
        }

    }

}