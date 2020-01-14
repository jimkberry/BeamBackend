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

		public override void Start(object param = null)	
        {
            base.Start();

            game = (BeamGameInstance)gameInst; // Todo - this oughta be in a higher-level BeamGameMode
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

            // Create player bike
            string playerBikeId = SpawnPlayerBike();

            for( int i=0;i<kMaxAiBikes; i++) 
            {
                // TODO: create a list of names/teams and respawn them when the blow up?
                // ...or do it when respawn gets called
                SpawnAIBike(); 
            }

            game.frontend?.OnStartMode(BeamModeFactory.kPractice, new TargetIdParams{targetId = playerBikeId} );             
        }

		public override void Loop(float frameSecs) 
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
            game.gameNet.SendBikeCreateData(bb); 
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

        protected void RespawnPlayerBike()
        {       
            logger.Error("RespawnPlayerBike() not implmented");
            // Player localPlayer = _mainObj.backend.Players.Values.Where( p => p.IsLocal).First();
            // GameObject playerBike = SpawnPlayerBike(localPlayer);
            // _mainObj.uiCamera.CurrentStage().transform.Find("RestartCtrl")?.SendMessage("moveOffScreen", null);         
            // _mainObj.uiCamera.CurrentStage().transform.Find("Scoreboard").SendMessage("SetLocalPlayerBike", playerBike); 
            // _mainObj.gameCamera.StartBikeMode( playerBike);                
        }     
    }
}