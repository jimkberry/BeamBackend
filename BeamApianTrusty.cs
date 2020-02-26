using System;
using System.Collections.Generic;
using System.Linq;
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

        // Place claim logic:
        //
        // When a place claim arrives: 
        //   clear out any "expired" pending claims (housekeeping)
        //   Is the  x/z/bike hash in the pending dict:
        //      increment the report counter
        //      count > half of peers:
        //         send the claim assertion
        //
        // (clean up by letting the entries expire)

        protected class PlaceClaimVoter
        {
            public static long NowMs => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;            
            protected struct PlaceData 
            {
                public int x;
                public int z;
                public string bikeId;
            }

            protected struct VoteData
            {
                public const long timeoutMs = 500;
                public int neededVotes;
                public long expireTs;
                public bool voteDone;
                public List<string> peerIds;
                public VoteData(int voteCnt, long now)
                {
                    neededVotes = voteCnt;
                    expireTs = now + timeoutMs;
                    peerIds = new List<string>();
                    voteDone = false;
                }
            }

            protected Dictionary<PlaceData, VoteData> voteDict;
            public UniLogger logger;

            public PlaceClaimVoter(UniLogger _logger) 
            { 
                logger = _logger;
                voteDict = new Dictionary<PlaceData, VoteData>();
            }

            public void Cleanup()
            {
                List<PlaceData> delKeys = voteDict.Keys.Where(k => voteDict[k].expireTs < NowMs).ToList();
                foreach (PlaceData k in delKeys)
                {
                    logger.Debug($"Vote.Cleanup(): removing: ({k.x},{k.z},{k.bikeId}), {voteDict[k].peerIds.Count} votes.");
                    voteDict.Remove(k);
                }
            }

            public bool AddVote(string bikeId, int placeX, int placeZ, string observerPeer, int totalPeers)
            {
                VoteData vd;

                Cleanup();
                PlaceData newPd = new PlaceData(){x=placeX, z=placeZ, bikeId=bikeId};

                if (voteDict.TryGetValue(newPd, out vd))
                {
                    // already had a values
                    if (vd.voteDone)
                        return false;

                    vd.peerIds.Add(observerPeer);
                    logger.Debug($"Vote.Add: +1 for: ({placeX},{placeX},{bikeId}), Votes: {vd.peerIds.Count}");                    
                } else {
                    int majorityCnt = totalPeers / 2 + 1;                    
                    vd = new VoteData(majorityCnt,PlaceClaimVoter.NowMs);
                    vd.peerIds.Add(observerPeer);
                    voteDict[newPd] = vd;
                    logger.Debug($"Vote.Add: New: ({placeX},{placeX},{bikeId}), Majority: {majorityCnt}"); 
                }

                if (vd.peerIds.Count >= vd.neededVotes)
                {
                    vd.voteDone = true;
                    return true;
                }
                return false;

            }
        }

        protected PlaceClaimVoter placeVoter;

        public void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay) 
        {
            if (placeVoter == null) // TODO: should happen in Apian init/ctor
                placeVoter = new PlaceClaimVoter(logger);

            logger.Debug($"OnPlaceClaimObs() - Got ClaimObs from {srcId}. PeerCount: {client.gameData.Peers.Count}");
            if (placeVoter.AddVote(msg.bikeId, msg.xIdx, msg.zIdx, srcId, client.gameData.Peers.Count))
            {
                logger.Debug($"OnPlaceClaimObs() - Calling OnPlaceClaim()");                
                client.OnPlaceClaim(msg, msgDelay);                
            }

        }

        // public void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay) 
        // {
        //     BaseBike b = gameData.GetBaseBike(msg.bikeId);
        //     //  This test is implementing the "trusty" consensus 
        //     // "bike owner is authority" rule. 
        //     // TODO: This is NOT good enough and can result in inconsistency. Even in a trusty Apian a "race to a place" like this
        //     //   requires some sort of inter-peer protocol. 
        //     if (b != null && srcId == b.peerId)
        //     {
        //         client.OnPlaceClaim(msg, msgDelay);
        //     }      
        // }
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