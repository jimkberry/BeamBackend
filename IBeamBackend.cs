using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    // From FE
    public interface IBeamBackend {
        void OnSwitchModeReq(int newMode, object modeParams);
        void OnNewPlayerReq(Player p);
        void OnNewBikeReq(IBike ib);
        void OnTurnReq(string bikeId, TurnDir turn);
    }

}
