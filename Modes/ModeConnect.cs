using System.Linq;
using System;
using UnityEngine;

namespace BeamBackend
{
    public class ModeConnect : BeamGameMode
    {
        protected const float kWaitForPlayersSecs = 3.0f;
        protected const int kCreatingGame = 0;        
        protected const int kJoiningGame = 1;
        protected const int kWaitingForPlayers = 2; // wait a couple seconds before creating bike
        protected const int kCreatingBike = 3;     
        protected const int kWaitingForRemoteBike = 4; // Wait until there is at least 1 remote bike           
        protected const int kReadyToPlay = 5;   

        public BeamGameInstance game = null; 
        public BeamUserSettings settings = null;     
        protected int _curState = kCreatingGame;

        protected float _curStateSecs = 0;  
        protected delegate void LoopFunc(float f);
        protected LoopFunc _loopFunc; 

		public override void Start(object param = null)	
        {
            logger.Info("Starting Connect");
            base.Start();

            game = (BeamGameInstance)gameInst;

            game.GameCreatedEvt += OnGameCreatedEvt;
            game.GameJoinedEvt += OnGameJoinedEvt;
            game.PeerJoinedEvt += OnPeerJoinedEvt;
            game.PeerLeftEvt += OnPeerLeftEvt;
            game.NewBikeEvt += OnNewBikeEvt;

            settings = game.frontend.GetUserSettings();

            game.ClearPeers();
            game.ClearBikes();    
            game.ClearPlaces();              

            // need to "connect"first in order to have a p2pId
            game.gameNet.Connect(settings.p2pConnectionString);
            string p2pId = game.gameNet.LocalP2pId();
            BeamPeer localPeer = _CreateLocalPeer(p2pId, settings);
            game.AddLocalPeer(localPeer);

            if (!settings.tempSettings.ContainsKey("gameId"))             
                _SetState(kCreatingGame, new BeamGameNet.GameCreationData());      
            else
                _SetState(kJoiningGame, settings.tempSettings["gameId"]);     

            game.frontend?.OnStartMode(ModeId(), null );                                                               
        }

        public override void Loop(float frameSecs)
        {
            _loopFunc(frameSecs);
            _curStateSecs += frameSecs;
        }

		public override object End() {            
            game.frontend?.OnEndMode(ModeId());            
            return null;
        }         

        protected void _SetState(int newState, object startParam = null)
        {
            _curStateSecs = 0;
            _loopFunc = _DoNothingLoop; // default
            switch (newState)
            {
            case kCreatingGame:
                logger.Info("Creating game");                     
                game.gameNet.CreateGame(startParam);  
                break;            
            case kJoiningGame:      
                logger.Info($"Joining Game {(string)startParam}");            
                game.gameNet.JoinGame((string)startParam);
                break;                      
            case kWaitingForPlayers:
                logger.Info($"Waiting for players");
                _loopFunc = _WaitForPlayersLoop;
                break;
            case kCreatingBike:
                logger.Info($"Creating local bike");  
                _CreateLocalBike(settings.localPlayerCtrlType);  
                _CreateADemoBike();  
                _CreateADemoBike();               
                break;
            case kWaitingForRemoteBike:
                logger.Info($"Waiting for a remote bike.");
                _loopFunc = _WaitForRemoteBikeLoop;
                break;                
            case kReadyToPlay:
                logger.Info($"Ready to play."); 
                game.RaiseReadyToPlay();
                break;
            default:
                logger.Error($"ModeConnect._SetState() - Unknown state: {newState}");
                break;
            }
        }

        protected void _DoNothingLoop(float frameSecs) {}
        protected void _WaitForPlayersLoop(float frameSecs) 
        {
            if (_curStateSecs > kWaitForPlayersSecs)
                _SetState(kCreatingBike);
        }

        protected void _WaitForRemoteBikeLoop(float frameSecs) 
        {
            if ( _RemoteBikeExists() )
                _SetState(kReadyToPlay);
        }


        // Event handlers
		// Event handlers
        public void OnGameCreatedEvt(object sender, string newGameId)
        {
            Console.WriteLine($"Created game: {newGameId}");
            _SetState(kJoiningGame, newGameId);                   
        }

        public void OnGameJoinedEvt(object sender, GameJoinedArgs ga)
        {     
            logger.Info($"Joined game: {ga.gameChannel} as ID: {ga.localP2pId}");
            _SetState(kWaitingForPlayers, null);             
        }

        public void OnPeerJoinedEvt(object sender, BeamPeer p)
        {
            string lr = p.IsLocal ? "Local" : "Remote";
            logger.Info($"{lr} Peer Joined: {p.Name}, ID: {p.PeerId}");                           
        }

        public void OnPeerLeftEvt(object sender, string p2pId)
        {
            logger.Info($"Remote Peer Left: {p2pId}");  
        }      		

        public void OnNewBikeEvt(object sender, IBike ib)
        {
            string lr = ib.peerId == game.LocalPeerId ? "local" : "remote";
            logger.Info($"New {lr} bike: {ib.bikeId}");             
            if (ib.peerId == game.LocalPeerId)
                _SetState(kWaitingForRemoteBike, null);                         
        }


        //
        // utils
        //

        protected bool _RemoteBikeExists()
        {
            return game.gameData.Bikes.Values.Where( ib => ib.peerId != game.LocalPeerId).Count() > 0;
        }

        protected BeamPeer _CreateLocalPeer(string p2pId, BeamUserSettings settings)
        {               
            // Game.LocalP2pId is not set yet
            return new BeamPeer(p2pId, settings.screenName, null, true);
        }

        protected void _CreateLocalBike(int bikeCtrlType)
        {
            string scrName = game.frontend.GetUserSettings().screenName;
            string bikeId = string.Format("{0:X8}", (scrName + game.LocalPeerId).GetHashCode());
            BaseBike bb =  game.CreateBaseBike(bikeCtrlType, game.LocalPeerId, game.LocalPeer.Name, game.LocalPeer.Team);     
            game.gameNet.SendBikeCreateData(bb); // will result in OnBikeInfo()            
        }          

        protected string _CreateADemoBike()
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

    }
}