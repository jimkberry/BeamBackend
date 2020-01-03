using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;

namespace BeamBackend
{
    public class ModePlay : BeamGameMode
    {
        public readonly int kMaxPlayers = 12;        

        public BeamGameInstance game = null;

        protected BaseBike playerBike = null;

        protected const float kRespawnCheckInterval = .33f;

        protected float _secsToNextRespawnCheck = kRespawnCheckInterval; 

		public override void Start(object param = null)	
        {
            base.Start();
           // TODO: respawning  _cmdDispatch["Respawn"] = new Action<object>(o => RespawnPlayerBike());  

            game = (BeamGameInstance)gameInst; // Todo - this oughta be in a higher-level BeamGameMode
            game.ClearPeers();
            game.ClearBikes();    
            game.ClearPlaces();     

            // Create player bike
            SpawnPlayerBike();

            for( int i=1;i<kMaxPlayers; i++) 
            {
                // TODO: create a list of names/teams and respawn them when the blow up?
                // ...or do it when respawn gets called
                SpawnAIBike(); 
            }

            game.frontend?.ModeHelper()
                .OnStartMode(BeamModeFactory.kPlay, new TargetIdParams{targetId = playerBike.bikeId} );             
        }

		public override void Loop(float frameSecs) 
        {
            _secsToNextRespawnCheck -= frameSecs;
            if (_secsToNextRespawnCheck <= 0)
            {
                // TODO: respawn with prev names/teams?
                SpawnAIBike();
                _secsToNextRespawnCheck = kRespawnCheckInterval;
            }
        }

		public override object End() {            
            game.frontend?.ModeHelper().OnEndMode(game.modeMgr.CurrentModeId(), null);
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
            string bikeId = string.Format("{0:X8}", (scrName + game.LocalPeerId).GetHashCode());
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