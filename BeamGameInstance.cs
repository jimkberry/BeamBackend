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

        public List<IBike> LocalBikes(string peerId)
        {
            return Bikes.Values.Where(ib => ib.peerId == peerId).ToList();
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

        // IBeamBackend events
        public event EventHandler<string> GameCreatedEvt; // game channel
        public event EventHandler<GameJoinedArgs> GameJoinedEvt;     
        public event EventHandler<BeamPeer> PeerJoinedEvt;    
        public event EventHandler<string> PeerLeftEvt; // peer p2pId
        public event EventHandler PeersClearedEvt;
        public event EventHandler<IBike> NewBikeEvt;   
        public event EventHandler<BikeRemovedData> BikeRemovedEvt; 
        public event EventHandler BikesClearedEvt;      
        public event EventHandler<Ground.Place> PlaceClaimedEvt;
        public event EventHandler<PlaceHitArgs> PlaceHitEvt;          

        public event EventHandler ReadyToPlayEvt;
        public event EventHandler RespawnPlayerEvt;        

        public BeamGameInstance(IBeamFrontend fep, BeamGameNet bgn)
        {
            logger = UniLogger.GetLogger("GameInstance");
            modeMgr = new ModeManager(new BeamModeFactory(), this);
            frontend = fep;
            gameNet = bgn;
            gameData = new BeamGameData(frontend);            
        }

        public void AddLocalPeer(BeamPeer p)
        {
            LocalPeer = p;            
            _AddPeer(p);
        }
        

        //
        // IGameInstance
        //
        public void Start(int initialMode)
        {
            modeMgr.Start(initialMode);
        }

        public bool Loop(float frameSecs)
        {
            //logger.Debug("Loop()");
            gameData.Loop(frameSecs);
            // TODO: gets throttled per-bike by gamenet, but we should probably
            // have it time-based here as well rather than going though
            // the whole thing every frame
            gameNet.SendBikeUpdates(gameData.LocalBikes(LocalPeerId));
            
            return modeMgr.Loop(frameSecs);
        }

        //
        // IBeamGameNetClient
        //

        public void SetGameNetInstance(IGameNet iGameNet)
        {
            gameNet = (IBeamGameNet)iGameNet;
        }  

        public string LocalPeerData()
        {
            if (LocalPeer == null)
                logger.Error("LocalPeerData() - no local peer");
            return  JsonConvert.SerializeObject( new NetPeerData(){ peer = LocalPeer });
        }         

        public void OnGameCreated(string gameP2pChannel)
        {
            logger.Info($"BGI.OnGameCreated({gameP2pChannel}"); 
            GameCreatedEvt?.Invoke(this, gameP2pChannel);
        }
        public void OnGameJoined(string gameId, string localP2pId)
        {
            logger.Info($"OnGameJoined({gameId}, {localP2pId})");  
            CurrentGameId = gameId;
            GameJoinedEvt?.Invoke(this, new GameJoinedArgs(gameId, localP2pId));                  
        }
        public void OnPeerJoined(string p2pId, string helloData)
        {  
            NetPeerData remoteData = JsonConvert.DeserializeObject<NetPeerData>(helloData);    
            logger.Info($"OnPeerJoined(name: {remoteData.peer.Name})");   
            _AddPeer(remoteData.peer);                             
        }
        public void OnPeerLeft(string p2pId)
        {
            logger.Info($"BGI.OnPeerLeft({p2pId})"); 
            _RemovePeer(p2pId);                                            
        }
      
         public void OnBikeCreateData(BikeCreateDataMsg msg, string srcId)
        {
            if ( gameData.GetBaseBike(msg.bikeId) != null)
            {
                logger.Info($"OnBikeCreateData() Bike already exists: {msg.bikeId}.");   
                return;
            }             

            logger.Info($"OnBikeCreateData(): {msg.bikeId}.");
            IBike ib = msg.ToBike(this);             
            _AddBike(ib);
            foreach ( BikeCreateDataMsg.PlaceCreateData pData in msg.ownedPlaces)
            {
                if (gameData.Ground.ClaimPlace(ib, pData.xIdx, pData.zIdx, pData.secsLeft) == null)
                    logger.Warn($"OnBikeCreateData() Claimplace() failed");
            }
        }

        public void OnBikeDataReq(BikeDataReqMsg msg, string srcId)
        {
            logger.Debug($"OnBikeDataReq() - sending data for bike: {msg.bikeId}");            
            IBike ib = gameData.GetBaseBike(msg.bikeId);
            if (ib != null)
                PostBikeCreateData(ib, srcId);
        }

        public void OnBikeCommand(BikeCommandMsg msg, string srcId)
        {
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                logger.Debug($"OnBikeCommand() - requesting data fror unknown bike: {msg.bikeId}");
                gameNet.RequestBikeData(msg.bikeId, srcId);
            }
            else
            {
                // Code (Apian) SHOULD check the validity
                // TODO: Even THIS code should check to see if the upcoming place is correct and fix things otherwise
                // I don;t think the bike's internal code should do anythin glike that in ApplyCommand()
                logger.Debug($"OnBikeCommand({msg.cmd}): Bike:{msg.bikeId}");
                bb.ApplyCommand(msg.cmd, new Vector2(msg.nextPtX, msg.nextPtZ));
            }
        }

        public void OnBikeTurn(BikeTurnMsg msg, string srcId)
        {
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                logger.Debug($"OnBikeTurnCommand() - requesting data fror unknown bike: {msg.bikeId}");
                gameNet.RequestBikeData(msg.bikeId, srcId);
            }
            else
            {
                // Code (Apian) SHOULD check the validity
                // TODO: Even THIS code should check to see if the upcoming place is correct and fix things otherwise
                // I don;t think the bike's internal code should do anythin glike that in ApplyCommand()
                logger.Debug($"OnBikeTurnCommand({msg.dir}): Bike:{msg.bikeId}");
                bb.ApplyTurn(msg.dir, new Vector2(msg.nextPtX, msg.nextPtZ));
            }
        }

        public void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId)
        {
            IBike ib = gameData.GetBaseBike(msg.bikeId);
            if (ib == null)
            {
                logger.Debug($"OnRemoteBikeUpdate() - requesting data fror unknown bike: {msg.bikeId}");
                gameNet.RequestBikeData(msg.bikeId, srcId);
            }
            else
            {
                logger.Debug($"OnRemoteBikeUpdate() - updating remote bike: {msg.bikeId}");
                gameData.GetBaseBike(msg.bikeId).ApplyUpdate(new Vector2(msg.xPos, msg.yPos), msg.speed, msg.heading, msg.score);
            }
        }

        //
        // IBeamBackend (requests from the frontend)
        // 

        public void RaiseReadyToPlay() => ReadyToPlayEvt?.Invoke(this, EventArgs.Empty); // GameCode -> FE
        public void RaiseRespawnPlayer() => RespawnPlayerEvt?.Invoke(this, EventArgs.Empty); // FE -> GameCode
        public Ground GetGround() => gameData.Ground;

        public void OnSwitchModeReq(int newModeId, object modeParam)
        {
           modeMgr.SwitchToMode(newModeId, modeParam);       
        }
     

        public void PostBikeCommand(IBike bike, BikeCommand cmd)
        {
            gameNet.SendBikeCommandMsg(bike, cmd, (bike as BaseBike).UpcomingGridPoint(Ground.gridSize));
        }

       public void PostBikeTurn(IBike bike, TurnDir dir)
        {
            Vector2 nextPt = (bike as BaseBike).UpcomingGridPoint(Ground.gridSize);

            float dx = Vector2.Distance(bike.position, nextPt);
            if (dx < BaseBike.length * .5f)
                logger.Debug($"PostBikeTurn(): Bike too close to turn: {dx} < {BaseBike.length * .5f}");
            else
                gameNet.SendBikeTurnMsg(bike, dir, nextPt);
        }

        public void PostBikeCreateData(IBike ib, string destId = null)
        {
            List<Ground.Place> places = gameData.Ground.PlacesForBike(ib);
            //logger.Info($"PostBikeCreateData(): {places.Count} places for {ib.bikeId}");
            gameNet.SendBikeCreateData(ib, places, destId);            
        }

        //
        // Messages from the network/consensus layer (external or internal loopback)
        //

        public void OnPlaceClaimed(PlaceClaimReportMsg msg, string srcId)
        {
            BaseBike b = gameData.GetBaseBike(msg.bikeId);
            // TODO: This test is implementing the "trusty" consensus 
            // "bike owner is authority" rule. 
            // &&&& IT SHOULD NOT HAPPEN HERE!!!! Apian should have taken care of it and the
            // call only made if it passed
            if (b != null && srcId == b.peerId)
            {
                Vector2 pos = new Vector2(msg.xPos, msg.zPos);
                if (gameData.Ground.PointIsOnMap(pos))
                {
                    // Claim it                
                    Ground.Place p = gameData.Ground.ClaimPlace(b, pos);
                    if (p != null)
                    {
                        OnScoreEvent(b, ScoreEvent.kClaimPlace, p);
                        PlaceClaimedEvt?.Invoke(this, p);                                                                      
                    } else {
                        logger.Warn($"OnPlaceClaimed() failed. Place already claimed.");
                    }

                } else {
                    // Oh oh. It's an "off the map" notification
                    OnScoreEvent(b, ScoreEvent.kOffMap, null);   // _RemoveBike() will raise BikeRemoved event                  
                }
            }

            // Ground sends message to FE when place s claimed
            // TODO: No, don;t have the ground do it.
        }

        public void OnPlaceHit(PlaceHitReportMsg msg, string srcId)
        {
            // TODO: This test is implementing the "trusty" consensus 
            // "place owner is authority" rule. 
            // &&&& IT SHOULD NOT HAPPEN HERE!!!! Apian should have taken care of it and the
            // call only made if it passed
            Vector2 pos = Ground.Place.PlacePos(msg.xIdx, msg.zIdx);
            Ground.Place p = gameData.Ground.GetPlace(pos);
            if (p != null && srcId == p.bike.peerId)
            {
                BaseBike hittingBike = gameData.GetBaseBike(msg.bikeId);
                // Source of message is place owner (well, owner of the bike that owns the place...)
                if (hittingBike != null)
                {
                    OnScoreEvent(hittingBike, p.bike.team == hittingBike.team ? ScoreEvent.kHitFriendPlace : ScoreEvent.kHitEnemyPlace, p);
                    PlaceHitEvt?.Invoke(this, new PlaceHitArgs(p, hittingBike));                
                } else {
                    logger.Warn($"PlaceHit sent by {srcId} for unknown bike {msg.bikeId}");
                }

            }
        } 

        public void OnScoreEvent(BaseBike bike, ScoreEvent evt, Ground.Place place)
        {
            // TODO: as with above: This is coming from the backend (BaseBike, mostly) and should
            // be comming from the Net/event/whatever layer
            int scoreDelta = GameConstants.eventScores[(int)evt];
            bike.AddScore(scoreDelta);

            if (evt == ScoreEvent.kHitEnemyPlace || evt == ScoreEvent.kHitFriendPlace)
            {
                logger.Debug($"OnScoreEvent(). Bike: {bike.bikeId} Event: {evt}");

                // half of the deduction goes to the owner of the place, the rest is divded 
                // among the owner's team 
                // UNLESS: the bike doing the hitting IS the owner - then the rest of the team just splits it
                if (bike != place.bike) {
                    scoreDelta /= 2;
                    place.bike.AddScore(-scoreDelta); // adds
                }

                IEnumerable<IBike> rewardedOtherBikes = 
                    gameData.Bikes.Values.Where( b => b != bike && b.team == place.bike.team);  // Bikes other the "bike" on affected team
                if (rewardedOtherBikes.Count() > 0)
                {
                    foreach (BaseBike b  in rewardedOtherBikes) 
                        b.AddScore(-scoreDelta / rewardedOtherBikes.Count());
                }
            }

            if (evt == ScoreEvent.kOffMap || bike.score <= 0)
            {
                bike.score = 0;
                _RemoveBike(bike);
            }
        }

        //
        // Hmm. Where do these go?
        //

        // Peer-related
        protected bool _AddPeer(BeamPeer p)
        {
            logger.Debug($"AddPeer(). Name: {p.Name} ID: {p.PeerId}");            
            if  ( gameData.Peers.ContainsKey(p.PeerId))
                return false;  

            gameData.Peers[p.PeerId] = p;
            PeerJoinedEvt?.Invoke(this, p);

            return true;
        }

        protected bool _RemovePeer(string p2pId)
        {
            if  (!gameData.Peers.ContainsKey(p2pId))
                return false;              

            PeerLeftEvt?.Invoke(this, p2pId);                                        
            gameData.Peers.Remove(p2pId);
            return true;
        }

        public void ClearPeers()
        {
            PeersClearedEvt?.Invoke(this, EventArgs.Empty);     
            gameData.Peers.Clear();
        }

        // Bike-related
        // public void NewBike(IBike b)
        // {
        //     logger.Debug(string.Format("NewBike(). ID: {0}, Pos: {1}", b.bikeId, b.position));            
        //     gameData.Bikes[b.bikeId] = b;
        //     frontend?.OnNewBike(b);
        // }        

        public BaseBike CreateBaseBike(int ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( this.gameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );  
            string bikeId = Guid.NewGuid().ToString();
            return  new BaseBike(this, bikeId, peerId, name, t, ctrlType, pos, heading, 0);
        }

        public void _AddBike(IBike ib)
        {
            logger.Info($"AddBike()");    
            
            if (gameData.GetBaseBike(ib.bikeId) != null)
                return;

            gameData.Bikes[ib.bikeId] = ib;   

            // Need to set remote bikes as InActive on creation
            if (ib.peerId != LocalPeerId)
                (ib as BaseBike).SetActive(false);

            NewBikeEvt?.Invoke(this, ib);                      
        }

        protected void _RemoveBike(IBike ib, bool shouldBlowUp=true)
        {
            gameData.Ground.RemovePlacesForBike(ib);
            BikeRemovedEvt?.Invoke(this, new BikeRemovedData(ib.bikeId,  shouldBlowUp));  
            gameData.PostBikeRemoval(ib.bikeId); // we're almost certainly iterating over the list of bikes so don;t remove it yet.
        }
        public void ClearBikes()
        {
            BikesClearedEvt?.Invoke(this, EventArgs.Empty);
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