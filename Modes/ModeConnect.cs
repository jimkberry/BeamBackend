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
            _SetState(kWaitingForPlayers1, null);             
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
                _SetState(kReadyToPlay, null);                         
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