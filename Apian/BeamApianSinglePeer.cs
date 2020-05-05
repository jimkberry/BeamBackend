using System.Collections.Generic;
using Newtonsoft.Json;
using GameNet;
using Apian;
using UniLog;
using UnityEngine;

namespace BeamBackend
{
    public class BeamApianSinglePeer : BeamApian
    {
        public BeamApianSinglePeer(IBeamGameNet _gn,  IBeamApianClient _client) : base(_gn, _client)
        {
            ApianGroup = new SinglePeerGroupManager(this);
            ApianClock.Set(0); // Need to start it running
        }


    }

}