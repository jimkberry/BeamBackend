using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using GameModeMgr;
using UnityEngine;
using GameNet;
using Apian;
using UniLog;

namespace BeamBackend
{
    public class BeamGameData : IApianStateData
    {
        public event EventHandler<BeamPlace> PlaceFreedEvt;
        public event EventHandler<BeamPlace> SetupPlaceMarkerEvt;
        public event EventHandler<BeamPlace> PlaceTimeoutEvt;
        public event EventHandler PlacesClearedEvt;


        // Here's the actual base state data:
	    public Ground Ground { get; private set; } = null; // TODO: Is there any mutable state here anymore?
        public Dictionary<string, BeamPlayer> Players { get; private set; } = null;
        public Dictionary<string, IBike> Bikes { get; private set; } = null;
        public List<BeamPlace> activePlaces = null; // TODO: use hash-indexed Dict instead of this and the array! (See FeGround)
        public BeamPlace[,] placeArray = null;

        // Ancillary data (initialize to empty if loading state data)
        protected Stack<BeamPlace> freePlaces = null; // re-use released/expired ones
        protected List<string> _bikeIdsToRemoveAfterLoop; // at end of Loop() any bikes listed here get removed
        protected List<BeamPlace> _placesToRemoveAfterLoop; // Places also are not destroyed until the end of the data loop

        public BeamGameData(IBeamFrontend fep)
        {
            Players = new Dictionary<string, BeamPlayer>();
            Bikes = new Dictionary<string, IBike>();
            Ground = new Ground();
            InitPlaces();

            _bikeIdsToRemoveAfterLoop = new List<string>();
        }

        public void Init()
        {
            Players.Clear();
            Bikes.Clear();
            InitPlaces();
        }

        protected void InitPlaces()
        {
            placeArray = new BeamPlace[Ground.pointsPerAxis,Ground.pointsPerAxis];
            activePlaces = new List<BeamPlace>();
            freePlaces = new Stack<BeamPlace>();
            _placesToRemoveAfterLoop = new List<BeamPlace>();
        }

        public void Loop(long nowMs, long frameMs)
        {
             foreach( IBike ib in Bikes.Values)
                ib.Loop(frameMs * .001f);  // Bike might get "destroyed" here and need to be removed

            LoopPlaces(nowMs);

            _bikeIdsToRemoveAfterLoop.RemoveAll( bid => {Bikes.Remove(bid); return true; });
            _placesToRemoveAfterLoop.RemoveAll( p => { RemoveActivePlace(p); return true; } );

        }

        protected void LoopPlaces(long nowMs)
        {
            List<BeamPlace> timedOutPlaces = new List<BeamPlace>();
            // Be very, very careful not to do something that might recusively delete a lest member while iterating over the list
            foreach (BeamPlace p in activePlaces)
                if (p.expirationTimeMs <= nowMs)
                    timedOutPlaces.Add(p);

            foreach (BeamPlace p  in timedOutPlaces )
                PlaceTimeoutEvt?.Invoke(this,p); // causes GameInst to post a PlaceRemovedMsg
        }

        public string ApianSerialized()
        {
            object[] peersData = Players.Values.OrderBy(p => p.PeerId).Select(p => p.ToBeamJson()).ToArray();
            object[] bikesData = Bikes.Values.OrderBy(ib => ib.bikeId).Select(ib => ib.ApianSerialized()).ToArray();
                // All that is needed here is a list of the active places.
                // The position arrays can be reconstructed by calling SetupPlace() on the placedata
            object[] placesData = activePlaces.OrderBy<BeamPlace,int>(p => p.posHash()).Select(p => p.ApianSerialized()).ToArray();

            return  JsonConvert.SerializeObject(new object[]{
                peersData,
                bikesData,
                placesData
            });

        }

        public BeamPlayer GetMember(string peerId)
        {
            try { return Players[peerId];} catch (KeyNotFoundException){ return null;}
        }

        // Bike stuff

        public BaseBike GetBaseBike(string bikeId)
        {
            try { return Bikes[bikeId] as BaseBike;} catch (KeyNotFoundException){ return null;}
        }

        public void PostBikeRemoval(string bikeId) => _bikeIdsToRemoveAfterLoop.Add(bikeId);


        public IBike ClosestBike(IBike thisBike)
        {
            return Bikes.Count <= 1 ? null : Bikes.Values.Where(b => b != thisBike)
                    .OrderBy(b => Vector2.Distance(b.position, thisBike.position)).First();
        }

        public List<IBike> LocalBikes(string peerId)
        {
            return Bikes.Values.Where(ib => ib.peerId == peerId).ToList();
        }

        public List<Vector2> CloseBikePositions(IBike thisBike, int maxCnt)
        {
            // Todo: this is actually "current enemy pos"
            return Bikes.Values.Where(b => b != thisBike)
                .OrderBy(b => Vector2.Distance(b.position, thisBike.position)).Take(maxCnt) // IBikes
                .Select(ob => ob.position).ToList();
        }

        // Places stuff

        // Set up a place instance for use or re-use
        protected BeamPlace SetupPlace(IBike bike, int xIdx, int zIdx, long expireTimeMs )
        {
            BeamPlace p = freePlaces.Count > 0 ? freePlaces.Pop() : new BeamPlace();
            // Maybe populating a new one, maybe re-populating a used one.
            p.expirationTimeMs = expireTimeMs;
            p.xIdx = xIdx;
            p.zIdx = zIdx;
            p.bike = bike;
            placeArray[xIdx, zIdx] = p;
            activePlaces.Add(p);
            SetupPlaceMarkerEvt?.Invoke(this,p);
            return p;
        }

        public void PostPlaceRemoval(BeamPlace p) => _placesToRemoveAfterLoop.Add(p);

        protected void RemoveActivePlace(BeamPlace p)
        {
            PlaceFreedEvt?.Invoke(this,p);
            p.bike = null;
            freePlaces.Push(p); // add to free list
            placeArray[p.xIdx, p.zIdx] = null;
            activePlaces.Remove(p);
        }

        public void ClearPlaces()
        {
            InitPlaces();
            PlacesClearedEvt?.Invoke(this, EventArgs.Empty);
        }

        public void RemovePlacesForBike(IBike bike)
        {
            foreach (BeamPlace p in PlacesForBike(bike))
                PostPlaceRemoval(p);
        }
        public List<BeamPlace> PlacesForBike(IBike ib)
        {
            return activePlaces.Where(p => p.bike.bikeId == ib.bikeId).ToList();
        }

        public BeamPlace GetPlace(int xIdx, int zIdx) => placeArray[xIdx,zIdx];

        public BeamPlace GetPlace(Vector2 pos)
        {
            Vector2 gridPos = Ground.NearestGridPoint(pos);
            int xIdx = (int)Mathf.Floor((gridPos.x - Ground.minX) / Ground.gridSize ); // TODO: this is COPY/PASTA EVERYWHERE!!! FIX!!!
            int zIdx = (int)Mathf.Floor((gridPos.y - Ground.minZ) / Ground.gridSize );
            //Debug.Log(string.Format("gridPos: {0}, xIdx: {1}, zIdx: {2}", gridPos, xIdx, zIdx));
            return Ground.IndicesAreOnMap(xIdx,zIdx) ? placeArray[xIdx,zIdx] : null;
        }

        public BeamPlace ClaimPlace(IBike bike, int xIdx, int zIdx, long expireTimeMs)
        {
            BeamPlace p = Ground.IndicesAreOnMap(xIdx,zIdx) ? ( placeArray[xIdx,zIdx] ?? SetupPlace(bike, xIdx, zIdx,expireTimeMs) ) : null;
            return (p?.bike == bike) ? p : null;
        }

    }

}