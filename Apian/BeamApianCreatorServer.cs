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

        protected override void SendRequestOrObservation(string destCh, ApianMessage msg)
        {
            if ((msg.MsgType == ApianMessage.CliObservation)
                && (ApianGroup?.GroupCreatorId != GameNet.LocalP2pId()))
            {
                Logger.Debug($"SendRequestOrObservation() We are not server, so don't send observations.");
                return;
            }
            base.SendRequestOrObservation(destCh, msg); // let this filter it too
        }

    }
}