using System.Collections.Generic;
using System;
using System.Linq;
using GameModeMgr;
using Apian;
using UnityEngine;

namespace BeamBackend
{
    public enum GroupCreateMode
    {
        JoinOnly = 0,
        CreateIfNeeded = 1,
        MustCreate = 2
    }

    public class ModePlay : BeamGameMode
    {
        protected string gameId;
        protected string apianGroupId;
        protected GroupCreateMode groupCreateMode;
        public BeamUserSettings settings;

        protected Dictionary<string, ApianGroupInfo> announcedGroups;

        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected int _curState;
        protected float _curStateSecs;
        protected delegate void LoopFunc(float f);
        protected LoopFunc _loopFunc;
        public BeamGameInstance game = null;
        protected BaseBike playerBike = null;

        // mode substates
        protected const int kCreatingGame = 0;
        protected const int kJoiningGame = 1;
        protected const int kCheckingForGroups = 2;
        protected const int kJoiningGroup = 4;
        protected const int kWaitingForMembers = 5;
        protected const int kPlaying = 6;
        protected const int kFailed = 7;

        protected const float kRespawnCheckInterval = 1.3f;
        protected const float kListenForGroupsSecs = 2.0f; // TODO: belongs here?

		public override void Start(object param = null)
        {
            base.Start();
            announcedGroups = new Dictionary<string, ApianGroupInfo>();

            settings = core.frontend.GetUserSettings();

            _ParseGameAndGroup();

            core.GameCreatedEvt += OnGameCreatedEvt;
            core.PeerJoinedGameEvt += OnPeerJoinedGameEvt;
            core.AddGameInstance(null);

            // Setup/connect fake network
            core.ConnectToNetwork(settings.p2pConnectionString);

            if (gameId == null)
                _SetState(kCreatingGame, new BeamGameNet.GameCreationData());
            else
                _SetState(kJoiningGame, gameId);

            core.frontend?.OnStartMode(ModeId(), null );
        }

		public override void Loop(float frameSecs)
        {
            _loopFunc(frameSecs);
            _curStateSecs += frameSecs;
        }

		public override object End() {
            core.GameCreatedEvt -= OnGameCreatedEvt;
            core.PeerJoinedGameEvt -= OnPeerJoinedGameEvt;
            game.PlayerJoinedEvt -= OnPlayerJoinedEvt;
            game.NewBikeEvt -= OnNewBikeEvt;
            game.frontend?.OnEndMode(core.modeMgr.CurrentModeId(), null);
            game.End();
            core.gameNet.LeaveGame();
            core.AddGameInstance(null);
            return null;
        }

        // Loopfuncs

        protected void _SetState(int newState, object startParam = null)
        {
            _curStateSecs = 0;
            _curState = newState;
            _loopFunc = _DoNothingLoop; // default
            switch (newState)
            {
            case kCreatingGame:
                logger.Verbose($"{(ModeName())}: SetState: kCreatingGame");
                core.CreateNetworkGame((BeamGameNet.GameCreationData)startParam);
                // Wait for OnGameCreatedEvt()
                break;
            case kJoiningGame:
                logger.Verbose($"{(ModeName())}: SetState: kJoiningGame");
                core.JoinNetworkGame((string)startParam);
                // Wait for OnGameJoinedEvt()
                break;
            case kCheckingForGroups:
                logger.Verbose($"{(ModeName())}: SetState: kCheckingForGroups");
                announcedGroups.Clear();
                core.GroupAnnounceEvt += OnGroupAnnounceEvt;
                core.ListenForGroups();
                _loopFunc = _GroupListenLoop;
                break;
            case kJoiningGroup:
                logger.Verbose($"{(ModeName())}: SetState: kJoiningGroup");
                _JoinGroup();
                break;
            case kWaitingForMembers:
                logger.Verbose($"{(ModeName())}: SetState: kWaitingForMembers");
                break;
            case kPlaying:
                logger.Verbose($"{(ModeName())}: SetState: kPlaying");
                SpawnPlayerBike();
                for (int i=0; i<settings.aiBikeCount; i++)
                    SpawnAiBike();
                _loopFunc = _PlayLoop;
                break;
            case kFailed:
                logger.Error($"{(ModeName())}: SetState: kFailed  Reason: {(string)startParam}");
                break;
            default:
                logger.Error($"ModeConnect._SetState() - Unknown state: {newState}");
                break;
            }
        }

        protected void _DoNothingLoop(float frameSecs) {}

        protected void _GroupListenLoop(float frameSecs)
        {
            if (_curStateSecs > kListenForGroupsSecs)
            {
                // TODO: Hoist all this!!!
                // Stop listening for groups and either create or join (or fail)
                core.GroupAnnounceEvt -= OnGroupAnnounceEvt; // stop listening
                bool targetGroupExisted = (apianGroupId != null) && announcedGroups.ContainsKey(apianGroupId);

                switch (groupCreateMode)
                {
                case GroupCreateMode.JoinOnly:
                    if (targetGroupExisted)
                    {
                        game.apian.InitExistingGroup(announcedGroups[apianGroupId]); // Like create, but for a remotely-created group
                        _SetState(kJoiningGroup);
                    }
                    else
                        _SetState(kFailed, $"Apian Group \"{apianGroupId}\" Not Found");
                    break;
                case GroupCreateMode.CreateIfNeeded:
                    if (targetGroupExisted)
                        game.apian.InitExistingGroup(announcedGroups[apianGroupId]);
                    else
                        _CreateGroup();
                    _SetState(kJoiningGroup);
                    break;
                case GroupCreateMode.MustCreate:
                    if (targetGroupExisted)
                        _SetState(kFailed, "Apian Group Already Exists");
                    else
                        _CreateGroup();
                        _SetState(kJoiningGroup);
                    break;
                }
            }
        }

        protected void _PlayLoop(float frameSecs)
        {
            if (settings.regenerateAiBikes)
            {
                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
                    if (game.GameData.LocalBikes(game.LocalPeerId).Where(ib => ib.ctrlType==BikeFactory.AiCtrl).Count() < settings.aiBikeCount)
                        SpawnAiBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

        // utils
        private void _ParseGameAndGroup()
        {
            string gameIdSetting;
            string[] parts = {};

            if (settings.tempSettings.TryGetValue("gameId", out gameIdSetting))
                parts = gameIdSetting.Split('/');

            // format is gameId/groupId/[j|c|m]   (joinOnly/createIfNeeded/mustCreate)
            groupCreateMode = parts.Count() < 3 ? GroupCreateMode.CreateIfNeeded :  (GroupCreateMode)"jcm".IndexOf(parts[2]);
            apianGroupId = parts.Count() < 2 ? null : parts[1]; // null means create a new group
            gameId = parts.Count() < 1 ? null : parts[0]; // null means create a new game

            logger.Verbose($"{(ModeName())}: _ParseGameAndGroup() game: {gameId}, group: {apianGroupId}, mode: {groupCreateMode}");
        }

        private void _CreateGroup()
        {
            logger.Verbose($"{(ModeName())}: _CreateGroup()");
            apianGroupId = apianGroupId ?? "BEAMGRP" + System.Guid.NewGuid().ToString();
            game.apian.CreateNewGroup(apianGroupId, "GRPNAME_" + apianGroupId);
        }

        private void _JoinGroup()
        {
            BeamPlayer mb = new BeamPlayer(core.LocalPeer.PeerId, core.LocalPeer.Name);
            game.apian.JoinGroup(apianGroupId, mb.ApianSerialized());
        }

        // Event handlers

        public void OnGameCreatedEvt(object sender, string newGameId)
        {
            logger.Info($"{(ModeName())} - OnGameCreatedEvt(): {newGameId}");
            if (_curState == kCreatingGame)
                _SetState(kJoiningGame, newGameId);
            else
                logger.Error($"{(ModeName())} - OnGameCreatedEvt() - Wrong state: {_curState}");
        }


        public void OnPeerJoinedGameEvt(object sender, PeerJoinedGameArgs ga)
        {
            BeamNetworkPeer p = ga.peer;
            bool isLocal = p.PeerId == core.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnPeerJoinedGameEvt() - {(isLocal?"Local":"Remote")} Peer Joined: {p.Name}, ID: {p.PeerId}");
            if (isLocal)
            {
                if (_curState == kJoiningGame)
                {
                    // Create gameinstance and ApianInstance
                    game = new BeamGameInstance(core.frontend);
                    game.PlayerJoinedEvt += OnPlayerJoinedEvt;
                    game.NewBikeEvt += OnNewBikeEvt;
                    BeamApian apian = new BeamApianCreatorServer(core.gameNet, game); // TODO: make the groupMgr type run-time spec'ed
                    //BeamApian apian = new BeamApianSinglePeer(core.gameNet, game); // *** This should be commented out (or gone)
                    core.AddGameInstance(game);
                    _SetState(kCheckingForGroups, null);
                }
                else
                    logger.Error($"{(ModeName())} - OnGameJoinedEvt() - Wrong state: {_curState}");
            }
        }

        public void OnGroupAnnounceEvt(object sender, ApianGroupInfo groupInfo)
        {
            logger.Verbose($"{(ModeName())} - OnGroupAnnounceEvt(): {groupInfo.GroupId}");
            announcedGroups[groupInfo.GroupId] = groupInfo;
        }


        public void OnPlayerJoinedEvt(object sender, PlayerJoinedArgs ga)
        {
            bool isLocal = ga.player.PeerId == core.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnPlayerJoinedEvt() - {(isLocal?"Local":"Remote")} Member Joined: {ga.player.Name}, ID: {ga.player.PeerId}");
            if (ga.player.PeerId == core.LocalPeer.PeerId)
            {
                game.RespawnPlayerEvt += OnRespawnPlayerEvt;
                //_SetState(kWaitingForMembers);
                _SetState(kPlaying);
            }
        }

        public void OnNewBikeEvt(object sender, IBike newBike)
        {
            // If it's local we need to tell it to Go!
            bool isLocal = newBike.peerId == core.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnNewBikeEvt() - {(isLocal?"Local":"Remote")} Bike created, ID: {newBike.bikeId}");
            if (isLocal)
            {
                game.PostBikeCommand(newBike, BikeCommand.kGo);
            }
        }

        public void OnRespawnPlayerEvt(object sender, EventArgs args)
        {
            logger.Info("Respawning Player");
            SpawnPlayerBike();
            // Note that this will eventually result in a NewBikeEvt
        }

        // Gameplay control
        protected string CreateBaseBike(string ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( game.GameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            BaseBike bb = new BaseBike(game.GameData, bikeId, peerId, name, t, ctrlType, pos, heading);
            game.PostBikeCreateData(bb);
            return bb.bikeId;
        }

        protected string SpawnAiBike()
        {
            // TODO: this (or something like it) appears several places in the codebase. Fix that.
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( game.GameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            IBike ib =  new BaseBike(game.GameData, bikeId, game.LocalPeerId, BikeDemoData.RandomName(), BikeDemoData.RandomTeam(),
                BikeFactory.AiCtrl, pos, heading);
            game.PostBikeCreateData(ib);
            logger.Info($"{this.ModeName()}: SpawnAiBike({bikeId})");
            return ib.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        protected string SpawnPlayerBike()
        {
            if (settings.localPlayerCtrlType != "none")
            {
                string scrName = game.frontend.GetUserSettings().screenName;
                string bikeId = string.Format("{0:X8}", (scrName + game.LocalPeerId).GetHashCode());
                return CreateBaseBike(settings.localPlayerCtrlType, game.LocalPeerId, game.LocalPlayer.Name, BikeDemoData.RandomTeam());
            }
            return null;
        }



    }
}


