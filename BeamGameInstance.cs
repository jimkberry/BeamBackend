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

        public BeamGameState GameData {get; private set;}
        public  IBeamFrontend frontend {get; private set;}
        public BeamApian apian {get; private set;}
        public UniLogger logger;
        public BeamPlayer LocalPlayer { get; private set; } = null;
        public string LocalPeerId => apian?.GameNet.LocalP2pId(); // TODO: make LocalP2pId a property?
        public string CurrentGameId  { get; private set; }

        public long NextCheckpointMs;

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
        public event EventHandler<BeamPlace> PlaceClaimedEvt;
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
            GameData = new BeamGameState(frontend);
            GameData.PlaceTimeoutEvt += OnPlaceTimeoutEvt;

            commandHandlers = new  Dictionary<string, Action<BeamMessage>>()
            {
                [BeamMessage.kNewPlayer] = (msg) => OnNewPlayerCmd(msg as NewPlayerMsg),
                [BeamMessage.kPlayerLeft] = (msg) => OnPlayerLeftCmd(msg as PlayerLeftMsg),
                [BeamMessage.kBikeCreateData] = (msg) => this.OnCreateBikeCmd(msg as BikeCreateDataMsg),
                [BeamMessage.kBikeTurnMsg] = (msg) => this.OnBikeTurnCmd(msg as BikeTurnMsg),
                [BeamMessage.kBikeCommandMsg] =(msg) => this.OnBikeCommandCmd(msg as BikeCommandMsg),
                [BeamMessage.kPlaceClaimMsg] = (msg) => this.OnPlaceClaimCmd(msg as PlaceClaimMsg),
                [BeamMessage.kPlaceHitMsg] = (msg) => this.OnPlaceHitCmd(msg as PlaceHitMsg),
                [BeamMessage.kPlaceRemovedMsg] = (msg) => this.OnPlaceRemovedCmd(msg as PlaceRemovedMsg),
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
            //
            // Ignore passed in frameSecs.
            //
            long prevFrameApianTime = FrameApianTime;
            long curApianTime = apian.CurrentApianTime();
            UpdateFrameTime(curApianTime);

            // Might call GameData.Loop() if not active before applying a stashed ApianCommand
            // so FrameApianTime needs to a
            bool isActive = apian.Update();  // returns "True" if Active

            if (isActive) // Don't call loop if not active
            {
                GameData.Loop(FrameApianTime, FrameApianTime - prevFrameApianTime);
            }

            return true;
        }

        public void UpdateFrameTime(long curApianTime)
        {
            FrameApianTime = curApianTime;
        }

         public void OnCheckpointCommand(long seqNum, long timeStamp)
        {
            logger.Info($"OnCheckpointCommand() seqNum: {seqNum}, timestamp: {timeStamp}, Now: {FrameApianTime}");
            string stateJson = GameData.ApianSerialized(new BeamGameState.SerialArgs(seqNum, timeStamp));
            logger.Verbose($"**** Checkpoint:\n{stateJson}\n************\n");
            apian.SendStateCheckpoint(FrameApianTime, seqNum, stateJson);
        }

        //
        // IBeamApianClient
        //

        public void SetApianReference(ApianBase ap)
        {
            apian = ap as BeamApian;
        }

        public void ScheduleStateCheckpoint(long whenMs) => NextCheckpointMs = whenMs;

        public void OnGroupJoined(string groupId)
        {
            logger.Info($"OnGroupJoined({groupId}) - local peer joined");
            GroupJoinedEvt?.Invoke(this, groupId);
        }

        public void OnApianCommand(ApianCommand cmd)
        {
            logger.Debug($"OnApianCommand() Seq#: {cmd.SequenceNum} Cmd: {cmd.CliMsgType}");
            commandHandlers[cmd.ClientMsg.MsgType](cmd.ClientMsg as BeamMessage);
        }

        public void OnNewPlayerCmd(NewPlayerMsg msg)
        {
            BeamPlayer newPlayer = msg.newPlayer;
            logger.Info($"OnNewPlayerCmd() {((newPlayer.PeerId == LocalPeerId)?"Local":"Remote")} name: {newPlayer.Name}");
            _AddPlayer(newPlayer);
        }

        public void OnPlayerLeftCmd(PlayerLeftMsg msg)
        {
            logger.Info($"OnPlayerLeftCmd({msg.peerId})");
            _RemovePlayer(msg.peerId);
        }

        public void OnCreateBikeCmd(BikeCreateDataMsg msg)
        {
            logger.Verbose($"OnCreateBikeCmd(): {msg.bikeId}.");
            IBike ib = msg.ToBike(this);
            logger.Verbose($"** OnCreateBike() created {ib.bikeId} at ({ib.position.x}, {ib.position.y})");
            if (_AddBike(ib))
            {
                // *** Bikes are created stationary now - so there's no need to correct for creation time delay
                logger.Verbose($"OnCreateBike() created {ib.bikeId} at ({ib.position.x}, {ib.position.y})");
            }
        }

        public void OnBikeCommandCmd(BikeCommandMsg msg)
        {
            BaseBike bb = GameData.GetBaseBike(msg.bikeId);
            logger.Verbose($"OnBikeCommandCmd({msg.cmd}) Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike:{msg.bikeId}");
            float elapsedSecs = (FrameApianTime - msg.TimeStamp) *.001f; // float secs
            bb.ApplyCommand(msg.cmd, new Vector2(msg.nextPtX, msg.nextPtZ), elapsedSecs);
        }

        public void OnBikeTurnCmd(BikeTurnMsg msg)
        {
            BaseBike bb = GameData.GetBaseBike(msg.bikeId);
            logger.Verbose($"OnBikeTurnCmd({msg.dir}) Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike:{msg.bikeId}");
            if (bb == null)
                logger.Warn($"OnBikeTurnCmd() Bike:{msg.bikeId} not found!");
            float elapsedSecs = (FrameApianTime - msg.TimeStamp) *.001f; // float secs
            bb.ApplyTurn(msg.dir, new Vector2(msg.nextPtX, msg.nextPtZ), elapsedSecs, msg.bikeState);
        }

        public void OnPlaceClaimCmd(PlaceClaimMsg msg)
        {
            // Apian has said this message is authoritative
            BaseBike b = GameData.GetBaseBike(msg.bikeId);
            if (GameData.Ground.IndicesAreOnMap(msg.xIdx, msg.zIdx))
            {
                if (b == null)
                    logger.Warn($"OnPlaceClaimCmd() Bike:{msg.bikeId} not found!");
                b.UpdatePosFromCommand(msg.TimeStamp, BeamPlace.PlacePos( msg.xIdx, msg.zIdx));

                // Claim it
                BeamPlace p = GameData.ClaimPlace(b, msg.xIdx, msg.zIdx, msg.TimeStamp+BeamPlace.kLifeTimeMs);
                if (p != null)
                {
                    logger.Verbose($"OnPlaceClaimCmd() Bike: {b.bikeId} claimed {BeamPlace.PlacePos( msg.xIdx, msg.zIdx).ToString()} at {msg.TimeStamp}");
                    logger.Verbose($"                  BikePos: {b.position.ToString()}, FrameApianTime: {FrameApianTime} ");
                    logger.Verbose($"   at Timestamp:  BikePos: {b.PosAtTime(msg.TimeStamp).ToString()}, Time: {msg.TimeStamp} ");
                    OnScoreEvent(b, ScoreEvent.kClaimPlace, p);
                    PlaceClaimedEvt?.Invoke(this, p);
                } else {
                    logger.Warn($"OnPlaceClaimCmd()) failed. Place already claimed.");
                }

            } else {
                // Oh oh. It's an "off the map" notification
                OnScoreEvent(b, ScoreEvent.kOffMap, null);   // _RemoveBike() will raise BikeRemoved event
            }
        }

        public void OnPlaceHitCmd(PlaceHitMsg msg)
        {
            // Apian has already checked the the place is claimed and the bike exists
            Vector2 pos = BeamPlace.PlacePos(msg.xIdx, msg.zIdx);
            BeamPlace p = GameData.GetPlace(pos);
            BaseBike hittingBike = GameData.GetBaseBike(msg.bikeId);
            if (p != null && hittingBike != null)
            {
                hittingBike.UpdatePosFromCommand(msg.TimeStamp, p.GetPos());
                logger.Verbose($"OnPlaceHitCmd(p?.GetPos().ToString() Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike: {hittingBike?.bikeId} Pos: {p?.GetPos().ToString()}");
                PlaceHitEvt?.Invoke(this, new PlaceHitArgs(p, hittingBike));
                OnScoreEvent(hittingBike, p.bike.team == hittingBike.team ? ScoreEvent.kHitFriendPlace : ScoreEvent.kHitEnemyPlace, p);
            }
        }

        public void OnPlaceRemovedCmd(PlaceRemovedMsg msg)
        {
            BeamPlace p = GameData.GetPlace(msg.xIdx, msg.zIdx);
            logger.Verbose($"OnPlaceRemovedCmd({msg.xIdx},{msg.zIdx}) {(p==null?"MISSING":"")} Now: {FrameApianTime} Ts: {msg.TimeStamp}");
            GameData.PostPlaceRemoval(p);
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
            apian.SendBikeCreateReq(FrameApianTime, ib, destId);
        }

        public void PostBikeCommand(IBike bike, BikeCommand cmd)
        {
            apian.SendBikeCommandReq(FrameApianTime, bike, cmd, (bike as BaseBike).UpcomingGridPoint());
        }

       public void PostBikeTurn(IBike bike, TurnDir dir)
        {
            Vector2 nextPt = (bike as BaseBike).UpcomingGridPoint();

            float dx = Vector2.Distance(bike.position, nextPt);
            if (dx < BaseBike.length * .5f)
                logger.Debug($"PostBikeTurn(): Bike too close to turn: {dx} < {BaseBike.length * .5f}");
            else
                apian.SendBikeTurnReq(FrameApianTime, bike, dir, nextPt);
        }

        protected void OnScoreEvent(BaseBike bike, ScoreEvent evt, BeamPlace place)
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
            logger.Verbose($"_AddBike(): {ib.bikeId} at ({ib.position.x}, {ib.position.y})");

            if (GameData.GetBaseBike(ib.bikeId) != null)
                return false;

            GameData.Bikes[ib.bikeId] = ib;

            NewBikeEvt?.Invoke(this, ib);

            return true;
        }

        protected void _RemoveBike(IBike ib, bool shouldBlowUp=true)
        {
            logger.Info($"_RemoveBike(): {ib.bikeId}");
            GameData.RemovePlacesForBike(ib);
            BikeRemovedEvt?.Invoke(this, new BikeRemovedData(ib.bikeId,  shouldBlowUp));
            GameData.PostBikeRemoval(ib.bikeId); // we're almost certainly iterating over the list of bikes so don;t remove it yet.
        }
        public void ClearBikes()
        {
            BikesClearedEvt?.Invoke(this, EventArgs.Empty);
            GameData.Bikes.Clear();
        }

       // Ground-related
        public void OnPlaceTimeoutEvt(object sender, BeamPlace p)
        {
            apian.SendPlaceRemovedObs(FrameApianTime, p.xIdx, p.zIdx);
        }

        public void ClearPlaces()
        {
            GameData.ClearPlaces(); // notifies FE.
        }

    }

}