using System.Security.Permissions;
using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;

namespace BeamBackend
{
    public class ModeSplash : BeamGameMode
    {
        static public readonly string GameName = "LocalSplashGame";
        static public readonly string ApianGroupName = "LocalSplashGroup";
        static public readonly string ApianGroupId = "LocalSplashId";
        static public readonly int kCmdTargetCamera = 1;
	    static public readonly int kSplashBikeCount = 12;
        protected const float kRespawnCheckInterval = 1.3f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        public BeamGameInstance game = null;
        protected bool gameJoined;
        protected bool bikesCreated;

        private enum ModeState {
            JoiningGame = 1,
            JoiningGroup,
            CreatingBikes,
            Playing
        }

        private ModeState _CurrentState;

		public override void Start(object param = null)
        {
            logger.Info("Starting Splash");
            base.Start();

            core.PeerJoinedGameEvt += OnPeerJoinedGameEvt;
            core.AddGameInstance(null); // TODO: THis is beam only. Need better way. ClearGameInstances()? Init()?

            // Setup/connect fake network
            core.ConnectToNetwork("p2ploopback");
            core.JoinNetworkGame(GameName);
            _CurrentState = ModeState.JoiningGame;
            // Now wait for OnPeerJoinedGame()

        }


		public override void Loop(float frameSecs)
        {
            if (gameJoined && !bikesCreated)
            {
                string cameraTargetBikeId = CreateADemoBike();
                for( int i=1;i<kSplashBikeCount; i++)
                    CreateADemoBike();

                // Note that the target bike is probably NOT created yet at this point.
                // This robably needs to happen differently
                game.frontend?.OnStartMode(BeamModeFactory.kSplash, new TargetIdParams{targetId = cameraTargetBikeId} );
                bikesCreated = true;
            }

            if (bikesCreated)
            {
                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
                    // TODO: respawn with prev names/teams?
                    if (game.GameData.Bikes.Count() < kSplashBikeCount)
                        CreateADemoBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

		public override object End() {
            core.PeerJoinedGameEvt -= OnPeerJoinedGameEvt;
            game.MemberJoinedGroupEvt -= OnMemberJoinedGroupEvt;
            game.frontend?.OnEndMode(core.modeMgr.CurrentModeId(), null);
            game.End();
            core.gameNet.LeaveGame();
            core.AddGameInstance(null);
            return null;
        }

        protected string CreateADemoBike()
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( game.GameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            IBike ib =  new BaseBike(game, bikeId, game.LocalPeerId, BikeDemoData.RandomName(), BikeDemoData.RandomTeam(),
                BikeFactory.AiCtrl, pos, heading, BaseBike.defaultSpeed);
            game.PostBikeCreateData(ib);
            logger.Debug($"{this.ModeName()}: CreateADemoBike({bikeId})");
            return ib.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        public void OnPeerJoinedGameEvt(object sender, PeerJoinedGameArgs ga)
        {
            bool isLocal = ga.peer.PeerId == core.LocalPeer.PeerId;
            if (isLocal && _CurrentState == ModeState.JoiningGame)
            {
                logger.Info("Splash game joined");
                // Create gameInstance and associated Apian
                game = new BeamGameInstance(core.frontend);
                game.MemberJoinedGroupEvt += OnMemberJoinedGroupEvt;
                BeamApian apian = new BeamApianSinglePeer(core.gameNet, game);
                core.AddGameInstance(game);
                // Dont need to check for groups in splash
                apian.CreateGroup(ApianGroupId, ApianGroupName);
                BeamGroupMember mb = new BeamGroupMember(core.LocalPeer.PeerId, core.LocalPeer.Name);
                apian.JoinGroup(ApianGroupId, mb.ApianSerialized());
                _CurrentState = ModeState.JoiningGroup;
                // waiting for OnGroupJoined()
            }
        }

        public void OnMemberJoinedGroupEvt(object sender, MemberJoinedGroupArgs ga)
        {
            _CurrentState = ModeState.Playing;
            gameJoined = true;
        }

    }
}