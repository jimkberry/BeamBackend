using System.Reflection.Emit;
using System;
using GameModeMgr;
namespace BeamBackend
{
    public class ModeConnect : BeamGameMode
    {
        protected const float kWaitForPlayersSecs = 3.0f;
        protected const int kCreatingGame = 0;        
        protected const int kJoiningGame = 1;
        protected const int kWaitingForPlayers = 2; // wait a couple seconds
        protected const int kCreatingBike = 3;        
        protected const int kReadyToPlay = 4;   

        public BeamGameInstance game = null; 
        public BeamUserSettings settings = null;     
        protected int _curState = kCreatingGame;

        protected float _curStateSecs = 0;  
        protected delegate void LoopFunc(float f);
        protected LoopFunc _loopFunc; 

		public override void Start(object param = null)	
        {
            UnityEngine.Debug.Log("Starting Connect");
            base.Start();

            _cmdDispatch[BeamMessage.kGameCreated ] = new Func<object, bool>(o => OnGameCreated(o)); 
            _cmdDispatch[BeamMessage.kGameJoined] = new Func<object, bool>(o => OnGameJoined(o));              
            _cmdDispatch[BeamMessage.kPeerJoined] = new Func<object, bool>(o => OnPeerJoined(o));

            game = (BeamGameInstance)gameInst;
            settings = game.frontend.GetUserSettings();

            game.ClearPeers();
            game.ClearBikes();    
            game.ClearPlaces();     

            game.frontend.ModeHelper()
                .OnStartMode(BeamModeFactory.kConnect, null );         

            // need to "connect"first in order to have a p2pId
            game.gameNet.Connect(settings.p2pConnectionString);
            string p2pId = game.gameNet.LocalP2pId();
            BeamPeer localPeer = _CreateLocalPeer(p2pId, settings);
            game.SetLocalPeer(localPeer);

   

            if (!settings.tempSettings.ContainsKey("gameId"))             
                _SetState(kCreatingGame, new BeamGameNet.GameCreationData());      
            else
                _SetState(kJoiningGame, settings.tempSettings["gameId"]);                                                    
        }

        public override void Loop(float frameSecs)
        {
            _loopFunc(frameSecs);
        }

        protected void _SetState(int newState, object startParam)
        {
            _curStateSecs = 0;
            _loopFunc = _doNothingLoop; // default
            switch (newState)
            {
            case kCreatingGame:
                UnityEngine.Debug.Log("Creating game");                     
                game.gameNet.CreateGame(startParam);  
                break;            
            case kJoiningGame:      
                UnityEngine.Debug.Log($"Joining Game {(string)startParam}");            
                game.gameNet.JoinGame((string)startParam);
                break;                      
            case kWaitingForPlayers:
            case kCreatingBike:
            case kReadyToPlay:
                break;
            default:
                logger.Error($"ModeConnect._SetState() - Unknown state: {newState}");
                break;
            }
        }

        protected void _doNothingLoop(float frameSecs) {}

        public bool OnGameCreated(object o)
        {
            string newGameId = ((GameCreatedMsg)o).gameId;
            Console.WriteLine($"Created game: {newGameId}");
            _SetState(kJoiningGame, newGameId);                   
            return true;
        }

        public bool OnGameJoined(object o)
        {
            string gameId = ((GameJoinedMsg)o).gameId;
            string localId = ((GameJoinedMsg)o).localId;            
            UnityEngine.Debug.Log($"Joined game: {gameId} as ID: {localId}");
            _SetState(kWaitingForPlayers, null);             
            return true;
        }

        public bool OnPeerJoined(object o)
        {
            BeamPeer p = ((PeerJoinedMsg)o).peer;
            Console.WriteLine($"Remote Peer Joined: {p.Name}, ID: {p.PeerId}");
            logger.Debug($"Peer joined: {p}");           
            return true;
        }

        protected BeamPeer _CreateLocalPeer(string p2pId, BeamUserSettings settings)
        {               
            // Game.LocalP2pId is not set yet
            return new BeamPeer(p2pId, settings.screenName, null, true);
        }
        


    }
}