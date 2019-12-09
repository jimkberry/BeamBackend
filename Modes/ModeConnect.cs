using System.Reflection.Emit;
using System;
using GameModeMgr;
namespace BeamBackend
{
    public class ModeConnect : BaseGameMode
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

            _cmdDispatch["GameCreatedMsg"] = new Func<object, bool>(o => OnGameCreated(o)); 
            _cmdDispatch["GameJoinedMsg"] = new Func<object, bool>(o => OnGameJoined(o));              

            game = (BeamGameInstance)gameInst;
            settings = game.frontend.GetUserSettings();

            game.ClearPlayers();
            game.ClearBikes();    
            game.ClearPlaces();     

            game.frontend.ModeHelper()
                .OnStartMode(BeamModeFactory.kConnect, null );         

            game.gameNet.Connect(settings.p2pConnectionString);

            if (settings.gameId == null)
            {
                game.gameNet.CreateGame(new BeamGameNet.GameCreationData());
                UnityEngine.Debug.Log("Creating game");
                _curState = kCreatingGame;                         
            }
            else
            {
                game.gameNet.JoinGame(settings.gameId);
                _curState = kJoiningGame;                    
            }
        }

        public override void Loop(float frameSecs)
        {

        }

        public bool OnGameCreated(object o)
        {
            string newGameId = ((BeamMessages.GameCreatedMsg)o).gameId;
            UnityEngine.Debug.Log($"Created game: {newGameId}");
            game.gameNet.JoinGame(newGameId);
            _curState = kJoiningGame;              
            return true;
        }

        public bool OnGameJoined(object o)
        {
            string gameId = ((BeamMessages.GameJoinedMsg)o).gameId;
            string localId = ((BeamMessages.GameJoinedMsg)o).localId;            
            UnityEngine.Debug.Log($"Joined game: {gameId} as ID: {localId}");
            _curState = kWaitingForPlayers;              
            return true;
        }

    }
}