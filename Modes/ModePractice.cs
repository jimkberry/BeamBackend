using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;

namespace BeamBackend
{
    public class ModePractice : BeamGameMode
    {
        public readonly int kMaxAiBikes = 11;        

        public BeamGameInstance game = null;

        protected BaseBike playerBike = null;

        protected const float kRespawnCheckInterval = 1.3f;

        protected float _secsToNextRespawnCheck = kRespawnCheckInterval; 
        protected bool gameJoined = false;
        protected bool bikesCreated = false;        

		public override void Start(object param = null)	
        {
            base.Start();

            game = (BeamGameInstance)gameInst; // Todo - this oughta be in a higher-level BeamGameMode
            game.GameJoinedEvt += OnGameJoinedEvt;            
            game.RespawnPlayerEvt += OnRespawnPlayerEvt; 

            gameJoined = false;
            bikesCreated = false;

            game.ClearPeers();
            game.ClearBikes();    
            game.ClearPlaces();     

            // Setup/connect fake network
            BeamUserSettings settings = game.frontend.GetUserSettings();
            game.gameNet.Connect("p2ploopback");
            string p2pId = game.gameNet.LocalP2pId();
            BeamPeer localPeer = new BeamPeer(p2pId, settings.screenName, null, true);
            game.AddLocalPeer(localPeer);
            game.gameNet.JoinGame("localgame");                 
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
                    if (game.gameData.Bikes.Count < kMaxAiBikes)
                        SpawnAIBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

		public override object End() {            
            game.frontend?.OnEndMode(game.modeMgr.CurrentModeId(), null);
            game.ClearPeers();
            game.ClearBikes();    
            game.ClearPlaces();              
            return null;
        } 
  
        protected string CreateBaseBike(int ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( game.gameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );  
            string bikeId = Guid.NewGuid().ToString();
            BaseBike bb = new BaseBike(game, bikeId, peerId, name, t, ctrlType, pos, heading, BaseBike.defaultSpeed);
            game.PostBikeCreateData(bb); 
            return bb.bikeId;
        }

        protected string SpawnPlayerBike()
        {
            // Create one the first time
            string scrName = game.frontend.GetUserSettings().screenName;
            return CreateBaseBike(BikeFactory.LocalPlayerCtrl, game.LocalPeerId, game.LocalPeer.Name, game.LocalPeer.Team);                 
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

        public void OnGameJoinedEvt(object sender, GameJoinedArgs ga)
        {     
            logger.Info("Practice game joined");
            gameJoined = true;            
        }        
    }
}