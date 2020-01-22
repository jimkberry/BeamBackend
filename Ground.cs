﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BeamBackend
{
    public class Ground 
    {
        // North is Z, East is X,  Y is up
        public static float gridSize = 10f; // assume a square grid    
        public static float minX = -500f;
        public static float maxX = 500f;
        public static float minZ = -500f;
        public static float maxZ = 500f;

        public static readonly int pointsPerAxis = 101;

        public static Vector2 zeroPos = new Vector2(0f, 0f);

        public static float secsHeld = 15; // TODO: Maybe should be per-bike and increase with time? Bike Trail FX would have to as well.

        public event EventHandler<Ground.Place> PlaceFreedEvt;     
        public event EventHandler<Ground.Place> SetupPlaceMarkerEvt;        
        public event EventHandler PlacesClearedEvt;

        public class Place
        {
            public int xIdx; // x index into array.
            public int zIdx;
            public IBike bike;
            public float secsLeft;

            public int posHash() => xIdx + zIdx * Ground.pointsPerAxis; // Is this useful?

            public Vector2 GetPos()
            {
                return PlacePos(xIdx,zIdx);
            }

            public static Vector2 PlacePos(int x, int z)
            {
                return new Vector2(x*gridSize+minX,z*gridSize+minZ);                
            }
        }

        // TODO: maybe use hash-indexed Dict instead of array? (See FeGround)
        public Place[,] placeArray = null; 
        protected List<Place> activePlaces = null;
        protected Stack<Place> freePlaces = null; // re-use released/expired ones

        //protected IBeamFrontend _feProxy = null;
        public Ground(IBeamFrontend fep)
        {
           // _feProxy = fep;
            InitPlaces();
        }


        // Update is called once per frame
        public void Loop(float deltaSecs)
        {
            // Assume that if it's in the active list it's not nulll
            // If secsLeft runs out then remove it.
            int removed = activePlaces.RemoveAll( p => {
                    p.secsLeft -= deltaSecs;
                    if (p.secsLeft <= 0)
                        RecyclePlace(p);
                    return p.secsLeft <= 0; // remove from active list
            });
            //if (removed > 0)
            //    Debug.Log(string.Format("--- Removed {0} places --- {1} still active --- {2} free -------------------", removed, activePlaces.Count, freePlaces.Count));
        }

        protected void RecyclePlace(Place p){

            PlaceFreedEvt?.Invoke(this,p);
            p.bike = null;                 
            freePlaces.Push(p); // add to free list
            placeArray[p.xIdx, p.zIdx] = null;
        }

        protected void InitPlaces()
        {         
            placeArray = new Place[pointsPerAxis,pointsPerAxis];
            activePlaces = new List<Place>();
            freePlaces = new Stack<Place>();         
        }

        public void ClearPlaces()
        {
            InitPlaces();
            PlacesClearedEvt?.Invoke(this, EventArgs.Empty);                             
        }

        public void RemovePlacesForBike(IBike bike)
        {
            activePlaces.RemoveAll( p => {
                    if (p.bike == bike)
                        RecyclePlace(p);
                    return p.bike == null; // remove from active list
            });        
        }


        public List<Ground.Place> PlacesForBike(IBike ib)
        {
            return activePlaces.Where(p => p.bike.bikeId == ib.bikeId).ToList();
        }        

        public Place GetPlace(Vector2 pos)
        {
            Vector2 gridPos = NearestGridPoint(pos);
            int xIdx = (int)Mathf.Floor((gridPos.x - minX) / gridSize );
            int zIdx = (int)Mathf.Floor((gridPos.y - minZ) / gridSize );
            //Debug.Log(string.Format("gridPos: {0}, xIdx: {1}, zIdx: {2}", gridPos, xIdx, zIdx));
            return IndicesAreOnMap(xIdx,zIdx) ? placeArray[xIdx,zIdx] : null;
        }

        public Place ClaimPlace(IBike bike, Vector2 pos)
        {
            // returns place ref if successful. null if already claimed or off map
            Vector2 gridPos = NearestGridPoint(pos); 
            int xIdx = (int)Mathf.Floor((gridPos.x - minX) / gridSize );
            int zIdx = (int)Mathf.Floor((gridPos.y - minZ) / gridSize ); 
            return ClaimPlace(bike, xIdx, zIdx);                  
        }
        public Place ClaimPlace(IBike bike, int xIdx, int zIdx, float secsLeft=-1)
        {
            Place p = IndicesAreOnMap(xIdx,zIdx) ? ( placeArray[xIdx,zIdx] ?? SetupPlace(bike, xIdx, zIdx,secsLeft) ) : null;
            // TODO: Should claiming a place already held by team reset the timer?
            return (p?.bike == bike) ? p : null;
        }

        public static Vector2 NearestGridPoint(Vector2 pos) 
        {
            float invGridSize = 1.0f / gridSize;
            return new Vector2( Mathf.Round(pos.x * invGridSize) * gridSize, Mathf.Round(pos.y * invGridSize) * gridSize);
        } 

        // Set up a place instance for use or re-use
        protected Place SetupPlace(IBike bike, int xIdx, int zIdx, float secsLeft )
        {
            Place p = freePlaces.Count > 0 ? freePlaces.Pop() : new Place(); 
            // Maybe populating a new one, maybe re-populating a used one.
            p.secsLeft = secsLeft < 0 ? secsLeft : secsHeld; // usually secsLeft is -1
            p.xIdx = xIdx;
            p.zIdx = zIdx;
            p.bike = bike;
            placeArray[xIdx, zIdx] = p;
            activePlaces.Add(p);
            SetupPlaceMarkerEvt?.Invoke(this,p);
            return p;
        }

        public bool PointIsOnMap(Vector2 pt)
        {
            int xIdx = (int)Mathf.Floor((pt.x - minX) / gridSize );
            int yIdx = (int)Mathf.Floor((pt.y - minZ) / gridSize );        
            return IndicesAreOnMap(xIdx, yIdx);
        }
        
        public bool IndicesAreOnMap(int xIdx, int yIdx)
        {
            return !(xIdx < 0 || yIdx < 0 || xIdx >= pointsPerAxis || yIdx >= pointsPerAxis);
        }
    }
}
