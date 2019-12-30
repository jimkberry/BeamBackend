using System.Reflection.Emit;
using System;
using GameModeMgr;
namespace BeamBackend
{
    public class ModeConnect : BeamGameMode
    {
        protected const int kCreatingGame = 0;        
        protected const int kJoiningGame = 1;
        protected const int kWaitingForPlayers = 2;

        public BeamGameInstance game = null; 
        public BeamUserSettings settings = null;     

        protected int _curState = kCreatingGame;

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
            {
                game.gameNet.CreateGame(new BeamGameNet.GameCreationData());
                UnityEngine.Debug.Log("Creating game");
                _curState = kCreatingGame;                         
            }
            else
            {
                game.gameNet.JoinGame(settings.tempSettings["gameId"]);
                _curState = kJoiningGame;                    
            }
        }

        public override void Loop(float frameSecs)
        {

        }

        public bool OnGameCreated(object o)
        {
            string newGameId = ((GameCreatedMsg)o).gameId;
            Console.WriteLine($"Created game: {newGameId}");           
            game.gameNet.JoinGame(newGameId);
            _curState = kJoiningGame;              
            return true;
        }

        public bool OnGameJoined(object o)
        {
            string gameId = ((GameJoinedMsg)o).gameId;
            string localId = ((GameJoinedMsg)o).localId;            
            UnityEngine.Debug.Log($"Joined game: {gameId} as ID: {localId}");
            _curState = kWaitingForPlayers;              
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