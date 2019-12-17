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
        IFrontendModeHelper ModeHelper();

        // Players
        void OnNewPeer(BeamPeer p);
        void OnPeerLeft(BeamPeer p);
        void OnClearPeers();        
        // Bikes
        void OnNewBike(IBike ib);
        void OnBikeRemoved(string bikeId, bool doExplode);
        void OnClearBikes();
        void OnBikeAtPlace(string bikeId, Ground.Place place, bool justClaimed);
        // Places
        void SetupPlaceMarker(Ground.Place p);    
        void OnFreePlace(Ground.Place p);     
        void OnClearPlaces();
        // scoring
        // void OnScoreEvent(string bikeId, ScoreEvent evt, Ground.Place place); Need this?
    }

}
