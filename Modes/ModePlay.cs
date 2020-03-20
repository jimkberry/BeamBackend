using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;

namespace BeamBackend
{
    public class ModePlay : BeamGameMode
    {
        public readonly int kMaxPlayers = 12;        

        public BeamGameInstance game = null;

        protected BaseBike playerBike = null;

        protected const float kRespawnCheckInterval = .33f;

        protected float _secsToNextRespawnCheck = kRespawnCheckInterval; 

		public override void Start(object param = null)	
        {
            base.Start();          
            game = (BeamGameInstance)gameInst; // Todo - this oughta be in a higher-level BeamGameMode 
            game.RespawnPlayerEvt += OnRespawnPlayerEvt; 

            game.ClearPlaces();     
            IBike playerBike = game.gameData.LocalBikes(game.LocalPeerId).First();
            _startLocalBikes();
            game.frontend?.OnStartMode(BeamModeFactory.kPlay, new TargetIdParams{targetId = playerBike.bikeId} );             
        }

		public override void Loop(float frameSecs) 
        {

        }

		public override object End() {            
            game.frontend?.OnEndMode(game.modeMgr.CurrentModeId(), null);
            game.ClearPeers();
            game.ClearBikes();    
            game.ClearPlaces();              
            return null;
        } 

        protected void _startLocalBikes()
        {
            foreach( IBike ib in game.gameData.LocalBikes(game.LocalPeerId)) { game.PostBikeCommand(ib, BikeCommand.kGo);  }
        }

        protected string CreateBaseBike(int ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( game.gameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );  
            string bikeId = Guid.NewGuid().ToString();
            BaseBike bb = new BaseBike(game, bikeId, peerId, name, t, ctrlType, pos, heading, BaseBike.defaultSpeed);
            game.PostBikeCreateData(bb); 
            return bb.bikeId;
        }

        protected string SpawnPlayerBike()
        {
            // Create one the first time
            string scrName = game.frontend.GetUserSettings().screenName;
            string bikeId = string.Format("{0:X8}", (scrName + game.LocalPeerId).GetHashCode());
            return CreateBaseBike(BikeFactory.LocalPlayerCtrl, game.LocalPeerId, game.LocalPeer.Name, game.LocalPeer.Team);                 
        }        

        public void OnRespawnPlayerEvt(object sender, EventArgs args)
        {
            logger.Info("Respawning Player");
            SpawnPlayerBike();
            // Note that this will eventually result in a NewBikeEvt which the frontend 
            // will catch and deal with. Maybe it'll point a camera at the new bike or whatever.            
        }
 
    }
}