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

    public class BeamGameInstance : IGameInstance, IBeamGameInstance, IBeamApianClient
    {

        public event EventHandler<string> GroupJoinedEvt;
        public event EventHandler<PlayerJoinedArgs> PlayerJoinedEvt;
        public event EventHandler<PlayerLeftArgs> PlayerLeftEvt;

        public BeamGameData GameData {get; private set;}
        public  IBeamFrontend frontend {get; private set;}
        public BeamApian apian {get; private set;}
        public UniLogger logger;
        public BeamPlayer LocalPlayer { get; private set; } = null;
        public string LocalPeerId => apian?.GameNet.LocalP2pId(); // TODO: make LocalP2pId a property?
        public string CurrentGameId  { get; private set; }

        public long CurGameTime {get => apian.CurrentApianTime(); }

        // Not sure where this oughta be. The Loop() methd gets passed a "frameSecs" float that is based on
        // Whatever clck the driver is using. We want everything in the GameInstance to be based on the shared "ApianClock"
        // So - when Loop() is called we are going to read Apian.CurrentApianTime and stash that value to use "next frame"
        // to determine the ApianClock time between frames.
        public long FrameApianTime {get; private set;} = -1;

        // IBeamBackend events

        public event EventHandler PlayersClearedEvt;
        public event EventHandler<IBike> NewBikeEvt;
        public event EventHandler<BikeRemovedData> BikeRemovedEvt;
        public event EventHandler BikesClearedEvt;
        public event EventHandler<Ground.Place> PlaceClaimedEvt;
        public event EventHandler<PlaceHitArgs> PlaceHitEvt;
        public event EventHandler<string> UnknownBikeEvt;

        public event EventHandler ReadyToPlayEvt;
        public event EventHandler RespawnPlayerEvt;

        protected Dictionary<string, Action<BeamMessage>> commandHandlers;

        public BeamGameInstance(IBeamFrontend fep)
        {
            logger = UniLogger.GetLogger("GameInstance");
            //modeMgr = new ModeManager(new BeamModeFactory(), this);
            frontend = fep;
            GameData = new BeamGameData(frontend);
            GameData.Ground.PlaceTimeoutEvt += OnPlaceTimeoutEvt;

            commandHandlers = new  Dictionary<string, Action<BeamMessage>>()
            {
                [BeamMessage.kNewPlayer] = (msg) => OnNewPlayer(msg as NewPlayerMsg),
                [BeamMessage.kBikeCreateData] = (msg) => this.OnCreateBike(msg as BikeCreateDataMsg),
                [BeamMessage.kBikeTurnMsg] = (msg) => this.OnBikeTurn(msg as BikeTurnMsg),
                [BeamMessage.kBikeCommandMsg] =(msg) => this.OnBikeCommand(msg as BikeCommandMsg),
                [BeamMessage.kPlaceClaimMsg] = (msg) => this.OnPlaceClaim(msg as PlaceClaimMsg),
                [BeamMessage.kPlaceHitMsg] = (msg) => this.OnPlaceHit(msg as PlaceHitMsg),
                [BeamMessage.kPlaceRemovedMsg] = (msg) => this.OnPlaceRemoved(msg as PlaceRemovedMsg),
            };
        }

        //
        // IGameInstance
        //
        public void Start(int initialMode)
        {

        }

        public void End()
        {
            ClearPlayers();
            ClearBikes();
            ClearPlaces();
        }
        public bool Loop(float frameSecs)
        {
            bool isActive = apian.Update();  // returns "True" if Active

            //
            // Ignore passed in frameSecs.
            //
            long prevFrameApianTime = FrameApianTime;
            FrameApianTime = CurGameTime;
            if (prevFrameApianTime < 0)
            {
                // skip first frame
                return true;
            }

            if (isActive)
            {
                // Don;t call loop if not active
                long apianFrameMs = FrameApianTime - prevFrameApianTime;
                GameData.Loop(FrameApianTime, apianFrameMs);
            }

            return true;
        }

        //
        // IBeamApianClient
        //

        public void SetApianReference(ApianBase ap)
        {
            apian = ap as BeamApian;
        }


        public void OnGroupJoined(string groupId)
        {
            logger.Info($"OnGroupJoined({groupId}) - local peer joined");
            GroupJoinedEvt?.Invoke(this, groupId);
        }

        public void OnApianCommand(ApianCommand cmd)
        {
            commandHandlers[cmd.ClientMsg.MsgType](cmd.ClientMsg as BeamMessage);
        }

        public void OnNewPlayer(NewPlayerMsg msg)
        {
            BeamPlayer newPlayer = msg.newPlayer;
            logger.Info($"OnNewPlayer() {((newPlayer.PeerId == LocalPeerId)?"Local":"Remote")} name: {newPlayer.Name}");
            _AddPlayer(newPlayer);
        }

        public void OnPlayerLeft(string p2pId) // TODO: needs command!!!
        {
            logger.Info($"OnPlayerLeft({p2pId})");
            _RemovePlayer(p2pId);
        }

        public void OnCreateBike(BikeCreateDataMsg msg)
        {
            logger.Verbose($"OnBikeCreateData(): {msg.bikeId}.");
            IBike ib = msg.ToBike(this);
            if (_AddBike(ib))
            {
                // *** Bikes are created stationary now - so there's no need to correct for creation time delay
                //float elapsedSecs = (CurGameTime - msg.TimeStamp) * .001f;
                logger.Verbose($"OnCreateBike() created {ib.bikeId}");
                //ib.Loop(elapsedSecs); // project to NOW
            }
        }

        public void OnBikeCommand(BikeCommandMsg msg)
        {
            BaseBike bb = GameData.GetBaseBike(msg.bikeId);
            // Caller (Apian) checks the bike and message sourcevalidity
            // TODO: Even THIS code should check to see if the upcoming place is correct and fix things otherwise
            // I don;t think the bike's internal code should do anythin glike that in ApplyCommand()
            logger.Debug($"OnBikeCommand({msg.cmd}): Bike:{msg.bikeId}");
            float elapsedSecs = ((float)CurGameTime - msg.TimeStamp) *.001f; // float secs
            bb.ApplyCommand(msg.cmd, new Vector2(msg.nextPtX, msg.nextPtZ), elapsedSecs);
        }

        public void OnBikeTurn(BikeTurnMsg msg)
        {
            BaseBike bb = GameData.GetBaseBike(msg.bikeId);
            // Code (Apian) checks bike and source validity
            // TODO: Even THIS code should check to see if the upcoming place is correct and fix things otherwise
            // I don;t think the bike's internal code should do anythin glike that in ApplyCommand()
            logger.Debug($"OnBikeTurnMsg({msg.dir}): Bike:{msg.bikeId}");
            float elapsedSecs = ((float)CurGameTime - msg.TimeStamp) *.001f; // float secs
            bb.ApplyTurn(msg.dir, new Vector2(msg.nextPtX, msg.nextPtZ), elapsedSecs, msg.bikeState);
        }

        public void OnPlaceClaim(PlaceClaimMsg msg)
        {
            // Apian has said this message is authoritative
            BaseBike b = GameData.GetBaseBike(msg.bikeId);
            if (GameData.Ground.IndicesAreOnMap(msg.xIdx, msg.zIdx))
            {
                // Claim it
                Ground.Place p = GameData.Ground.ClaimPlace(b, msg.xIdx, msg.zIdx, msg.TimeStamp+Ground.kPlaceLifeTimeMs);
                if (p != null)
                {
                    logger.Verbose($"OnPlaceClaim() Bike: {b.bikeId} claimed ({msg.xIdx},{msg.zIdx}) at {msg.TimeStamp}");
                    logger.Debug($"               FrameApianTime: {FrameApianTime} ");
                    OnScoreEvent(b, ScoreEvent.kClaimPlace, p);
                    PlaceClaimedEvt?.Invoke(this, p);
                } else {
                    logger.Warn($"OnPlaceClaim()) failed. Place already claimed.");
                }

            } else {
                // Oh oh. It's an "off the map" notification
                OnScoreEvent(b, ScoreEvent.kOffMap, null);   // _RemoveBike() will raise BikeRemoved event
            }
        }

        public void OnPlaceHit(PlaceHitMsg msg)
        {
            // Apian has already checked the the place is claimed and the bike exists
            Vector2 pos = Ground.Place.PlacePos(msg.xIdx, msg.zIdx);
            Ground.Place p = GameData.Ground.GetPlace(pos);
            BaseBike hittingBike = GameData.GetBaseBike(msg.bikeId);
            logger.Verbose($"OnPlaceHit() Bike: {hittingBike?.bikeId} hit ({p?.xIdx},{p?.zIdx})");
            OnScoreEvent(hittingBike, p.bike.team == hittingBike.team ? ScoreEvent.kHitFriendPlace : ScoreEvent.kHitEnemyPlace, p);
            PlaceHitEvt?.Invoke(this, new PlaceHitArgs(p, hittingBike));
        }

        public void OnPlaceRemoved(PlaceRemovedMsg msg)
        {
            Ground.Place p = GameData.Ground.GetPlace(msg.xIdx, msg.zIdx);
            logger.Verbose($"OnPlaceRemoved() ({p?.xIdx},{p?.zIdx})");
            GameData.Ground.RemoveActivePlace(p);
        }

        //
        // IBeamBackend (requests from the frontend)
        //

        public void RaiseReadyToPlay() => ReadyToPlayEvt?.Invoke(this, EventArgs.Empty); // GameCode -> FE
        public void RaiseRespawnPlayer() => RespawnPlayerEvt?.Invoke(this, EventArgs.Empty); // FE -> GameCode
        public Ground GetGround() => GameData.Ground;

        // public void OnSwitchModeReq(int newModeId, object modeParam)
        // {
        //    logger.Error("backend.OnSwitchModeReq() not working");
        //    // &&&&&modeMgr.SwitchToMode(newModeId, modeParam);
        // }

        // A couple of these are just acting as intermediaries to commands in GameNet that could potentially be called by the frontend
        // directly - if the particular FE code had a reference to the GameNet. It's a lot more likely to  have an IBackend ref.
        // I'm not completely convinced this is the best way to handle it.

        public void PostBikeCreateData(IBike ib, string destId = null)
        {
            logger.Info($"PostBikeCreateData(): {ib.bikeId}");
            apian.SendBikeCreateReq(ib, destId);
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
                    GameData.Bikes.Values.Where( b => b != bike && b.team == place.bike.team);  // Bikes other the "bike" on affected team
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
        protected bool _AddPlayer(BeamPlayer p)
        {
            logger.Debug($"_AddPlayer(). Name: {p.Name} ID: {p.PeerId}");
            if  ( GameData.Players.ContainsKey(p.PeerId))
            {
                logger.Warn($"_AddPlayer(). Player already exists!!!!");
                return false;
            }

            GameData.Players[p.PeerId] = p;
            if (p.PeerId == LocalPeerId )
                LocalPlayer = p;
            PlayerJoinedEvt.Invoke(this, new PlayerJoinedArgs(CurrentGameId, p));
            return true;
        }

        protected bool _RemovePlayer(string p2pId)
        {
            if  (!GameData.Players.ContainsKey(p2pId))
                return false;

            PlayerLeftEvt?.Invoke(this, new PlayerLeftArgs(CurrentGameId, p2pId));

            foreach (IBike ib in GameData.LocalBikes(p2pId))
                _RemoveBike(ib, true); // Blow em up just for yuks.

            GameData.Players.Remove(p2pId);
            return true;
        }

        public void ClearPlayers()
        {
            PlayersClearedEvt?.Invoke(this, EventArgs.Empty);
            GameData.Players.Clear();
        }

        // Bike-related

        public BaseBike CreateBaseBike(string ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( this.GameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            return  new BaseBike(this, bikeId, peerId, name, t, ctrlType, pos, heading);
        }

        public bool _AddBike(IBike ib)
        {
            logger.Verbose($"_AddBike(): {ib.bikeId}");

            if (GameData.GetBaseBike(ib.bikeId) != null)
                return false;

            GameData.Bikes[ib.bikeId] = ib;

            NewBikeEvt?.Invoke(this, ib);
            return true;
        }

        protected void _RemoveBike(IBike ib, bool shouldBlowUp=true)
        {
            logger.Verbose($"_RemoveBike(): {ib.bikeId}");
            GameData.Ground.RemovePlacesForBike(ib);
            BikeRemovedEvt?.Invoke(this, new BikeRemovedData(ib.bikeId,  shouldBlowUp));
            GameData.PostBikeRemoval(ib.bikeId); // we're almost certainly iterating over the list of bikes so don;t remove it yet.
        }
        public void ClearBikes()
        {
            BikesClearedEvt?.Invoke(this, EventArgs.Empty);
            GameData.Bikes.Clear();
        }

       // Ground-related
        public void OnPlaceTimeoutEvt(object sender, Ground.Place p)
        {
            apian.SendPlaceRemovedObs(p.xIdx, p.zIdx);
        }

        public void ClearPlaces()
        {
            GameData.Ground.ClearPlaces(); // ground notifies FE.
        }

    }

}