using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using GameModeMgr;
using UnityEngine;
using GameNet;
using Apian;
using UniLog;

namespace BeamBackend
{

    public class BeamGameInstance : IGameInstance, IBeamBackend, IBeamApianClient
    {
        public ModeManager modeMgr {get; private set;}
        public  BeamGameData gameData {get; private set;}
        public  IBeamFrontend frontend {get; private set;}
        public  IBeamGameNet gameNet {get; private set;}
        public BeamApian apian {get; private set;}
        public UniLogger logger;
        public BeamPeer LocalPeer { get; private set; } = null;
        public string LocalPeerId => LocalPeer?.PeerId;
        public string CurrentGameId  { get; private set; }

        public long CurGameTime {get => gameNet.CurrentApianTime(); }

        // Not sure where this oughta be. The Loop() methd gets passed a "frameSecs" float that is based on
        // Whatever clck the driver is using. We want everything in the GameInstance to be based on the shared "ApianClock"
        // So - when Loop() is called we are going to read GameNet.CurrentApianTime() and stash that value to use "next fraem"
        // to determine the ApianClock time between frames.
        public long FrameApianTime {get; private set;} = -1;

        // IBeamBackend events
        public event EventHandler<string> GameCreatedEvt; // game channel
        public event EventHandler<PeerJoinedGameArgs> PeerJoinedGameEvt;
        public event EventHandler<PeerLeftGameArgs> PeerLeftGameEvt;
        public event EventHandler PeersClearedEvt;
        public event EventHandler<IBike> NewBikeEvt;
        public event EventHandler<BikeRemovedData> BikeRemovedEvt;
        public event EventHandler BikesClearedEvt;
        public event EventHandler<Ground.Place> PlaceClaimedEvt;
        public event EventHandler<PlaceHitArgs> PlaceHitEvt;
        public event EventHandler<string> UnknownBikeEvt;

        public event EventHandler ReadyToPlayEvt;
        public event EventHandler RespawnPlayerEvt;

        protected Dictionary<string, Action<BeamMessage, long>> assertionHandlers;

        public BeamGameInstance(IBeamFrontend fep, BeamGameNet bgn)
        {
            logger = UniLogger.GetLogger("GameInstance");
            modeMgr = new ModeManager(new BeamModeFactory(), this);
            frontend = fep;
            gameNet = bgn;
            gameData = new BeamGameData(frontend);

            assertionHandlers = new  Dictionary<string, Action<BeamMessage, long>>()
            {
                [BeamMessage.kBikeCreateData] = (msg,dly) => this.OnCreateBike(msg as BikeCreateDataMsg, dly),
                [BeamMessage.kBikeTurnMsg] = (msg,dly) => this.OnBikeTurn(msg as BikeTurnMsg, dly),
                [BeamMessage.kBikeCommandMsg] =(msg,dly) => this.OnBikeCommand(msg as BikeCommandMsg, dly),
                [BeamMessage.kPlaceClaimMsg] = (msg,dly) => this.OnPlaceClaim(msg as PlaceClaimMsg, dly),
                [BeamMessage.kPlaceHitMsg] = (msg,dly) => this.OnPlaceHit(msg as PlaceHitMsg, dly),

            };
        }

        public void SetApianReference(ApianBase ap)
        {
            apian = ap as BeamApian;
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
            // Ignore passed in framesecs.
            long prevFrameApianTime = FrameApianTime;
            FrameApianTime = gameNet.CurrentApianTime();
            if (prevFrameApianTime < 0)
            {
                // skip first frame
                return true;
            }

            float apianFrameSecs = (FrameApianTime - prevFrameApianTime) / 1000f;

            //logger.Debug("Loop()");
            gameData.Loop(apianFrameSecs);
            // TODO: gets throttled per-bike by gamenet, but we should probably
            // have it time-based here as well rather than going though
            // the whole thing every frame
            //gameNet.SendBikeUpdates(gameData.LocalBikes(LocalPeerId));

            return modeMgr.Loop(frameSecs); // TODO: I THINK this is OK. manager code can't change instance state
        }

        //
        // IBeamApianClient
        //

        public string LocalPeerData()
        {
            if (LocalPeer == null)
                logger.Warn("LocalPeerData() - no local peer");
            return  JsonConvert.SerializeObject( new NetPeerData(){ peer = LocalPeer });
        }

        public void OnGameCreated(string gameP2pChannel)
        {
            logger.Info($"OnGameCreated({gameP2pChannel}");
            GameCreatedEvt?.Invoke(this, gameP2pChannel);
        }

        public void OnPeerJoinedGame(string p2pId, string gameId, string helloData)
        {
            NetPeerData peerData = JsonConvert.DeserializeObject<NetPeerData>(helloData);
            logger.Info($"OnPeerJoinedGame() {((p2pId == LocalPeerId)?"Local":"Remote")} name: {peerData.peer.Name}");
            if (p2pId == LocalPeerId)
            {
                CurrentGameId = gameId;
            }
            _AddPeer(peerData.peer);
            PeerJoinedGameEvt.Invoke(this, new PeerJoinedGameArgs(gameId, peerData.peer));
        }

        public void OnPeerLeftGame(string p2pId, string gameId)
        {
            logger.Info($"OnPeerLeftGame({p2pId})");
            PeerLeftGameEvt.Invoke(this, new PeerLeftGameArgs(gameId, p2pId));
            _RemovePeer(p2pId);
        }

        public void OnCreateBike(BikeCreateDataMsg msg, long msgDelay)
        {
            logger.Verbose($"OnBikeCreateData(): {msg.bikeId}.");
            IBike ib = msg.ToBike(this);

            float elapsedSecs = ((float)gameNet.CurrentApianTime() - msg.TimeStamp) *.001f; // float secs
            logger.Verbose($"OnCreateBike() projecting bike {ib.bikeId} forward {elapsedSecs} secs.");
            ib.Loop(elapsedSecs); // project to NOW

            if (_AddBike(ib))
            {
                foreach ( BikeCreateDataMsg.PlaceCreateData pData in msg.ownedPlaces)
                {
                    if (gameData.Ground.ClaimPlace(ib, pData.xIdx, pData.zIdx, pData.secsLeft) == null)
                        logger.Warn($"OnBikeCreateData() Claimplace() failed");
                }
            }
        }

        public void OnBikeCommand(BikeCommandMsg msg, long msgDelay)
        {
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            // Caller (Apian) checks the bike and message sourcevalidity
            // TODO: Even THIS code should check to see if the upcoming place is correct and fix things otherwise
            // I don;t think the bike's internal code should do anythin glike that in ApplyCommand()
            logger.Debug($"OnBikeCommand({msg.cmd}): Bike:{msg.bikeId}");
            float elapsedSecs = ((float)gameNet.CurrentApianTime() - msg.TimeStamp) *.001f; // float secs
            bb.ApplyCommand(msg.cmd, new Vector2(msg.nextPtX, msg.nextPtZ), elapsedSecs);
        }

        public void OnBikeTurn(BikeTurnMsg msg, long msgDelay)
        {
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            // Code (Apian) checks bike and source validity
            // TODO: Even THIS code should check to see if the upcoming place is correct and fix things otherwise
            // I don;t think the bike's internal code should do anythin glike that in ApplyCommand()
            logger.Debug($"OnBikeTurnMsg({msg.dir}): Bike:{msg.bikeId}");
            float elapsedSecs = ((float)gameNet.CurrentApianTime() - msg.TimeStamp) *.001f; // float secs
            bb.ApplyTurn(msg.dir, new Vector2(msg.nextPtX, msg.nextPtZ), elapsedSecs, msg.bikeState);
        }

        public void OnPlaceClaim(PlaceClaimMsg msg, long msgDelay)
        {
            // Apian has said this message is authoritative
            BaseBike b = gameData.GetBaseBike(msg.bikeId);
            if (gameData.Ground.IndicesAreOnMap(msg.xIdx, msg.zIdx))
            {
                // Claim it
                Ground.Place p = gameData.Ground.ClaimPlace(b, msg.xIdx, msg.zIdx);
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

        public void OnPlaceHit(PlaceHitMsg msg, long msgDelay)
        {
            // Apian has already checked the the place is claimed and the bike exists
            Vector2 pos = Ground.Place.PlacePos(msg.xIdx, msg.zIdx);
            Ground.Place p = gameData.Ground.GetPlace(pos);
            BaseBike hittingBike = gameData.GetBaseBike(msg.bikeId);
            OnScoreEvent(hittingBike, p.bike.team == hittingBike.team ? ScoreEvent.kHitFriendPlace : ScoreEvent.kHitEnemyPlace, p);
            PlaceHitEvt?.Invoke(this, new PlaceHitArgs(p, hittingBike));
        }

        public void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay)
        {
            IBike ib = gameData.GetBaseBike(msg.bikeId);
            logger.Debug($"OnRemoteBikeUpdate() - updating remote bike: {msg.bikeId}. TS: {msg.TimeStamp}");
            gameData.GetBaseBike(msg.bikeId).ApplyUpdate(new Vector2(msg.xPos, msg.yPos), msg.speed, msg.heading, msg.score, msg.TimeStamp);
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

        // A couple of these are just acting as intermediaries to commands in GameNet that could potentially be called by the frontend
        // directly - if the particular FE code had a reference to the GameNet. It's a lot more likely to  have an IBackend ref.
        // I'm not completely convinced this is the best way to handle it.

        public void PostBikeCreateData(IBike ib, string destId = null)
        {
            List<Ground.Place> places = gameData.Ground.PlacesForBike(ib);
            logger.Info($"PostBikeCreateData(): {places.Count} places for {ib.bikeId}");
            apian.SendBikeCreateReq(ib, places, destId);
        }

        public void PostBikeCommand(IBike bike, BikeCommand cmd)
        {
            apian.SendBikeCommandReq(bike, cmd, (bike as BaseBike).UpcomingGridPoint());
        }

       public void PostBikeTurn(IBike bike, TurnDir dir)
        {
            Vector2 nextPt = (bike as BaseBike).UpcomingGridPoint();

            float dx = Vector2.Distance(bike.position, nextPt);
            if (dx < BaseBike.length * .5f)
                logger.Debug($"PostBikeTurn(): Bike too close to turn: {dx} < {BaseBike.length * .5f}");
            else
                apian.SendBikeTurnReq(bike, dir, nextPt);
        }

        protected void OnScoreEvent(BaseBike bike, ScoreEvent evt, Ground.Place place)
        {
            // TODO: as with above: This is coming from the backend (BaseBike, mostly) and should
            // be comming from the Net/event/whatever layer
            // NOTE: I'm not so sure about above comment. It;'s not clear that score changes constitute "events"
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

        //  informational
        public void OnUnknownBike(string bikeId, string srcId)
        {
            UnknownBikeEvt?.Invoke(this, bikeId);
        }

        // Peer-related
        protected bool _AddPeer(BeamPeer p)
        {
            logger.Debug($"AddPeer(). Name: {p.Name} ID: {p.PeerId}");
            if  ( gameData.Peers.ContainsKey(p.PeerId))
                return false;

            return true;
        }

        protected bool _RemovePeer(string p2pId)
        {
            if  (!gameData.Peers.ContainsKey(p2pId))
                return false;

            PeerLeftGameEvt?.Invoke(this, new PeerLeftGameArgs(CurrentGameId, p2pId));

            foreach (IBike ib in gameData.LocalBikes(p2pId))
                _RemoveBike(ib, true); // Blow em up just for yuks.

            gameData.Peers.Remove(p2pId);
            return true;
        }

        public void ClearPeers()
        {
            PeersClearedEvt?.Invoke(this, EventArgs.Empty);
            gameData.Peers.Clear();
        }

        // Bike-related

        public BaseBike CreateBaseBike(string ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( this.gameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            return  new BaseBike(this, bikeId, peerId, name, t, ctrlType, pos, heading, BaseBike.defaultSpeed);
        }

        public bool _AddBike(IBike ib)
        {
            logger.Verbose($"_AddBike(): {ib.bikeId}");

            if (gameData.GetBaseBike(ib.bikeId) != null)
                return false;

            gameData.Bikes[ib.bikeId] = ib;

            NewBikeEvt?.Invoke(this, ib);
            return true;
        }

        protected void _RemoveBike(IBike ib, bool shouldBlowUp=true)
        {
            logger.Verbose($"_RemoveBike(): {ib.bikeId}");
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

    }

}