using System.Collections.Generic;
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
            placeClaimVoteMachine = new ApianVoteMachine<PlaceBikeData>(kDefaultVoteTimeoutMs, kDefaultVoteTimeoutMs*2, logger);
            placeHitVoteMachine = new ApianVoteMachine<PlaceBikeData>(kDefaultVoteTimeoutMs, kDefaultVoteTimeoutMs*2, logger);   

             // Trusty messagees json/from/to/lagMs  &&&&& (obsolete)
            // OldBeamApMsgHandlers[BasicGroupMessages.kRequestGroups] = (j, f,t,l) => OnRequestGroupsMsg(j, f,t,l);             
            // OldBeamApMsgHandlers[BasicGroupMessages.kGroupAnnounce] = (j, f,t,l) => OnGroupAnnounceMsg(j, f,t,l);
            // OldBeamApMsgHandlers[BasicGroupMessages.kGroupJoinReq] = (j, f,t,l) => OnGroupJoinReq(j, f,t,l);            
            // OldBeamApMsgHandlers[BasicGroupMessages.kGroupJoinVote] = (j, f,t,l) => OnGroupJoinVote(j, f,t,l);                       
        }

        // Apian Message Handlers
        // public void OnRequestGroupsMsg(string msgJson, string fromId, string toId, long lagMs)
        // {
        //     BasicGroupMessages.RequestGroupsMsg msg = JsonConvert.DeserializeObject<BasicGroupMessages.RequestGroupsMsg>(msgJson);
        //     ApianGroup.OnApianMsg(msg, fromId, toId);
        // }        
        // public void OnGroupAnnounceMsg(string msgJson, string fromId, string toId, long lagMs)
        // {
        //     BasicGroupMessages.GroupAnnounceMsg msg = JsonConvert.DeserializeObject<BasicGroupMessages.GroupAnnounceMsg>(msgJson);
        //     ApianGroup.OnApianMsg(msg, fromId, toId);
        // }

        // public void OnGroupJoinReq(string msgJson, string fromId, string toId, long lagMs)
        // {
        //     BasicGroupMessages.GroupJoinRequestMsg msg = JsonConvert.DeserializeObject<BasicGroupMessages.GroupJoinRequestMsg>(msgJson);
        //     ApianGroup.OnApianMsg(msg, fromId, toId);
        // }

        // public void OnGroupJoinVote(string msgJson, string fromId, string toId, long lagMs)
        // {
        //     BasicGroupMessages.GroupJoinVoteMsg msg = JsonConvert.DeserializeObject<BasicGroupMessages.GroupJoinVoteMsg>(msgJson);
        //     ApianGroup.OnApianMsg(msg, fromId, toId);
        // }

        //
        // IBeamApian  
        //
        //protected void _SendApianRequest( ApianClientMsg cliMsg)
        //{
        //    ApianRequest req = new ApianRequest(cliMsg);
        //    BeamGameNet.SendApianMessage(ApianGroup.GroupId, req);  
        //}

        public override void SendBikeTurnReq(IBike bike, TurnDir dir, Vector2 nextPt)
        {
            logger.Debug($"SendBikeTurnReq) Bike: {bike.bikeId}");                    
            BikeTurnMsg msg = new BikeTurnMsg(ApianClock.CurrentTime, bike, dir, nextPt); 
            ApianBikeTurnRequest req = new ApianBikeTurnRequest(msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, req);                   
        }
        public override void SendBikeCommandReq(IBike bike, BikeCommand cmd, Vector2 nextPt)
        {
            logger.Debug($"BeamGameNet.SendBikeCommand() Bike: {bike.bikeId}");                    
            BikeCommandMsg msg = new BikeCommandMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, cmd, nextPt);
            ApianBikeCommandRequest req = new ApianBikeCommandRequest(msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, req);                      
        }        
        public override void SendBikeCreateReq(IBike ib, List<Ground.Place> ownedPlaces, string destId = null)
        {
            logger.Debug($"SendBikeCreateReq() - dest: {(destId??"bcast")}");            
            // Broadcast this to send it to everyone
            BikeCreateDataMsg msg = new BikeCreateDataMsg(ApianClock.CurrentTime, ib, ownedPlaces);
            ApianBikeCreateRequest req = new ApianBikeCreateRequest(msg);
            BeamGameNet.SendApianMessage(destId ?? ApianGroup.GroupId, req);
        }

        public override void SendPlaceClaimObs(IBike bike, int xIdx, int zIdx)
        {
            logger.Debug($"SendPlaceClaimObs()");            
            PlaceClaimMsg msg = new PlaceClaimMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, xIdx, zIdx);
            ApianPlaceClaimObservation obs = new ApianPlaceClaimObservation(msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, obs);          
        }
        
        public override void SendPlaceHitObs(IBike bike, int xIdx, int zIdx)
        {
            logger.Debug($"SendPlaceHitObs()");                
            PlaceHitMsg msg = new PlaceHitMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, xIdx, zIdx);
            ApianPlaceHitObservation obs = new ApianPlaceHitObservation(msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, obs);             
        }        

        public override void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay)
        {
            logger.Verbose($"OnBikeTurnReq() - bike: {msg.bikeId}, source: {srcId}");            
            if (ApianClock.IsIdle) // this is fugly
                return;            
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                if (msg.ownerPeer != ApianGroup.LocalP2pId)
                {                
                    logger.Debug($"OnBikeTurnReq() - unknown bike: {msg.bikeId}, source: {srcId}");
                    client.OnUnknownBike(msg.bikeId, msg.ownerPeer);                
                    BeamGameNet.RequestBikeData(msg.bikeId, msg.ownerPeer);
                }
            } else {            
                if ( bb.peerId == srcId)
                    client.OnBikeTurn(msg, msgDelay);
            }
        } 

        public override void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay) 
        {
            if (ApianClock.IsIdle) // this is fugly
                return;            
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null) 
            {                
                if (msg.ownerPeer != ApianGroup.LocalP2pId)
                {
                    logger.Verbose($"OnBikeCommandReq() - unknown bike: {msg.bikeId}, source: {srcId}");                    
                    client.OnUnknownBike(msg.bikeId, msg.ownerPeer);               
                    BeamGameNet.RequestBikeData(msg.bikeId, msg.ownerPeer);
                }
            } else {
                if (bb.peerId == srcId)
                    client.OnBikeCommand(msg, msgDelay);
            }
        }

        public override void OnBikeCreateReq(BikeCreateDataMsg msg, string srcId, long msgDelay)
        {
            if (ApianClock.IsIdle) // TODO: this is fugly. SHould replace with a check if the local ApianPeer obkect is ready
                return;

            logger.Info($"OnCreateBikeReq() - got req from {srcId}"); // &&&&&&&
            if ( gameData.GetBaseBike(msg.bikeId) != null)
            {
                logger.Verbose($"OnCreateBikeReq() Bike already exists: {msg.bikeId}.");   
                return;
            }    

            if (srcId == msg.peerId)
                client.OnCreateBike(msg, msgDelay);
        }

        public override void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay) 
        {
            if (ApianClock.IsIdle) // this is fugly
                return;            
           BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                if (msg.ownerPeer != ApianGroup.LocalP2pId)
                {                
                    logger.Verbose($"OnPlaceClaimObs() - unknown bike: {msg.bikeId}, source: {srcId}");
                    client.OnUnknownBike(msg.bikeId, msg.ownerPeer);                
                    BeamGameNet.RequestBikeData(msg.bikeId, msg.ownerPeer);
                }
            }              
         
            logger.Debug($"OnPlaceClaimObs() - Got ClaimObs from {srcId}. PeerCount: {client.gameData.Peers.Count}");
            PlaceBikeData newPd = new PlaceBikeData(msg.xIdx, msg.zIdx, msg.bikeId);            
            placeClaimVoteMachine.AddVote(newPd, srcId, msg.TimeStamp, client.gameData.Peers.Count);
            VoteResult vr = placeClaimVoteMachine.GetResult(newPd);        
            if (!vr.wasComplete && vr.status == VoteStatus.kWon && bb != null)
            {        
                msg.TimeStamp = vr.timeStamp;                
                logger.Debug($"OnPlaceClaimObs() - Calling OnPlaceClaim()");              
                client.OnPlaceClaim(msg, msgDelay); 
            }
        }


        public override void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay) 
        {
            if (ApianClock.IsIdle) // this is fugly
                return;            
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                if (msg.ownerPeer != ApianGroup.LocalP2pId)
                {                
                    logger.Verbose($"OnPlaceHitObs() - unknown bike: {msg.bikeId}, source: {srcId}");
                    client.OnUnknownBike(msg.bikeId, msg.ownerPeer);
                    BeamGameNet.RequestBikeData(msg.bikeId, msg.ownerPeer);
                    return;
                }
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
            placeHitVoteMachine.AddVote(newPd, srcId, msg.TimeStamp, client.gameData.Peers.Count);
            VoteResult vr = placeHitVoteMachine.GetResult(newPd);         
            if (!vr.wasComplete && vr.status == VoteStatus.kWon && bb != null)
            {
                msg.TimeStamp = vr.timeStamp;
                logger.Verbose($"OnPlaceHitObs() - Calling OnPlaceHit()");                
                client.OnPlaceHit(msg, msgDelay);                
            }

        }        

        // IBeamApian  &&& new is above - - - - - - - - - -

        public override void OnBikeDataQuery(BikeDataQueryMsg msg, string srcId, long msgDelay)
        {
            if (ApianClock.IsIdle) // this is fugly
                return;                       
            IBike ib = client.gameData.GetBaseBike(msg.bikeId);
            logger.Info($"OnBikeDataQuery() - bike: {msg.bikeId} {(ib==null?"is GONE! Ignoring.":"Sending")}");             
            if (ib != null)
                client.PostBikeCreateData(ib, srcId); 
        }        

        public override void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay) 
        {
            // TODO: no longer used. Get rid of update stuff
            if (ApianClock.IsIdle) // this is fugly
                return;            
            BaseBike b = gameData.GetBaseBike(msg.bikeId);            
            if (b != null && srcId == b.peerId)
                client.OnRemoteBikeUpdate(msg, srcId,  msgDelay);
        }

    }

}