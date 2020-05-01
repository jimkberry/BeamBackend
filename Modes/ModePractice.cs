using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;

namespace BeamBackend
{
    public class ModePractice : BeamGameMode
    {
        static public readonly string GameName = "LocalPracticeGame";
        static public readonly string ApianGroupName = "LocalPracticeGroup";
        static public readonly string ApianGroupId = "LocalPracticeId";
        public readonly int kMaxAiBikes = 11;
        public BeamGameInstance game = null;
        protected BaseBike playerBike = null;
        protected const float kRespawnCheckInterval = 1.3f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected bool gameJoined;
        protected bool bikesCreated;

		public override void Start(object param = null)
        {
            base.Start();

            core.PeerJoinedGameEvt += OnPeerJoinedGameEvt;
            core.AddGameInstance(null); // TODO: THis is beam only. Need better way. ClearGameInstances()? Init()?

            // Setup/connect fake network
            core.ConnectToNetwork("p2ploopback");
            core.JoinNetworkGame(GameName);
            // Now wait for OnPeerJoinedGame()
        }

		public override void Loop(float frameSecs)
        {
            if (gameJoined && !bikesCreated)
            {
                // Create player bike
                string playerBikeId = SpawnPlayerBike();
                for( int i=0;i<kMaxAiBikes; i++)
                {
                    // TODO: create a list of names/teams and respawn them when the blow up?
                    // ...or do it when respawn gets called
                    SpawnAIBike();
                }
                game.frontend?.OnStartMode(BeamModeFactory.kPractice, new TargetIdParams{targetId = playerBikeId} );
                bikesCreated = true;
            }

            if (bikesCreated)
            {
                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
                    // TODO: respawn with prev names/teams?
                    if (game.GameData.Bikes.Count < kMaxAiBikes)
                        SpawnAIBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

		public override object End() {
            core.PeerJoinedGameEvt -= OnPeerJoinedGameEvt;
            game.RespawnPlayerEvt -= OnRespawnPlayerEvt;
            game.frontend?.OnEndMode(core.modeMgr.CurrentModeId(), null);
            game.ClearPeers();
            game.ClearBikes();
            game.ClearPlaces();
            return null;
        }

        protected string CreateBaseBike(string ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( game.GameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            BaseBike bb = new BaseBike(game, bikeId, peerId, name, t, ctrlType, pos, heading, BaseBike.defaultSpeed);
            game.PostBikeCreateData(bb);
            return bb.bikeId;
        }

        protected string SpawnPlayerBike()
        {
            // Create one the first time
            string scrName = game.frontend.GetUserSettings().screenName;
            return CreateBaseBike(BikeFactory.LocalPlayerCtrl, game.LocalPeerId, game.LocalMember.Name, BikeDemoData.RandomTeam());
        }

        protected string SpawnAIBike(string name = null, Team team = null)
        {
            if (name == null)
                name = BikeDemoData.RandomName();

            if (team == null)
                team = BikeDemoData.RandomTeam();

            return CreateBaseBike(BikeFactory.AiCtrl, game.LocalPeerId, name, team);
        }

        public void OnRespawnPlayerEvt(object sender, EventArgs args)
        {
            logger.Info("Respawning Player");
            SpawnPlayerBike();
            // Note that this will eventually result in a NewBikeEvt which the frontend
            // will catch and deal with. Maybe it'll point a camera at the new bike or whatever.
        }

        public void OnPeerJoinedGameEvt(object sender, PeerJoinedGameArgs ga)
        {
            bool isLocal = ga.peer.PeerId == core.LocalPeer.PeerId;
            if (isLocal && game == null)
            {
                logger.Info("practice game joined");
                // Create gameInstance and associated Apian
                game = new BeamGameInstance(core.frontend);
                game.MemberJoinedGroupEvt += OnMemberJoinedGroupEvt;
                BeamApian apian = new BeamApianSinglePeer(core.gameNet, game);
                core.AddGameInstance(game);
                // Dont need to check for groups in splash
                apian.CreateGroup(ApianGroupId, ApianGroupName);
                BeamGroupMember mb = new BeamGroupMember(core.LocalPeer.PeerId, core.LocalPeer.Name);
                apian.JoinGroup(ApianGroupId, mb.ApianSerialized());
                // waiting for OnGroupJoined()
            }
        }

        public void OnMemberJoinedGroupEvt(object sender, MemberJoinedGroupArgs ga)
        {
            game.RespawnPlayerEvt += OnRespawnPlayerEvt;
            gameJoined = true;
        }
    }
}


