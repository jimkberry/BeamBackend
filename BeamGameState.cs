using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Apian;
using UniLog;

namespace BeamBackend
{
    public class BeamGameState : IApianStateData
    {
        public event EventHandler<BeamPlace> PlaceFreedEvt;
        public event EventHandler<BeamPlace> SetupPlaceMarkerEvt;
        public event EventHandler<BeamPlace> PlaceTimeoutEvt;
        public event EventHandler PlacesClearedEvt;

        public UniLogger Logger;

        // Here's the actual base state data:
	    public Ground Ground { get; private set; } = null; // TODO: Is there any mutable state here anymore?
        public Dictionary<string, BeamPlayer> Players { get; private set; } = null;
        public Dictionary<string, IBike> Bikes { get; private set; } = null;
        public Dictionary<int, BeamPlace> activePlaces = null; //  BeamPlace.PosHash() -indexed Dict of places.

        // Ancillary data (initialize to empty if loading state data)
        protected Stack<BeamPlace> freePlaces = null; // re-use released/expired ones
        protected List<string> _bikeIdsToRemoveAfterLoop; // at end of Loop() any bikes listed here get removed
        protected List<BeamPlace> _placesToRemoveAfterLoop; // Places also are not destroyed until the end of the data loop
        protected Dictionary<int, BeamPlace> _reportedTimedOutPlaces; // places that have been reported as timed out, but not removed yet

        public BeamGameState(IBeamFrontend fep)
        {
            Logger = UniLogger.GetLogger("GameState");
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
            activePlaces = new Dictionary<int, BeamPlace>();
            freePlaces = new Stack<BeamPlace>();
            _placesToRemoveAfterLoop = new List<BeamPlace>();
            _reportedTimedOutPlaces  = new Dictionary<int, BeamPlace>(); // check this before reporting. delete entry when removed.
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
            // This is probably unneeded givent that PostPlaceRemoval() exists
            foreach (BeamPlace p in activePlaces.Values)
                if (p.expirationTimeMs <= nowMs)
                    timedOutPlaces.Add(p);

            foreach (BeamPlace p  in timedOutPlaces )
            {
                if ( !_reportedTimedOutPlaces.ContainsKey(p.PosHash))
                {
                    _reportedTimedOutPlaces[p.PosHash] = p;
                    PlaceTimeoutEvt?.Invoke(this,p); // causes GameInst to post a PlaceRemovedMsg
                }
            }
        }

        public class SerialArgs
        {
            public long seqNum;
            public long timeStamp;
            public SerialArgs(long sn, long ts) {seqNum=sn; timeStamp=ts;}
        };

        public string ApianSerialized(object args=null)
        {
            // args is [lonf seqNum, long timeStamp]
            SerialArgs sArgs = args as SerialArgs;

            // create array index lookups for peers, bikes to replace actual IDs (which are long) in serialized data
            Dictionary<string,int> peerIndicesDict =  Players.Values.OrderBy(p => p.PeerId)
                .Select((p,idx) => new {p.PeerId, idx}).ToDictionary( x =>x.PeerId, x=>x.idx);

            Dictionary<string,int> bikeIndicesDict =  Bikes.Values.OrderBy(b => b.bikeId)
                .Select((b,idx) => new {b.bikeId, idx}).ToDictionary( x =>x.bikeId, x=>x.idx);

            // Not all of the data needs the timestamp
            object[] peersData = Players.Values.OrderBy(p => p.PeerId)
                .Select(p => p.ApianSerialized()).ToArray();
            object[] bikesData = Bikes.Values.OrderBy(ib => ib.bikeId)
                .Select(ib => ib.ApianSerialized(new BaseBike.SerialArgs(peerIndicesDict,sArgs.timeStamp))).ToArray();
            object[] placesData = activePlaces.Values
                .OrderBy(p => p.expirationTimeMs).ThenBy(p => p.PosHash)
                .Select(p => p.ApianSerialized(new BeamPlace.SerialArgs(bikeIndicesDict))).ToArray();

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
            activePlaces[p.PosHash] = p;
            SetupPlaceMarkerEvt?.Invoke(this,p);
            return p;
        }

        public void PostPlaceRemoval(BeamPlace p) => _placesToRemoveAfterLoop.Add(p);

        protected void RemoveActivePlace(BeamPlace p)
        {
            if (p != null)
            {
                Logger.Verbose($"RemoveActivePlace({p.GetPos().ToString()}) Bike: {p.bike?.bikeId}");
                PlaceFreedEvt?.Invoke(this,p);
                freePlaces.Push(p); // add to free list
                activePlaces.Remove(p.PosHash);
                _reportedTimedOutPlaces.Remove(p.PosHash);
                p.bike = null; // this is the only reference it holds
            }
        }

        public void ClearPlaces()
        {
            InitPlaces();
            PlacesClearedEvt?.Invoke(this, EventArgs.Empty);
        }

        public void RemovePlacesForBike(IBike bike)
        {
            Logger.Verbose($"RemovePlacesForBike({bike.bikeId})");
            foreach (BeamPlace p in PlacesForBike(bike))
                PostPlaceRemoval(p);
        }

        public List<BeamPlace> PlacesForBike(IBike ib)
        {
            return activePlaces.Values.Where(p => p.bike.bikeId == ib.bikeId).ToList();
        }

        // public List<BeamPlace> PlacesForBike(IBike ib)
        // {
        //     return activePlaces.Values.Where(p =>
        //         {
        //             if ( ib == null)
        //                 Logger.Warn($"PlacesForBike() null Bike!");

        //             if ( p == null)
        //                 Logger.Warn($"PlacesForBike() null place!");
        //             if (p.bike == null)
        //                 Logger.Warn($"PlacesForBike() Active place {p.GetPos().ToString()} has null bike.");
        //             return p.bike?.bikeId == ib.bikeId;
        //         } ).ToList();
        // }

        public BeamPlace GetPlace(int xIdx, int zIdx) => activePlaces.GetValueOrDefault(BeamPlace.MakePosHash(xIdx,zIdx), null);

        public BeamPlace GetPlace(Vector2 pos)
        {
            Vector2 gridPos = Ground.NearestGridPoint(pos);
            int xIdx = (int)Mathf.Floor((gridPos.x - Ground.minX) / Ground.gridSize ); // TODO: this is COPY/PASTA EVERYWHERE!!! FIX!!!
            int zIdx = (int)Mathf.Floor((gridPos.y - Ground.minZ) / Ground.gridSize );
            //Debug.Log(string.Format("gridPos: {0}, xIdx: {1}, zIdx: {2}", gridPos, xIdx, zIdx));
            return Ground.IndicesAreOnMap(xIdx,zIdx) ? GetPlace(xIdx,zIdx) : null; // note this returns null for "no place" and for "out of bounds"
        }

        public BeamPlace ClaimPlace(IBike bike, int xIdx, int zIdx, long expireTimeMs)
        {
            BeamPlace p = Ground.IndicesAreOnMap(xIdx,zIdx) ? ( GetPlace(xIdx,zIdx) ?? SetupPlace(bike, xIdx, zIdx,expireTimeMs) ) : null;
            return (p?.bike == bike) ? p : null;
        }

    }

}