using System.Collections.Generic;
using Newtonsoft.Json;
using GameNet;
using Apian;
using UniLog;
using UnityEngine;

namespace BeamBackend
{
    public class BeamApianCreatorServer : BeamApian
    {
        public BeamApianCreatorServer(IBeamGameNet _gn,  IBeamApianClient _client) : base(_gn, _client)
        {
            ApianGroup = new CreatorServerGroupManager(this);
        }
    }
}