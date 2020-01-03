using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    // From FE
    // TODO: This is getting sparse - is it needed?
    public interface IBeamBackend {
        void OnSwitchModeReq(int newMode, object modeParams);
        void OnTurnReq(string bikeId, TurnDir turn);
    }

}
