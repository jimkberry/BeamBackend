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
        void ReportPlaceClaim(string bikeId, float xPos, float zPos);
        void ReportPlaceHit(string bikeId, int xIdx, int zIdx);
    }

    public interface IBeamGameNetClient : IGameNetClient
    {       
        void OnBikeCreateData(BikeCreateDataMsg msg, string srcId);
        void OnBikeDataReq(BikeDataReqMsg msg, string srcId);
        void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId);
        void OnPlaceClaimed(PlaceClaimReportMsg msg, string srcId);
        void OnPlaceHit(PlaceHitReportMsg msg, string srcId);        
    }    

    public class BeamGameNet : GameNetBase, IBeamGameNet
    {
        public readonly long kBikeUpdateMs = 500;
        protected Dictionary<string, long> _lastBikeUpdatesMs;

        protected Dictionary<string, Action<string, string, GameNetClientMessage>> _MsgHandlers;

        public class GameCreationData {}

        public BeamGameNet() : base() 
        {
            _lastBikeUpdatesMs = new Dictionary<string, long>();
            _MsgHandlers = new  Dictionary<string, Action<string, string, GameNetClientMessage>>() 
            {
                [BeamMessage.kBikeCreateData] = (f,t,m) => this._HandleBikeCreateData(f,t,m),
                [BeamMessage.kBikeDataReq] = (f,t,m) => this._HandleBikeDataReq(f,t,m),   
                [BeamMessage.kBikeUpdate] = (f,t,m) => this._HandleBikeUpdate(f,t,m),
                [BeamMessage.kPlaceClaimReport] = (f,t,m) => this._HandlePlaceClaimReport(f,t,m),
                [BeamMessage.kPlaceHitReport] = (f,t,m) => this._HandlePlaceHitReport(f,t,m),                                
            };            
        }

        public override void  CreateGame<GameCreationData>(GameCreationData data)
        {
            logger.Info($"CreateGame()");
            _SyncTrivialNewGame(); // Creates/sets an ID and enqueues OnGameCreated()
        }        

        // IBeamGameNet
        public void SendBikeCreateData(IBike ib, string destId = null)
        {
            logger.Info($"SendBikeCreateData()");            
            // Info to create a bike.
            // Broadcast this to send it to everyone
            BikeCreateDataMsg msg = new BikeCreateDataMsg(ib);
            _SendClientMessage( destId ?? CurrentGameId(), msg.msgType.ToString(), JsonConvert.SerializeObject(msg));
        }
        public void RequestBikeData(string bikeId, string destId)
        {
            logger.Info($"RequestBikeData()");              
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

        public void ReportPlaceClaim(string bikeId, float xPos, float zPos)
        {
            logger.Info($"ReportPlaceClaim()");            
            PlaceClaimReportMsg msg = new PlaceClaimReportMsg(bikeId, xPos, zPos);
            _SendClientMessage( CurrentGameId(), msg.msgType, JsonConvert.SerializeObject(msg));            
        }
        
        public void ReportPlaceHit(string bikeId, int xIdx, int zIdx)
        {
            logger.Info($"ReportPlaceHit()");                
            PlaceHitReportMsg msg = new PlaceHitReportMsg(bikeId, xIdx, zIdx);
            _SendClientMessage( CurrentGameId(), msg.msgType, JsonConvert.SerializeObject(msg));            
        }

        //
        // Beam message handlers
        //
        protected override void _HandleClientMessage(string from, string to, GameNetClientMessage msg)
        {
            try {
                _MsgHandlers[msg.clientMsgType](from, to, msg);
            } catch(KeyNotFoundException) {
                logger.Warn($"Unknown client message type: {msg.clientMsgType}");
            }
        }

        protected void _HandleBikeCreateData(string from, string to, GameNetClientMessage clientMessage)
        {
            logger.Info($"_HandleBikeCreateData()");              
            (client as IBeamGameNetClient).OnBikeCreateData(JsonConvert.DeserializeObject<BikeCreateDataMsg>(clientMessage.payload), from);
        }

        protected void _HandleBikeDataReq(string from, string to, GameNetClientMessage clientMessage)
        {
            logger.Info($"_HandleBikeDataReq()");             
            (client as IBeamGameNetClient).OnBikeDataReq(JsonConvert.DeserializeObject<BikeDataReqMsg>(clientMessage.payload), from);
        }

        protected void _HandleBikeUpdate(string from, string to, GameNetClientMessage clientMessage)
        {             
            // NOTE: Do NOT act on loopbacked bike update messages. These are NOT state chage events, just "helpers"
            // We could filter this in the client, but then all the deseriaizaltion and object creation would happen
            if (from != LocalP2pId())
                (client as IBeamGameNetClient).OnRemoteBikeUpdate(JsonConvert.DeserializeObject<BikeUpdateMsg>(clientMessage.payload), from);
        }

        protected void _HandlePlaceClaimReport(string from, string to, GameNetClientMessage clientMessage)
        {
            logger.Info($"_HandlePlaceClaimReport()");             
            // TRUSTY: owner of bike is authoritative
            // TODO: We can't know that here, but GameInst can. A proper Apian implmentation of this 
            // trust gamenet would have to be able to ask questions about game internals.
            // maybe the Apian implmentation is part of the backend, and not gamenet?
            //PlaceClaimReportMsg msg = JsonConvert.DeserializeObject<PlaceClaimReportMsg>(clientMessage.payload);

            // For now we'll just apss it to the game inst, which will have to decide
            (client as IBeamGameNetClient).OnPlaceClaimed(JsonConvert.DeserializeObject<PlaceClaimReportMsg>(clientMessage.payload), from);
        }

        protected void _HandlePlaceHitReport(string from, string to, GameNetClientMessage clientMessage)
        {
            logger.Info($"_HandlePlaceHitReport()");              
            // See above. In trustyworld, place owner is authoritative
            (client as IBeamGameNetClient).OnPlaceHit(JsonConvert.DeserializeObject<PlaceHitReportMsg>(clientMessage.payload), from);
        }
    }

}