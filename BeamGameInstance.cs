using System;
using System.Linq;
using System.Collections.Generic;
using GameModeMgr;
using UnityEngine;

namespace BeamBackend
{
    public class BeamGameData
    {
        public Dictionary<string, Player> Players { get; private set; } = null;
        public Dictionary<string, IBike> Bikes { get; private set; } = null;
	    public Ground Ground { get; private set; } = null;

        protected List<string> _bikeIdsToRemoveAfterLoop; // at end of Loop() any bikes listed here get removed
        public BeamGameData(IBeamFrontend fep)
        {
            Players = new Dictionary<string, Player>();
            Bikes = new Dictionary<string, IBike>();
            Ground = new Ground(fep);    
            _bikeIdsToRemoveAfterLoop = new List<string>();          
        }

        public Player GetPlayer(string playerId)
        {
            try { return Players[playerId];} catch (KeyNotFoundException){ return null;} 
        }

        public BaseBike GetBaseBike(string bikeId)
        {
            try { return Bikes[bikeId] as BaseBike;} catch (KeyNotFoundException){ return null;}
        }

        public void PostBikeRemoval(string bikeId) => _bikeIdsToRemoveAfterLoop.Add(bikeId);

        public void Init() 
        {
            Players.Clear();
            Bikes.Clear();
        }

        public void Loop(float frameSecs)
        {
            Ground.Loop(frameSecs);
            foreach( IBike ib in Bikes.Values)
                ib.Loop(frameSecs);  // Bike "ib" might get destroyed here and need to be removed

             _bikeIdsToRemoveAfterLoop.RemoveAll( bid => {Bikes.Remove(bid); return true; });

        }

        public IBike ClosestBike(IBike thisBike)
        {  
            return Bikes.Count <= 1 ? null : Bikes.Values.Where(b => b != thisBike)
                    .OrderBy(b => Vector2.Distance(b.position, thisBike.position)).First();
        }   

        public List<Vector2> CloseBikePositions(IBike thisBike, int maxCnt)
        {
            // Todo: this is actually "current enemy pos"         
            return Bikes.Values.Where(b => b != thisBike)
                .OrderBy(b => Vector2.Distance(b.position, thisBike.position)).Take(maxCnt) // IBikes
                .Select(ob => ob.position).ToList();
        }                 
    }

    public class BeamGameInstance : IGameInstance, IBeamBackend
    {
        public ModeManager modeMgr {get; private set;}
        public  BeamGameData gameData {get; private set;}
        public  IBeamFrontend frontend {get; private set;}

        public BeamGameInstance(IBeamFrontend fep = null)
        {
            modeMgr = new ModeManager(new BeamModeFactory(), this);
            frontend = fep; // Should work without one
            gameData = new BeamGameData(frontend);            
        }

        // IGameInstance
        public void Start()
        {
            modeMgr.Start(BeamModeFactory.kSplash);
        }

        public bool Loop(float frameSecs)
        {
            //UnityEngine.Debug.Log("Inst.Loop()");
            gameData.Loop(frameSecs);
            return modeMgr.Loop(frameSecs);
        }

        //
        // IBeamBackend (requests from the frontend)
        // 

        public void OnSwitchModeReq(int newModeId, object modeParam)
        {
           modeMgr.SwitchToMode(newModeId, modeParam);       
        }

        public void OnTurnReq(string bikeId, TurnDir turn)
        {
            // TODO: In real life, this message from the FE should get sent to the net layer
            // and looped back, rather than directly talking to the bike
            //UnityEngine.Debug.Log(string.Format("Backend.OnTurnReq({0}, {1})", bikeId, turn));               
            gameData.GetBaseBike(bikeId)?.PostPendingTurn(turn);            
        }       

        //
        // Messages from the network/consensus layer (external or internal loopback)
        //

        public void OnNewPlayerReq(Player p)
        {
            UnityEngine.Debug.Log(string.Format("** need to implement BeamGameInst.OnNewPlayerReq()")); 
        }
        public void OnNewBikeReq(IBike ib)
        {
            UnityEngine.Debug.Log(string.Format("** need to implement BeamGameInst.OnNewBikeReq()"));             
        }


        public void OnPlaceClaim(string bikeId, Vector2 pos)
        {
            // TODO: should be coming from net, instead of the backend
            BaseBike b = (BaseBike)gameData.Bikes[bikeId];            
            Ground.Place p = gameData.Ground.ClaimPlace(b, pos);
            // Ground sends message to FE when place s claimed
        }


        public void OnScoreEvent(BaseBike bike, ScoreEvent evt, Ground.Place place)
        {
            // TODO: as with above: This is coming from the backend (BaseBike, mostly) and should
            // be comming from the Net/event/whatever layer
            int scoreDelta = GameConstants.eventScores[(int)evt];
            bike.score += scoreDelta;

            if (evt == ScoreEvent.kHitEnemyPlace || evt == ScoreEvent.kHitFriendPlace)
            {
                // half of the deduction goes to the owner of the place, the rest is divded 
                // among the owner's team 
                // UNLESS: the bike doing the hitting IS the owner - then the rest of the team just splits it
                if (bike != place.bike) {
                    scoreDelta /= 2;
                    place.bike.score -= scoreDelta; // adds
                }

                IEnumerable<IBike> rewardedOtherBikes = 
                    gameData.Bikes.Values.Where( b => b != bike && b.player.Team == place.bike.player.Team);  // Bikes other the "bike" on affected team
                if (rewardedOtherBikes.Count() > 0)
                {
                    foreach (BaseBike b  in rewardedOtherBikes) 
                        b.score -= scoreDelta / rewardedOtherBikes.Count();
                }
            }

            if (evt == ScoreEvent.kOffMap || bike.score <= 0)
            {
                bike.score = 0;
                RemoveBike(bike);
            }
        }

        //
        // Hmm. Where do these go?
        //

        // Player-related
        public bool AddNewPlayer(Player p)
        {
            if  ( gameData.Players.ContainsKey(p.ID))
                return false;  

            gameData.Players[p.ID] = p;
            frontend?.OnNewPlayer(p);
            return true;
        }
        public void ClearPlayers()
        {
            frontend?.OnClearPlayers();     
            gameData.Players.Clear();
        }

        // Bike-related
        public void NewBike(IBike b)
        {
            //UnityEngine.Debug.Log(string.Format("NEW BIKE. ID: {0}, Pos: {1}", b.bikeId, b.position));            
            gameData.Bikes[b.bikeId] = b;
            frontend?.OnNewBike(b);
        }        

        public void RemoveBike(BaseBike bb, bool shouldBlowUp=true)
        {
            gameData.Ground.RemovePlacesForBike(bb);
            frontend?.OnBikeRemoved(bb.bikeId, shouldBlowUp);  
            bb.player.bikeId = ""; 
            gameData.PostBikeRemoval(bb.bikeId); // we're almost certainly iterating over the list of bikes so don;t remove it yet.
        }
        public void ClearBikes()
        {
            frontend?.OnClearBikes();
            gameData.Bikes.Clear();
        }

       // Ground-related
        public void ClearPlaces()
        {
            frontend?.OnClearPlaces();            
            gameData.Ground.ClearPlaces();
        }

        // Info    

    }

}