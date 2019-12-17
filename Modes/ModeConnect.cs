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

            _cmdDispatch[BeamMessage.MsgType.kGameCreated ] = new Func<object, bool>(o => OnGameCreated(o)); 
            _cmdDispatch[BeamMessage.MsgType.kGameJoined] = new Func<object, bool>(o => OnGameJoined(o));              
            _cmdDispatch[BeamMessage.MsgType.kPlayerJoined] = new Func<object, bool>(o => OnPlayerJoined(o));

            game = (BeamGameInstance)gameInst;
            settings = game.frontend.GetUserSettings();

            game.ClearPlayers();
            game.ClearBikes();    
            game.ClearPlaces();     

            game.frontend.ModeHelper()
                .OnStartMode(BeamModeFactory.kConnect, null );         

            Player localPlayer = _CreateLocalPlayer(settings);
            game.SetLocalPlayer(localPlayer);

            game.gameNet.Connect(settings.p2pConnectionString);

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

        public bool OnPlayerJoined(object o)
        {
            Player p = ((PlayerJoinedMsg)o).player;
            Console.WriteLine($"Remote Player Joined: {p.ScreenName}");
            logger.Debug($"Peer joined: {p}");           
            return true;
        }

        protected Player _CreateLocalPlayer(BeamUserSettings settings)
        {
            string scrName = settings.screenName;
            string playerId = string.Format("{0:X8}", (scrName + game.LocalPeerId).GetHashCode());
            logger.Debug($"{this.ModeName()}: Creating player. Name: {scrName}, id: {playerId}");            
            return new Player(game.LocalPeerId, playerId, scrName, null, true);
        }
        


    }
}