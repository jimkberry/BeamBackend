using System.Diagnostics;
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using GameModeMgr;
using UnityEngine;
using GameNet;
using UniLog;

namespace BeamBackend
{
    public class NetPeerData 
    {
        public BeamPeer peer;
    }

    public class BeamGameData
    {
        public Dictionary<string, BeamPeer> Peers { get; private set; } = null;
        public Dictionary<string, IBike> Bikes { get; private set; } = null;
	    public Ground Ground { get; private set; } = null;

        protected List<string> _bikeIdsToRemoveAfterLoop; // at end of Loop() any bikes listed here get removed
        public BeamGameData(IBeamFrontend fep)
        {
            Peers = new Dictionary<string, BeamPeer>();
            Bikes = new Dictionary<string, IBike>();
            Ground = new Ground(fep);    
            _bikeIdsToRemoveAfterLoop = new List<string>();          
        }

        public BeamPeer GetPeer(string peerId)
        {
            try { return Peers[peerId];} catch (KeyNotFoundException){ return null;} 
        }

        public BaseBike GetBaseBike(string bikeId)
        {
            try { return Bikes[bikeId] as BaseBike;} catch (KeyNotFoundException){ return null;}
        }

        public void PostBikeRemoval(string bikeId) => _bikeIdsToRemoveAfterLoop.Add(bikeId);

        public void Init() 
        {
            Peers.Clear();
            Bikes.Clear();
        }

        public void Loop(float frameSecs)
        {
            Ground.Loop(frameSecs);
            foreach( IBike ib in Bikes.Values)
                ib.Loop(frameSecs);  // Bike "ib" might get destroyed here and need to be removed

             _bikeIdsToRemoveAfterLoop.RemoveAll( bid => {Bikes.Remove(bid); return true; });

        }

        public IBike ClosestBike(IBike thisBike)
        {  
            return Bikes.Count <= 1 ? null : Bikes.Values.Where(b => b != thisBike)
                    .OrderBy(b => Vector2.Distance(b.position, thisBike.position)).First();
        }   

        public List<Vector2> CloseBikePositions(IBike thisBike, int maxCnt)
        {
            // Todo: this is actually "current enemy pos"         
            return Bikes.Values.Where(b => b != thisBike)
                .OrderBy(b => Vector2.Distance(b.position, thisBike.position)).Take(maxCnt) // IBikes
                .Select(ob => ob.position).ToList();
        }                 
    }

    public class BeamGameInstance : IGameInstance, IBeamBackend, IBeamGameNetClient
    {
        public ModeManager modeMgr {get; private set;}
        public  BeamGameData gameData {get; private set;}
        public  IBeamFrontend frontend {get; private set;}
        public  IBeamGameNet gameNet {get; private set;}        
        public UniLogger logger;
        public BeamPeer LocalPeer { get; private set; } = null;   
        public string LocalPeerId => LocalPeer?.PeerId;
        public string CurrentGameId  { get; private set; }

        public BeamGameInstance(IBeamFrontend fep, BeamGameNet bgn)
        {
            logger = UniLogger.GetLogger("GameInstance");
            modeMgr = new ModeManager(new BeamModeFactory(), this);
            frontend = fep;
            gameNet = bgn;
            gameData = new BeamGameData(frontend);            
        }

        public void SetLocalPeer(BeamPeer p)
        {
            AddPeer(p);
            LocalPeer = p;
        }
        
        // IGameInstance
        public void Start(int initialMode)
        {
            modeMgr.Start(initialMode);
        }

        public bool Loop(float frameSecs)
        {
            //logger.Debug("Loop()");
            gameData.Loop(frameSecs);
            return modeMgr.Loop(frameSecs);
        }

        //
        // IBeamGameNetClient
        //

        public void SetGameNetInstance(IGameNet iGameNet)
        {
            gameNet = (IBeamGameNet)iGameNet;
        }  


        public void OnGameCreated(string gameP2pChannel)
        {
            logger.Info($"BGI.OnGameCreated({gameP2pChannel}");          
            modeMgr.DispatchCmd( new GameCreatedMsg(gameP2pChannel));
        }
        public void OnGameJoined(string gameId, string localP2pId)
        {
            CurrentGameId = gameId;
            logger.Info($"OnGameJoined({gameId}, {localP2pId})");  
            modeMgr.DispatchCmd(new GameJoinedMsg(gameId, localP2pId));                      
        }
        public void OnPeerJoined(string p2pId, string helloData)
        {
            logger.Info($"OnPeerJoined(): helloData: {helloData})"); 
            NetPeerData remoteData = JsonConvert.DeserializeObject<NetPeerData>(helloData);    
            logger.Info($"OnPeerJoined(name: {remoteData.peer.Name})");        
            AddPeer(remoteData.peer);
            modeMgr.DispatchCmd(new PeerJoinedMsg(remoteData.peer));            
        }
        public void OnPeerLeft(string p2pId)
        {
            logger.Info($"BGI.OnPeerLeft({p2pId})");   
            modeMgr.DispatchCmd(new PeerLeftMsg(gameData.GetPeer(p2pId)));                     
            RemovePeer(p2pId);                        
        }
        public void OnP2pMsg(string from, string to, string payload)
        {
            logger.Info($"BGI.OnP2pMsg from {from}");            
        }
        public string LocalPeerData()
        {
            if (LocalPeer == null)
                logger.Error("LocalPeerData() - no local peer");
            return  JsonConvert.SerializeObject( new NetPeerData(){ peer = LocalPeer });
        }       

        //
        // IBeamBackend (requests from the frontend)
        // 

        public void OnSwitchModeReq(int newModeId, object modeParam)
        {
           modeMgr.SwitchToMode(newModeId, modeParam);       
        }

        public void OnTurnReq(string bikeId, TurnDir turn)
        {
            // TODO: In real life, this message from the FE should get sent to the net layer
            // and looped back, rather than directly talking to the bike
            //UnityEngine.Debug.Log(string.Format("Backend.OnTurnReq({0}, {1})", bikeId, turn));               
            gameData.GetBaseBike(bikeId)?.PostPendingTurn(turn);            
        }       

        //
        // Messages from the network/consensus layer (external or internal loopback)
        //

        public void OnNewBikeReq(IBike ib)
        {
            // TODO: Where does this come from? I think is was supposed to be the FE, which means this
            // should just go away.
            UnityEngine.Debug.Log(string.Format("** need to implement BeamGameInst.OnNewBikeReq()"));             
        }


        public void OnPlaceClaim(string bikeId, Vector2 pos)
        {
            // TODO: should be coming from net, instead of the backend
            BaseBike b = (BaseBike)gameData.Bikes[bikeId];            
            Ground.Place p = gameData.Ground.ClaimPlace(b, pos);
            // Ground sends message to FE when place s claimed
        }


        public void OnScoreEvent(BaseBike bike, ScoreEvent evt, Ground.Place place)
        {
            // TODO: as with above: This is coming from the backend (BaseBike, mostly) and should
            // be comming from the Net/event/whatever layer
            int scoreDelta = GameConstants.eventScores[(int)evt];
            bike.score += scoreDelta;

            if (evt == ScoreEvent.kHitEnemyPlace || evt == ScoreEvent.kHitFriendPlace)
            {
                logger.Debug($"OnScoreEvent(). Bike: {bike.bikeId} Event: {evt}");

                // half of the deduction goes to the owner of the place, the rest is divded 
                // among the owner's team 
                // UNLESS: the bike doing the hitting IS the owner - then the rest of the team just splits it
                if (bike != place.bike) {
                    scoreDelta /= 2;
                    place.bike.score -= scoreDelta; // adds
                }

                IEnumerable<IBike> rewardedOtherBikes = 
                    gameData.Bikes.Values.Where( b => b != bike && b.team == place.bike.team);  // Bikes other the "bike" on affected team
                if (rewardedOtherBikes.Count() > 0)
                {
                    foreach (BaseBike b  in rewardedOtherBikes) 
                        b.score -= scoreDelta / rewardedOtherBikes.Count();
                }
            }

            if (evt == ScoreEvent.kOffMap || bike.score <= 0)
            {
                bike.score = 0;
                RemoveBike(bike);
            }
        }

        //
        // Hmm. Where do these go?
        //

        // Peer-related
        public bool AddPeer(BeamPeer p)
        {
            logger.Debug($"AddPeer(). Name: {p.Name} ID: {p.PeerId}");            
            if  ( gameData.Peers.ContainsKey(p.PeerId))
                return false;  

            gameData.Peers[p.PeerId] = p;
            frontend?.OnNewPeer(p);
            return true;
        }

        public bool RemovePeer(string p2pId)
        {
            if  ( gameData.Peers.ContainsKey(p2pId))
                return false;  
            frontend?.OnPeerLeft(gameData.Peers[p2pId]);                
            gameData.Peers.Remove(p2pId);
            return true;
        }

        public void ClearPeers()
        {
            frontend?.OnClearPeers();     
            gameData.Peers.Clear();
        }

        // Bike-related
        public void NewBike(IBike b)
        {
            logger.Debug(string.Format("NewBike(). ID: {0}, Pos: {1}", b.bikeId, b.position));            
            gameData.Bikes[b.bikeId] = b;
            frontend?.OnNewBike(b);
        }        

        public void RemoveBike(IBike ib, bool shouldBlowUp=true)
        {
            gameData.Ground.RemovePlacesForBike(ib);
            frontend?.OnBikeRemoved(ib.bikeId, shouldBlowUp);  
            gameData.PostBikeRemoval(ib.bikeId); // we're almost certainly iterating over the list of bikes so don;t remove it yet.
        }
        public void ClearBikes()
        {
            frontend?.OnClearBikes();
            gameData.Bikes.Clear();
        }

       // Ground-related
        public void ClearPlaces()
        {           
            gameData.Ground.ClearPlaces(); // ground notifies FE.
        }

        // Info    

    }

}