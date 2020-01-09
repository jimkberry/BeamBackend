using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    public class TargetIdParams {public string targetId;}   
        
    public interface IFrontendModeHelper 
    {
        void OnStartMode(int modeId, object param);
        void DispatchCmd(int modeId, int cmdId, object param);
        void OnEndMode(int modeId, object param);
    }


    public interface IBeamFrontend 
    {
        // Called by backend

        BeamUserSettings GetUserSettings();

        // Game Modes
        void OnStartMode(int modeId, object param = null);
        void OnEndMode(int modeId, object param = null);        

        // Players
        void OnNewPeer(BeamPeer p, int modeId=-1);
        void OnPeerLeft(string p2pId, int modeId=-1);
        void OnClearPeers(int modeId=-1);        
        // Bikes
        void OnNewBike(IBike ib, int modeId=-1);
        void OnBikeRemoved(string bikeId, bool doExplode, int modeId=-1);
        void OnClearBikes(int modeId=-1);
        void OnBikeAtPlace(string bikeId, Ground.Place place, bool justClaimed, int modeId=-1);
        // Places
        void SetupPlaceMarker(Ground.Place p, int modeId=-1);    
        void OnFreePlace(Ground.Place p, int modeId=-1);     
        void OnClearPlaces(int modeId=-1);
        // scoring
        // void OnScoreEvent(string bikeId, ScoreEvent evt, Ground.Place place); Need this?
    }

}
