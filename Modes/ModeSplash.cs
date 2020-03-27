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
        protected bool gameJoined;
        protected bool bikesCreated;        


		public override void Start(object param = null)	
        {
            logger.Info("Starting Splash");
            base.Start();
                
            game = (BeamGameInstance)gameInst; // Todo - this oughta be in a higher-level BeamGameMode
            game.PeerJoinedGameEvt += OnPeerJoinedGameEvt;    

            gameJoined = false;
            bikesCreated = false;

            game = (BeamGameInstance)gameInst;
            game.ClearPeers();
            game.ClearBikes();    
            game.ClearPlaces();     

            // Setup/connect fake network
            BeamUserSettings settings = game.frontend.GetUserSettings();
            game.gameNet.Connect("p2ploopback");
            string p2pId = game.gameNet.LocalP2pId();
            BeamPeer localPeer = new BeamPeer(p2pId, settings.screenName, null);
            game.AddLocalPeer(localPeer);
            game.gameNet.JoinGame("localgame");                     
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
                    if (game.gameData.Bikes.Count() < kSplashBikeCount)
                        CreateADemoBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

		public override object End() {   
            game.PeerJoinedGameEvt -= OnPeerJoinedGameEvt;                      
            game.frontend?.OnEndMode(game.modeMgr.CurrentModeId(), null);
            game.gameNet.LeaveGame();           
            game.ClearPeers();
            game.ClearBikes();    
            game.ClearPlaces();              
            return null;
        } 

        protected string CreateADemoBike()
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( game.gameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            IBike ib =  new BaseBike(game, bikeId, game.LocalPeerId, BikeDemoData.RandomName(), BikeDemoData.RandomTeam(), 
                BikeFactory.AiCtrl, pos, heading, BaseBike.defaultSpeed);
            game.PostBikeCreateData(ib); 
            logger.Debug($"{this.ModeName()}: CreateADemoBike({bikeId})");
            return ib.bikeId;  // the bike hasn't been added yet, so this id is not valid yet. 
        }       
        
        public void OnPeerJoinedGameEvt(object sender, PeerJoinedGameArgs ga)
        {     
            bool isLocal = ga.peer.PeerId == game.LocalPeerId;              
            if (isLocal)
            {
                logger.Info("Splash game joined");
                gameJoined = true;            
            }
        } 

    }
}