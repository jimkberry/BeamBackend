using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using GameNet;
using P2pNet;

namespace BeamBackend
{
    public interface IBeamGameNet : IGameNet
    {
        void SendBikeCreateData(IBike ib, List<Ground.Place> ownedPlaces, string destId = null);
        void SendBikeUpdate(IBike localBike);        
        void RequestBikeData(string bikeId, string destId);
        void SendBikeUpdates(List<IBike> localBikes);

        void SendBikeTurnMsg(IBike bike, TurnDir dir, Vector2 nextPt);        
        void SendBikeCommandMsg(IBike bike, BikeCommand cmd, Vector2 nextPt);
        void ReportPlaceClaim(string bikeId, float xPos, float zPos);
        void ReportPlaceHit(string bikeId, int xIdx, int zIdx);
    }

    public interface IBeamGameNetClient : IGameNetClient
    {       
        void OnBikeCreateData(BikeCreateDataMsg msg, string srcId);
        void OnBikeDataReq(BikeDataReqMsg msg, string srcId);
        void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId);
        void OnBikeTurn(BikeTurnMsg msg, string srcId);
        void OnBikeCommand(BikeCommandMsg msg, string srcId);        
        void OnPlaceClaimed(PlaceClaimReportMsg msg, string srcId);
        void OnPlaceHit(PlaceHitReportMsg msg, string srcId);        
    }    

    public class BeamGameNet : GameNetBase, IBeamGameNet
    {
        public readonly long kBikeUpdateMs = 300;
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
                [BeamMessage.kBikeTurnMsg] = (f,t,m) => this._HandleBikeTurnMsg(f,t,m),                
                [BeamMessage.kBikeCommandMsg] = (f,t,m) => this._HandleBikeCommandMsg(f,t,m),                     
                [BeamMessage.kPlaceClaimReport] = (f,t,m) => this._HandlePlaceClaimReport(f,t,m),
                [BeamMessage.kPlaceHitReport] = (f,t,m) => this._HandlePlaceHitReport(f,t,m),                                
            };            
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
            logger.Info($"CreateGame()");
            _SyncTrivialNewGame(); // Creates/sets an ID and enqueues OnGameCreated()
        }        

        // IBeamGameNet
        public void SendBikeCreateData(IBike ib, List<Ground.Place> ownedPlaces, string destId = null)
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

        public void SendBikeTurnMsg(IBike bike, TurnDir dir, Vector2 nextPt)
        {
            logger.Debug($"BeamGameNet.SendBikeCommand() Bike: {bike.bikeId}");                    
            BikeTurnMsg msg = new BikeTurnMsg(bike.bikeId, dir, nextPt);
            _SendClientMessage(CurrentGameId(), msg.msgType.ToString(), JsonConvert.SerializeObject(msg));            
        }
        public void SendBikeCommandMsg(IBike bike, BikeCommand cmd, Vector2 nextPt)
        {
            logger.Debug($"BeamGameNet.SendBikeCommand() Bike: {bike.bikeId}");                    
            BikeCommandMsg msg = new BikeCommandMsg(bike.bikeId, cmd, nextPt);
            _SendClientMessage(CurrentGameId(), msg.msgType.ToString(), JsonConvert.SerializeObject(msg));            
        }        

        public void SendBikeUpdate(IBike bike)
        {
            // Always sent
            long nowMs = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;            
            _lastBikeUpdatesMs[bike.bikeId] = nowMs;  
            logger.Debug($"BeamGameNet.SendBikeUpdate() Bike: {bike.bikeId}");                    
            BikeUpdateMsg msg = new BikeUpdateMsg(bike);
            _SendClientMessage(CurrentGameId(), msg.msgType.ToString(), JsonConvert.SerializeObject(msg));            
        }

        public void SendBikeUpdates(List<IBike> localBikes)
        {
            // Not sent if too recent
            long nowMs = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            foreach(IBike ib in localBikes)
            {
                long prevMs;
                if ( !_lastBikeUpdatesMs.TryGetValue(ib.bikeId, out prevMs) || (nowMs - prevMs > kBikeUpdateMs))
                    SendBikeUpdate(ib);
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
 //           try {
                _MsgHandlers[msg.clientMsgType](from, to, msg);
//            } catch(KeyNotFoundException) {
//                logger.Warn($"Unknown client message type: {msg.clientMsgType}");
//            }
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

        protected void _HandleBikeCommandMsg(string from, string to, GameNetClientMessage clientMessage)
        {             
            (client as IBeamGameNetClient).OnBikeCommand(JsonConvert.DeserializeObject<BikeCommandMsg>(clientMessage.payload), from);
        }

        protected void _HandleBikeTurnMsg(string from, string to, GameNetClientMessage clientMessage)
        {             
            (client as IBeamGameNetClient).OnBikeTurn(JsonConvert.DeserializeObject<BikeTurnMsg>(clientMessage.payload), from);
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