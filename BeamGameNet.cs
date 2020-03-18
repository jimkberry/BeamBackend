using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using GameNet;
using P2pNet;
using Apian;

namespace BeamBackend
{
    public interface IBeamGameNet : IGameNet
    {
        void SendBikeCreateData(IBike ib, List<Ground.Place> ownedPlaces, string destId = null);
        void SendBikeUpdate(IBike localBike);        
        void RequestBikeData(string bikeId, string destId);
        void SendBikeUpdates(List<IBike> localBikes);
        void SendBikeTurnReq(IBike bike, TurnDir dir, Vector2 nextPt);        
        void SendBikeCommandReq(IBike bike, BikeCommand cmd, Vector2 nextPt);
        void SendPlaceClaimObs(string bikeId, int xIdx, int zIdx);
        void SendPlaceHitObs(string bikeId, int xIdx, int zIdx);     
        void SendApianMessage(string toChannel, ApianMessage apianMsg);      
        long CurrentGroupTime();  
        string CurrentGroupId();  
    }

    public interface IBeamGameNetClient : IGameNetClient
    {       
        void OnBikeDataReq(BikeDataReqMsg msg, string srcId, long msgDelay);
        void OnCreateBikeReq(BikeCreateDataMsg msg, string srcId, long msgDelay);        
        void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay);
        void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay);        
        void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay);
        void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay);       
        void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay);  

        void OnApianMessage(string apMsgType, string apMsgJson, string fromId, string toId, long lagMs);                          
    }    

    public class BeamGameNet : GameNetBase, IBeamGameNet
    {
        public readonly long kBikeUpdateMs = 1017;
        protected Dictionary<string, long> _lastBikeUpdatesMs;

        public BeamApian ApianInst {get; protected set;}

        protected Dictionary<string, Action<string, string, long, GameNetClientMessage>> _MsgHandlers;

        public class GameCreationData {}

        public BeamGameNet() : base() 
        {
            _lastBikeUpdatesMs = new Dictionary<string, long>();
            _MsgHandlers = new  Dictionary<string, Action<string, string, long, GameNetClientMessage>>() 
            {
                [BeamMessage.kApianMsg] = (f,t,s,m) => this._HandleApianMessage(f,t,s,m),                
                [BeamMessage.kBikeCreateData] = (f,t,s,m) => this._HandleBikeCreateData(f,t,s,m),
                [BeamMessage.kBikeDataReq] = (f,t,s,m) => this._HandleBikeDataReq(f,t,s,m),   
                [BeamMessage.kBikeUpdate] = (f,t,s,m) => this._HandleBikeUpdate(f,t,s,m),
                [BeamMessage.kBikeTurnMsg] = (f,t,s,m) => this._HandleBikeTurnMsg(f,t,s,m),                
                [BeamMessage.kBikeCommandMsg] = (f,t,s,m) => this._HandleBikeCommandMsg(f,t,s,m),                     
                [BeamMessage.kPlaceClaimMsg] = (f,t,s,m) => this._HandlePlaceClaimReport(f,t,s,m),
                [BeamMessage.kPlaceHitMsg] = (f,t,s,m) => this._HandlePlaceHitReport(f,t,s,m),                                
            };            
        }

        public override void Init(IGameNetClient client)
        {
            base.Init(client); // sets GameNet.IGmeNetClient
            ApianInst = client as BeamApian;
        }

        public override void Loop()
        {
            base.Loop();
            ApianInst.Update(); 
        }

        public long CurrentGroupTime()
        {
            return ApianInst.ApianClock.CurrentTime;
        }

        public string CurrentGroupId()
        {
           return ApianInst.ApianGroup.GroupId;
           //return CurrentGameId();
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

        // IBeamGameNet
        public void SendBikeCreateData(IBike ib, List<Ground.Place> ownedPlaces, string destId = null)
        {
            logger.Verbose($"SendBikeCreateData() - dest: {(destId??"bcast")}");            
            // Info to create a bike.
            // Broadcast this to send it to everyone
            BikeCreateDataMsg msg = new BikeCreateDataMsg(CurrentGroupTime(), ib, ownedPlaces);
            _SendClientMessage( destId ?? CurrentGroupId(), msg.MsgType.ToString(), JsonConvert.SerializeObject(msg));
        }
        public void RequestBikeData(string bikeId, string destId)
        {
            logger.Verbose($"RequestBikeData()");              
            BikeDataReqMsg msg = new BikeDataReqMsg(CurrentGroupTime(), bikeId);
            _SendClientMessage( destId, msg.MsgType.ToString(), JsonConvert.SerializeObject(msg));
        }

        public void SendBikeTurnReq(IBike bike, TurnDir dir, Vector2 nextPt)
        {
            logger.Debug($"BeamGameNet.SendBikeCommand() Bike: {bike.bikeId}");                    
            BikeTurnMsg msg = new BikeTurnMsg(CurrentGroupTime(), bike.bikeId, dir, nextPt);
            _SendClientMessage(CurrentGroupId(), msg.MsgType.ToString(), JsonConvert.SerializeObject(msg));            
        }
        public void SendBikeCommandReq(IBike bike, BikeCommand cmd, Vector2 nextPt)
        {
            logger.Debug($"BeamGameNet.SendBikeCommand() Bike: {bike.bikeId}");                    
            BikeCommandMsg msg = new BikeCommandMsg(CurrentGroupTime(), bike.bikeId, cmd, nextPt);
            _SendClientMessage(CurrentGroupId(), msg.MsgType.ToString(), JsonConvert.SerializeObject(msg));            
        }        

        public void SendBikeUpdate(IBike bike)
        {
            // Always sent
            long nowMs = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;            
            _lastBikeUpdatesMs[bike.bikeId] = nowMs;  
            logger.Debug($"BeamGameNet.SendBikeUpdate() Bike: {bike.bikeId}");                    
            BikeUpdateMsg msg = new BikeUpdateMsg(CurrentGroupTime(), bike);
            _SendClientMessage(CurrentGroupId(), msg.MsgType.ToString(), JsonConvert.SerializeObject(msg));            
        }

        public void SendBikeUpdates(List<IBike> localBikes)
        {
            // Not sent if too recent
            long nowMs = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            foreach(IBike ib in localBikes)
            {
                long prevMs;
                if ((!_lastBikeUpdatesMs.TryGetValue(ib.bikeId, out prevMs) || (nowMs - prevMs > kBikeUpdateMs)) && !(ib as BaseBike).CloseToGridPoint())
                    SendBikeUpdate(ib);
            }
      
        }

        public void SendPlaceClaimObs(string bikeId, int xIdx, int zIdx)
        {
            logger.Verbose($"ReportPlaceClaim()");            
            PlaceClaimMsg msg = new PlaceClaimMsg(CurrentGroupTime(), bikeId, xIdx, zIdx);
            _SendClientMessage( CurrentGroupId(), msg.MsgType, JsonConvert.SerializeObject(msg));            
        }
        
        public void SendPlaceHitObs(string bikeId, int xIdx, int zIdx)
        {
            logger.Verbose($"ReportPlaceHit()");                
            PlaceHitMsg msg = new PlaceHitMsg(CurrentGroupTime(), bikeId, xIdx, zIdx);
            _SendClientMessage( CurrentGroupId(), msg.MsgType, JsonConvert.SerializeObject(msg));            
        }

        public void SendApianMessage(string toChannel, ApianMessage apianMsg)  
        {
            logger.Verbose($"SendApianMessage() -  type: {apianMsg.msgType}, To: {toChannel}");        
            BeamApianMessage msg = new BeamApianMessage(CurrentGroupTime(), apianMsg.msgType, JsonConvert.SerializeObject(apianMsg));     
            _SendClientMessage( toChannel ?? CurrentGroupId(), msg.MsgType, JsonConvert.SerializeObject(msg));             
        }

        //
        // Beam message handlers
        //
        protected override void _HandleClientMessage(string from, string to, long msSinceSent, GameNetClientMessage msg)
        {
// Turns out we're better off letting it throw
//           try {
                _MsgHandlers[msg.clientMsgType](from, to, msSinceSent, msg);
//            } catch(KeyNotFoundException) {
//                logger.Warn($"Unknown client message type: {msg.clientMsgType}");
//            }
        }

        protected void _HandleApianMessage(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {
            BeamApianMessage bam = JsonConvert.DeserializeObject<BeamApianMessage>(clientMessage.payload);
            logger.Verbose($"_HandleApianMessage() - Type: {bam.apianMsgType}");                
            (client as IBeamGameNetClient).OnApianMessage(bam.apianMsgType, bam.apianMsgJson, from, to, msSinceSent);
        }


        protected void _HandleBikeCreateData(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {
            logger.Verbose($"_HandleBikeCreateData()");              
            (client as IBeamGameNetClient).OnCreateBikeReq(JsonConvert.DeserializeObject<BikeCreateDataMsg>(clientMessage.payload), from, msSinceSent);
        }

        protected void _HandleBikeDataReq(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {
            logger.Verbose($"_HandleBikeDataReq()");             
            (client as IBeamGameNetClient).OnBikeDataReq(JsonConvert.DeserializeObject<BikeDataReqMsg>(clientMessage.payload), from, msSinceSent);
        }

        protected void _HandleBikeCommandMsg(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {             
            (client as IBeamGameNetClient).OnBikeCommandReq(JsonConvert.DeserializeObject<BikeCommandMsg>(clientMessage.payload), from, msSinceSent);
        }

        protected void _HandleBikeTurnMsg(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {             
            (client as IBeamGameNetClient).OnBikeTurnReq(JsonConvert.DeserializeObject<BikeTurnMsg>(clientMessage.payload), from, msSinceSent);
        }
        protected void _HandleBikeUpdate(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {             
            // NOTE: Do NOT act on loopbacked bike update messages. These are NOT state chage events, just "helpers"
            // We could filter this in Apian - and maybe should, but then all the deseriaizaltion and object creation would happen
            if (from != LocalP2pId())
                (client as IBeamGameNetClient).OnRemoteBikeUpdate(JsonConvert.DeserializeObject<BikeUpdateMsg>(clientMessage.payload), from, msSinceSent);
        }

        protected void _HandlePlaceClaimReport(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {
            logger.Verbose($"_HandlePlaceClaimReport()");             
            // TRUSTY rule: There isn;t one. No peer can be authoritative.
            // For now we'll just pass it to the game inst, which will accept the bike owner's message as authoritative.
            // NOTE: This WILL lead to inconsistency between peers unles some sort of multi-peer protocol is
            // implmented to fix it.
            // TODO: in other words we need to implment BeamApian (yeah, I said I'd initially do a custom solution
            // but that's just a waste of time.)
            (client as IBeamGameNetClient).OnPlaceClaimObs(JsonConvert.DeserializeObject<PlaceClaimMsg>(clientMessage.payload), from, msSinceSent);
        }

        protected void _HandlePlaceHitReport(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {
            logger.Verbose($"_HandlePlaceHitReport()");              
            // See above. In trustyworld, place owner is authoritative
            (client as IBeamGameNetClient).OnPlaceHitObs(JsonConvert.DeserializeObject<PlaceHitMsg>(clientMessage.payload), from, msSinceSent);
        }
    }

}