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

            _cmdDispatch[BeamMessage.kGameCreated ] = new Func<object, bool>(o => OnGameCreated(o)); 
            _cmdDispatch[BeamMessage.kGameJoined] = new Func<object, bool>(o => OnGameJoined(o));              
            _cmdDispatch[BeamMessage.kPeerLeft] = new Func<object, bool>(o => OnPeerLeft(o));
            _cmdDispatch[BeamMessage.kNewBike] = new Func<object, bool>(o => OnNewBike(o));                      

            game = (BeamGameInstance)gameInst;
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

            string cameraTargetBikeId = CreateADemoBike();
            for( int i=1;i<kSplashBikeCount; i++) 
                CreateADemoBike(); 

            // Note that the target bike is probably NOT created yet at this point.
            // This robably needs to happen differently
            game.frontend?.OnStartMode(BeamModeFactory.kSplash, new TargetIdParams{targetId = cameraTargetBikeId} );             
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
            game.frontend?.OnEndMode(game.modeMgr.CurrentModeId(), null);
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
            game.gameNet.SendBikeCreateData(ib); 
            logger.Debug($"{this.ModeName()}: CreateADemoBike({bikeId})");
            return ib.bikeId;  // the bike hasn't been added yet, so this id is not valid yet. 
        }

        public bool OnGameCreated(object o)
        {
            string newGameId = ((GameCreatedMsg)o).gameId;
            Console.WriteLine($"Created game: {newGameId}");
            // Tell frontend, if needed                 
            return true;
        }

        public bool OnGameJoined(object o)
        {
            string gameId = ((GameJoinedMsg)o).gameId;
            string localId = ((GameJoinedMsg)o).localId; 
            game.SetGameId(gameId);           
            logger.Info($"Joined game: {gameId} as ID: {localId}");             
            return true;
        }

        public bool OnPeerJoined(object o)
        {
            BeamPeer p = ((PeerJoinedMsg)o).peer;
            string lr = p.IsLocal ? "Local" : "Remote";
            logger.Info($"{lr} Peer Joined: {p.Name}, ID: {p.PeerId}");  
            game.frontend?.OnNewPeer(p, ModeId());                           
            return true;
        }

        public bool OnPeerLeft(object o)
        {
            string p2pId =  ((PeerLeftMsg)o).p2pId;
            logger.Info($"Remote Peer Left: {p2pId}");  
            game.frontend?.OnPeerLeft(p2pId);                     
            return true;
        }      

        public bool OnNewBike(object o)
        {
            IBike ib =  ((NewBikeMsg)o).ib;
            logger.Info($"OnNewBike: {ib.bikeId}");   
            game.frontend?.OnNewBike(ib);                                  
            return true;
        }  

    }
}