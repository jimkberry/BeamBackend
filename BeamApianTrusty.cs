
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



        protected ApianVoteMachine<PlaceBikeData> placeClaimVoteMachine;
        protected ApianVoteMachine<PlaceBikeData> placeHitVoteMachine;        

        public BeamApianTrusty(IBeamGameNet _gn,  IBeamApianClient _client) : base(_gn, _client)
        {
            placeClaimVoteMachine = new ApianVoteMachine<PlaceBikeData>(logger);
            placeHitVoteMachine = new ApianVoteMachine<PlaceBikeData>(logger);            
        }

 
        //
        // IBeamApian  
        //
        public override void OnCreateBikeReq(BikeCreateDataMsg msg, string srcId, long msgDelay)
        {
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
                gameNet.RequestBikeData(msg.bikeId, srcId);
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
            if (placeHitVoteMachine.AddVote(newPd, srcId, client.gameData.Peers.Count) && bb != null)
            {
                logger.Verbose($"OnPlaceHitObs() - Calling OnPlaceHit()");                
                client.OnPlaceHit(msg, msgDelay);                
            }
   
        }

        // public void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay) 
        // {
        //     // TODO: This test is implementing the "trusty" consensus 
        //     // "place owner is authority" rule. 
        //     Vector2 pos = Ground.Place.PlacePos(msg.xIdx, msg.zIdx);
        //     Ground.Place p = gameData.Ground.GetPlace(pos); 
        //     BaseBike hittingBike = gameData.GetBaseBike(msg.bikeId);                        
        //     if (p == null)
        //     {
        //         logger.Verbose($"OnPlaceHitObs(). PlaceHitObs for unclaimed place: ({msg.xIdx}, {msg.zIdx})");                
        //     } else if (hittingBike == null) { 
        //         logger.Verbose($"OnPlaceHitObs(). PlaceHitObs for unknown bike:  msg.bikeId)"); 
        //     } else {          
        //         // It's OK (expected, even) for ther peers to observe it. We just ignore them in Trusty
        //         if (srcId == p.bike.peerId)      
        //             client.OnPlaceHit(msg,  msgDelay);
        //     }      
        // }


        public override void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay) 
        {
           BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                logger.Debug($"OnPlaceClaimObs() - unknown bike: {msg.bikeId}");
                gameNet.RequestBikeData(msg.bikeId, srcId);
            }              
         
            logger.Debug($"OnPlaceClaimObs() - Got ClaimObs from {srcId}. PeerCount: {client.gameData.Peers.Count}");
            PlaceBikeData newPd = new PlaceBikeData(msg.xIdx, msg.zIdx, msg.bikeId);            
            if (placeClaimVoteMachine.AddVote(newPd, srcId, client.gameData.Peers.Count) && bb != null)
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
                gameNet.RequestBikeData(msg.bikeId, srcId);
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
                gameNet.RequestBikeData(msg.bikeId, srcId);
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