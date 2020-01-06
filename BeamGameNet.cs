using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using GameNet;

namespace BeamBackend
{
    public interface IBeamGameNet : IGameNet
    {
        void SendBikeCreateData(IBike ib, string destId = null);
        void RequestBikeData(string bikeId, string destId);
        void SendBikeUpdates(List<IBike> localBikes);
    }

    public interface IBeamGameNetClient : IGameNetClient
    {
        void OnBikeCreateData(BikeCreateDataMsg msg, string srcId);
        void OnBikeDataReq(BikeDataReqMsg msg, string srcId);
        void OnBikeUpdate(BikeUpdateMsg msg, string srcId);
    }    

    public class BeamGameNet : GameNetBase, IBeamGameNet
    {
        public readonly long kBikeUpdateMs = 500;
        protected Dictionary<string, long> _lastBikeUpdatesMs;

        public class GameCreationData {}

        public BeamGameNet() : base() 
        {
            _lastBikeUpdatesMs = new Dictionary<string, long>();
        }

        public override void  CreateGame<GameCreationData>(GameCreationData data)
        {
            logger.Info($"BeamGameNet.CreateGame()");
            _SyncTrivialNewGame(); // Creates/sets an ID and enqueues OnGameCreated()
        }        

        // IBeamGameNet
        public void SendBikeCreateData(IBike ib, string destId = null)
        {
            // Info to create a bike.
            // Broadcast this to send it to everyone
            BikeCreateDataMsg msg = new BikeCreateDataMsg(ib);
            _SendClientMessage( destId ?? CurrentGameId(), msg.msgType.ToString(), JsonConvert.SerializeObject(msg));
        }
        public void RequestBikeData(string bikeId, string destId)
        {
            BikeDataReqMsg msg = new BikeDataReqMsg(bikeId);
            _SendClientMessage( destId, msg.msgType.ToString(), JsonConvert.SerializeObject(msg));
        }

        public void SendBikeUpdates(List<IBike> localBikes)
        {
            long nowMs = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            foreach(IBike ib in localBikes)
            {
                long prevMs;
                if ( !_lastBikeUpdatesMs.TryGetValue(ib.bikeId, out prevMs) || (nowMs - prevMs > kBikeUpdateMs))
                {
                    _lastBikeUpdatesMs[ib.bikeId] = nowMs;  
                    logger.Debug($"BeamGameNet.SendBikeUpdates() Bike: {ib.bikeId}");                    
                    BikeUpdateMsg msg = new BikeUpdateMsg(ib);
                    _SendClientMessage(CurrentGameId(), msg.msgType.ToString(), JsonConvert.SerializeObject(msg));      
                }
            }
      
        }

        protected override void _HandleClientMessage(string from, string to, GameNetClientMessage clientMessage)
        {
            // TODO: write a dispatch table
            switch (clientMessage.clientMsgType)
            {
                case BeamMessage.kBikeCreateData:
                    (client as IBeamGameNetClient).OnBikeCreateData(JsonConvert.DeserializeObject<BikeCreateDataMsg>(clientMessage.payload), to);
                    break;
                case BeamMessage.kBikeDataReq:
                    (client as IBeamGameNetClient).OnBikeDataReq(JsonConvert.DeserializeObject<BikeDataReqMsg>(clientMessage.payload), to);
                    break;
                case BeamMessage.kBikeUpdate:
                    // NOTE: Do NOT act on loopbacked bike update messages. These are NOT state chage events, just "helpers"
                    // We could filter this in the client, but then all the deseriaizaltion and object creation would happen
                    if (from != LocalP2pId())
                        (client as IBeamGameNetClient).OnBikeUpdate(JsonConvert.DeserializeObject<BikeUpdateMsg>(clientMessage.payload), to);
                    break;                                        
            }
        }
    }
}