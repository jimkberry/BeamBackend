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
        protected const int kWaitingForPlayers1 = 2; // wait a couple seconds
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
            _cmdDispatch[BeamMessage.kPeerLeft] = new Func<object, bool>(o => OnPeerLeft(o));
            _cmdDispatch[BeamMessage.kNewBike] = new Func<object, bool>(o => OnNewBike(o));

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
            _curStateSecs += frameSecs;
        }

        protected void _SetState(int newState, object startParam = null)
        {
            _curStateSecs = 0;
            _loopFunc = _DoNothingLoop; // default
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
            case kWaitingForPlayers1:
                UnityEngine.Debug.Log($"Waiting for players");
                _loopFunc = _WaitForPlayers1Loop;
                break;
            case kCreatingBike:
                UnityEngine.Debug.Log($"Creating local bike");  
                _CreateLocalBike();    
                break;
            case kReadyToPlay:
                break;
            default:
                logger.Error($"ModeConnect._SetState() - Unknown state: {newState}");
                break;
            }
        }

        protected void _DoNothingLoop(float frameSecs) {}
        protected void _WaitForPlayers1Loop(float frameSecs) 
        {
            if (_curStateSecs > kWaitForPlayersSecs)
                _SetState(kCreatingBike);
        }

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
            game.SetGameId(gameId);           
            logger.Info($"Joined game: {gameId} as ID: {localId}");
            _SetState(kWaitingForPlayers1, null);             
            return true;
        }

        public bool OnPeerJoined(object o)
        {
            BeamPeer p = ((PeerJoinedMsg)o).peer;
            logger.Info($"Remote Peer Joined: {p.Name}, ID: {p.PeerId}");  
            game.AddPeer(p);                     
            return true;
        }

        public bool OnPeerLeft(object o)
        {
            string p2pId =  ((PeerLeftMsg)o).p2pId;
            logger.Info($"Remote Peer Left: {p2pId}");  
            game.RemovePeer(p2pId);                     
            return true;
        }      

        public bool OnNewBike(object o)
        {
            IBike ib =  ((NewBikeMsg)o).ib;
            logger.Info($"OnNewBike: {ib.bikeId}");                      
            return true;
        }  


        //
        // utils
        //

        protected BeamPeer _CreateLocalPeer(string p2pId, BeamUserSettings settings)
        {               
            // Game.LocalP2pId is not set yet
            return new BeamPeer(p2pId, settings.screenName, null, true);
        }
        

        protected void _CreateLocalBike()
        {
            // Create one the first time
            string scrName = game.frontend.GetUserSettings().screenName;
            string bikeId = string.Format("{0:X8}", (scrName + game.LocalPeerId).GetHashCode());
            BaseBike bb =  game.CreateBaseBike(BikeFactory.LocalPlayerCtrl, game.LocalPeerId, game.LocalPeer.Name, game.LocalPeer.Team);     
            game.gameNet.SendBikeCreateData(bb); // will result in OnBikeInfo()            
        }          


    }
}