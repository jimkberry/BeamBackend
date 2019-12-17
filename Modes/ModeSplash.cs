using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;

namespace BeamBackend
{
    // Remember, BaseGameMode is Setup() with:
    //  manager == ModeManager
	//	gameInst == GameInstance    
    public class ModeSplash : BeamGameMode
    {      
        static public readonly int kCmdTargetCamera = 1;

	    static public readonly int kSplashBikeCount = 12;
        protected const float kRespawnCheckInterval = .33f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;         
        public BeamGameInstance game = null;      

		public override void Start(object param = null)	
        {
            logger.Info("Starting Splash");
            base.Start();
            game = (BeamGameInstance)gameInst;
            game.ClearPlayers();
            game.ClearBikes();    
            game.ClearPlaces();     

            string cameraTargetBikeId = CreateADemoBike();
            for( int i=1;i<kSplashBikeCount; i++) 
                CreateADemoBike(); 

            game.frontend.ModeHelper()
                .OnStartMode(BeamModeFactory.kSplash, new TargetIdParams{targetId = cameraTargetBikeId} );             
        }

		public override void Loop(float frameSecs) 
        {
            _secsToNextRespawnCheck -= frameSecs;
            if (_secsToNextRespawnCheck <= 0)
            {
                // TODO: respawn with prev names/teams?
                if (game.gameData.Bikes.Count() < kSplashBikeCount)
                    CreateADemoBike();
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

        protected string CreateADemoBike()
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( game.gameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            IBike ib = BikeFactory.CreateBike(game, bikeId, game.LocalPeerId,  DemoPlayerData.RandomName(),
                DemoPlayerData.RandomTeam(), BikeFactory.AiCtrl, pos, heading);
            game.NewBike(ib); 
            logger.Info($"{this.ModeName()}: CreateADemoBike({bikeId})");
            return ib.bikeId;          
        }

    }
}