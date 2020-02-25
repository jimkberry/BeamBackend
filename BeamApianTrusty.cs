using GameNet;
using UniLog;
using UnityEngine;

namespace BeamBackend
{
    public class BeamApianTrusty : IBeamApian // , IBeamGameNetClient 
    {
        protected BeamGameInstance client;
        protected BeamGameData gameData; // can read it - Apian writing to it is not allowed
        protected IBeamGameNet _gn;
        public UniLogger logger;

        public BeamApianTrusty(IBeamApianClient _client) 
        {
            client = _client as BeamGameInstance;
            gameData = client.gameData;
            logger = UniLogger.GetLogger("Apian");
        }

        //
        // IApian
        //
        public void SetGameNetInstance(IGameNet gn) => _gn = gn as IBeamGameNet;
        public void OnGameCreated(string gameP2pChannel) => client.OnGameCreated(gameP2pChannel);
        public void OnGameJoined(string gameId, string localP2pId) => client.OnGameJoined(gameId, localP2pId);
        public void OnPeerJoined(string p2pId, string peerHelloData) => client.OnPeerJoined(p2pId, peerHelloData);
        public void OnPeerLeft(string p2pId) => client.OnPeerLeft(p2pId);
        public string LocalPeerData() => client.LocalPeerData();              
        public void OnApianMessage(ApianMessage msg) {}   

        //
        // IBeamApian  
        //
        public void OnCreateBikeReq(BikeCreateDataMsg msg, string srcId, long msgDelay)
        {
            if ( gameData.GetBaseBike(msg.bikeId) != null)
            {
                logger.Verbose($"OnCreateBikeReqData() Bike already exists: {msg.bikeId}.");   
                return;
            }    

            if (srcId == msg.peerId)
                client.OnCreateBike(msg, msgDelay);
        }

        public void OnBikeDataReq(BikeDataReqMsg msg, string srcId, long msgDelay)
        {
            logger.Debug($"OnBikeDataReq() - sending data for bike: {msg.bikeId}");            
            IBike ib = client.gameData.GetBaseBike(msg.bikeId);
            if (ib != null)
                client.PostBikeCreateData(ib, srcId); 
        }        

        public void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay) 
        {
            // TODO: This test is implementing the "trusty" consensus 
            // "place owner is authority" rule. 
            Vector2 pos = Ground.Place.PlacePos(msg.xIdx, msg.zIdx);
            Ground.Place p = gameData.Ground.GetPlace(pos); 
            BaseBike hittingBike = gameData.GetBaseBike(msg.bikeId);                        
            if (p == null)
            {
                logger.Verbose($"OnPlaceHitObs(). PlaceHitObs for unclaimed place: ({msg.xIdx}, {msg.zIdx})");                
            } else if (hittingBike == null) { 
                logger.Verbose($"OnPlaceHitObs(). PlaceHitObs for unknown bike:  msg.bikeId)"); 
            } else {          
                // It's OK (expected, even) for ther peers to observe it. We just ignore them in Trusty
                if (srcId == p.bike.peerId)      
                    client.OnPlaceHit(msg,  msgDelay);
            }      
        }


        public void OnPlaceClaimObs(PlaceClaimIdxMsg msg, string srcId, long msgDelay) 
        {
            BaseBike b = gameData.GetBaseBike(msg.bikeId);
            //  This test is implementing the "trusty" consensus 
            // "bike owner is authority" rule. 
            // TODO: This is NOT good enough and can result in inconsistency. Even in a trusty Apian a "race to a place" like this
            //   requires some sort of inter-peer protocol. 
            if (b != null && srcId == b.peerId)
            {
                client.OnPlaceClaim(msg, msgDelay);
            }      
        }
        public void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay) 
        {
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                logger.Debug($"OnBikeCommandReq() - unknown bike: {msg.bikeId}");
                _gn.RequestBikeData(msg.bikeId, srcId);
            } else {
                if (bb.peerId == srcId)
                    client.OnBikeCommand(msg, msgDelay);
            }
        }
        public void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay)
        {
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                logger.Debug($"OnBikeTurnReq() - unknown bike: {msg.bikeId}");
                _gn.RequestBikeData(msg.bikeId, srcId);
            } else {            
                if ( bb.peerId == srcId)
                    client.OnBikeTurn(msg, msgDelay);
            }
        }    

        public void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay) 
        {
            BaseBike b = gameData.GetBaseBike(msg.bikeId);            
            if (b != null && srcId == b.peerId)
                client.OnRemoteBikeUpdate(msg, srcId,  msgDelay);
        }

    }

}