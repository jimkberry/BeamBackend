using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;

namespace BeamBackend
{
    public class ModePlay : BaseGameMode
    {

        public enum Commands
        {
            kRespawn = 0,
            kCount = 1
        }    
        public readonly int kMaxPlayers = 12;        

        public BeamGameInstance game = null;

        protected BaseBike playerBike = null;

        protected const float kRespawnCheckInterval = .33f;

        protected float _secsToNextRespawnCheck = kRespawnCheckInterval; 

		public override void Start(object param = null)	
        {
            base.Start();
            _cmdDispatch[(int)Commands.kRespawn] = new Action<object>(o => RespawnPlayerBike());  

            game = (BeamGameInstance)gameInst; // Todo - this oughta be in a higher-level BeamGameMode
            game.ClearPlayers();
            game.ClearBikes();    
            game.ClearPlaces();     

            // Create player bike
            playerBike = SpawnPlayerBike();

            for( int i=1;i<kMaxPlayers; i++) 
            {
                Player p = null;
                while (p == null) {
                    p = DemoPlayerData.CreatePlayer();
                    if (!game.AddNewPlayer(p))
                        p = null;
                }
                SpawnAIBike(p); 
            }

            game.frontend?.ModeHelper()
                .OnStartMode(BeamModeFactory.kPlay, new TargetIdParams{targetId = playerBike.bikeId} );             
        }

		public override void Loop(float frameSecs) 
        {
            _secsToNextRespawnCheck -= frameSecs;
            if (_secsToNextRespawnCheck <= 0)
            {
			    //Debug.Log(string.Format("Checking for idle player"));                
                Player p = PlayerWithoutBike();
                if (p != null)
                {
			        Debug.Log(string.Format("Respawning AI: {0}", p.ScreenName));
                    SpawnAIBike(p);
                }

                _secsToNextRespawnCheck = kRespawnCheckInterval;
            }
        }

		public override object End() {            
            game.frontend?.ModeHelper().OnEndMode(game.modeMgr.CurrentModeId(), null);
            game.ClearPlayers();
            game.ClearBikes();    
            game.ClearPlaces();              
            return null;
        } 

        public override void HandleCmd(int cmd, object param)
        {

        }                

        protected Player PlayerWithoutBike()
        {
            // Maybe ought to put a weak ref to a bike in the player class
            return game.gameData.Players.Values.Where( (p) => !p.IsLocal && game.gameData.GetBaseBike(p.bikeId) == null).FirstOrDefault();
        }

        protected BaseBike CreateBaseBike(Player p)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( game.gameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );  
            string bikeId = Guid.NewGuid().ToString();
            int ctrlType = p.IsLocal ? BikeFactory.LocalPlayerCtrl : BikeFactory.AiCtrl;
            BaseBike bb = new BaseBike(game, bikeId, p, ctrlType, pos, heading);
            game.NewBike(bb); 
            return bb;
        }

        protected BaseBike SpawnPlayerBike(Player p = null)
        {
        // Create one the first time
            while (p == null) {
                p = DemoPlayerData.CreatePlayer(true); 
                if (game.AddNewPlayer(p) == false)
                    p = null;
            }
            return CreateBaseBike(p);                   
        }        

        protected BaseBike SpawnAIBike(Player p)
        {
            return CreateBaseBike(p);  
        }

        protected void RespawnPlayerBike()
        {       
            // Player localPlayer = _mainObj.backend.Players.Values.Where( p => p.IsLocal).First();
            // GameObject playerBike = SpawnPlayerBike(localPlayer);
            // _mainObj.uiCamera.CurrentStage().transform.Find("RestartCtrl")?.SendMessage("moveOffScreen", null);         
            // _mainObj.uiCamera.CurrentStage().transform.Find("Scoreboard").SendMessage("SetLocalPlayerBike", playerBike); 
            // _mainObj.gameCamera.StartBikeMode( playerBike);                
        }     
    }
}