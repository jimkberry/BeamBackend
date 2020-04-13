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
    public class NetPeerData 
    {
        public BeamPeer peer;
    }

    public class BeamGameData : IApianStateData
    {
        public Dictionary<string, BeamPeer> Peers { get; private set; } = null;
        public Dictionary<string, IBike> Bikes { get; private set; } = null;
	    public Ground Ground { get; private set; } = null;

        protected List<string> _bikeIdsToRemoveAfterLoop; // at end of Loop() any bikes listed here get removed
        public BeamGameData(IBeamFrontend fep)
        {
            Peers = new Dictionary<string, BeamPeer>();
            Bikes = new Dictionary<string, IBike>();
            Ground = new Ground(fep);    
            _bikeIdsToRemoveAfterLoop = new List<string>();          
        }

        public void Init() 
        {
            Peers.Clear();
            Bikes.Clear();
        }

        public void Loop(float frameSecs)
        {
            Ground.Loop(frameSecs);
            foreach( IBike ib in Bikes.Values)
                ib.Loop(frameSecs);  // Bike "ib" might get destroyed here and need to be removed

            _bikeIdsToRemoveAfterLoop.RemoveAll( bid => {Bikes.Remove(bid); return true; });

        }

        public string ApianSerialized()
        {
            object[] peersData = Peers.Values.OrderBy(p => p.PeerId).Select(p => p.ApianSerialized()).ToArray();
            object[] bikesData = Bikes.Values.OrderBy(ib => ib.bikeId).Select(ib => ib.ApianSerialized()).ToArray();
            return  JsonConvert.SerializeObject(new object[]{
                peersData,
                bikesData,
                Ground.ApianSerialized()
            });

        }

        public BeamPeer GetPeer(string peerId)
        {
            try { return Peers[peerId];} catch (KeyNotFoundException){ return null;} 
        }

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
    }

}