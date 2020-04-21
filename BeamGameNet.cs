using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using GameNet;
using P2pNet;
using Apian;

namespace BeamBackend
{
    public interface IBeamGameNet : IApianGameNet
    {
        void RequestBikeData(string bikeId, string destId);
    }

    public interface IBeamGameNetClient : IApianGameNetClient
    {
        void OnBikeDataQuery(BikeDataQueryMsg msg, string from, long msSinceSent);
    }

    public class BeamGameNet : ApianGameNet, IBeamGameNet
    {


        public BeamGameNet() : base()
        {
            _MsgHandlers[BeamMessage.kBikeDataQuery] = (f,t,s,m) => this._HandleBikeDataQuery(f,t,s,m);
        }

        protected override IP2pNet P2pNetFactory(string p2pConnectionString)
        {
            // P2pConnectionString is <p2p implmentation name>::<imp-dependent connection string>
            // Names are: p2ploopback, p2predis

            IP2pNet ip2p = null;
            string[] parts = p2pConnectionString.Split(new string[]{"::"},StringSplitOptions.None); // Yikes! This is fugly.

            switch(parts[0].ToLower())
            {
                case "p2predis":
                    ip2p = new P2pRedis(this, parts[1]);
                    break;
                case "p2ploopback":
                    ip2p = new P2pLoopback(this, null);
                    break;
                // case "p2pactivemq":
                //     p2p = new P2pActiveMq(this, parts[1]);
                //     break;
                default:
                    throw( new Exception($"Invalid connection type: {parts[0]}"));
            }

            if (ip2p == null)
                throw( new Exception("p2p Connect failed"));

            return ip2p;
        }

        public override void  CreateGame<GameCreationData>(GameCreationData data)
        {
            logger.Verbose($"CreateGame()");
            _SyncTrivialNewGame(); // Creates/sets an ID and enqueues OnGameCreated()
        }

        // Sending

        // IBeamGameNet

        public void RequestBikeData(string bikeId, string destId)
        {
            logger.Verbose($"RequestBikeData()");
            BikeDataQueryMsg msg = new BikeDataQueryMsg(ApianInst.ApianClock.CurrentTime, bikeId);
            _SendClientMessage( destId, msg.MsgType.ToString(), JsonConvert.SerializeObject(msg));
        }


        //
        // Beam message handlers
        //
        protected void _HandleBikeDataQuery(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {
            // TODO: this protocol (see a message about a bike you don't know / ask for data about it)  doesn;t work
            // with a proper Consensus System. I mean, I guess it could as part of the member sync process,
            // but it really doesn;t belong here
            logger.Verbose($"_HandleBikeDataQuery() src: {(from==LocalP2pId()?"Local":from)}");
            (client as IBeamGameNetClient).OnBikeDataQuery(JsonConvert.DeserializeObject<BikeDataQueryMsg>(clientMessage.payload), from, msSinceSent);
        }
    }

}