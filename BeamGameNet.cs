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
        void SendApianMessage(string toChannel, ApianMessage appMsg);
 
        void RequestBikeData(string bikeId, string destId); // TODO: I dunno if this makes sense in an Apian game
    
        long CurrentApianTime();  
        string CurrentGroupId();  
    }

    public interface IBeamGameNetClient : IGameNetClient
    {       
        void OnBikeDataQuery(BikeDataQueryMsg msg, string srcId, long msgDelay);
        void OnBikeCreateReq(BikeCreateDataMsg msg, string srcId, long msgDelay);        
        void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay);
        void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay);        
        void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay);
        void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay);       
 
                         
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
                [ApianMessage.kCliRequest] = (f,t,s,m) => this._HandleApianRequest(f,t,s,m),                                   
                [ApianMessage.kCliObservation ] = (f,t,s,m) => this._HandleApianObservation(f,t,s,m), 
                // Need ApianMessage.kCliCommand
                [ApianMessage.kGroupMessage] = (f,t,s,m) => this._HandleApianGroupMessage(f,t,s,m), 
                [ApianMessage.kApianClockOffset ] = (f,t,s,m) => this._HandleApianClockMessage(f,t,s,m),                
                               
                // ------ &&&              
                //[BeamMessage.kApianMsg] = (f,t,s,m) => this._HandleBeamApianMessage(f,t,s,m),                 
                [BeamMessage.kBikeDataQuery] = (f,t,s,m) => this._HandleBikeDataQuery(f,t,s,m),   
                              
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
            ApianInst.Update(); // &&& Is this where we want this?
        }

        public long CurrentApianTime()
        {
            return ApianInst.ApianClock.CurrentTime;
        }

        public string CurrentGroupId()
        {
            // &&& Needed
           return ApianInst.ApianGroup.GroupId;
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

        public void SendApianMessage(string toChannel, ApianMessage appMsg)  
        {
            logger.Verbose($"SendApianMessage() - type: {appMsg.msgType}, To: {toChannel}");   
            _SendClientMessage( toChannel ?? CurrentGroupId(), appMsg.msgType,  JsonConvert.SerializeObject(appMsg));     

        }  

        // public void OldSendBeamApianMessage(string toChannel, ApianMessage apianMsg)  
        // {
        //     logger.Verbose($"SendApianMessage() -  type: {apianMsg.msgType}, To: {toChannel}");        
        //     BeamApianMessage msg = new BeamApianMessage(CurrentApianTime(), apianMsg.msgType, JsonConvert.SerializeObject(apianMsg));     
        //     _SendClientMessage( toChannel ?? CurrentGroupId(), msg.MsgType, JsonConvert.SerializeObject(msg));     
        // }        



        // IBeamGameNet

        public void RequestBikeData(string bikeId, string destId)
        {
            logger.Verbose($"RequestBikeData()");              
            BikeDataQueryMsg msg = new BikeDataQueryMsg(CurrentApianTime(), bikeId);
            _SendClientMessage( destId, msg.MsgType.ToString(), JsonConvert.SerializeObject(msg));
        }


        //
        // Beam message handlers
        //
        protected override void _HandleClientMessage(string from, string to, long msSinceSent, GameNetClientMessage msg)
        {
                // Turns out we're best-off letting it throw rather than handling exceptions
                _MsgHandlers[msg.clientMsgType](from, to, msSinceSent, msg);
        }

         protected void _HandleApianRequest(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {
            ApianRequest genReq = JsonConvert.DeserializeObject<ApianRequest>(clientMessage.payload);
            ApianMessage apMsg = BeamMessageDeserializer.FromJSON(ApianMessage.kCliRequest+genReq.cliMsgType,clientMessage.payload);            
            logger.Verbose($"_HandleApianRequest() Type: {apMsg.msgType}, src: {(from==LocalP2pId()?"Local":from)}");                
            (client as BeamApian).OnApianMessage(from, to, apMsg, msSinceSent);
        }

         protected void _HandleApianObservation(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {
            // TODO: Can we get rid of this double deserializetion. Also, we probably ought to be asking the Apian instance
            // too do the decoding isntead of calling NewtonSoft here.
            ApianObservation genObs = JsonConvert.DeserializeObject<ApianObservation>(clientMessage.payload);
            ApianMessage apMsg = BeamMessageDeserializer.FromJSON(ApianMessage.kCliObservation+genObs.cliMsgType,clientMessage.payload);            
            logger.Verbose($"_HandleApianObservation() Type: {apMsg.msgType}, src: {(from==LocalP2pId()?"Local":from)}");                
            (client as BeamApian).OnApianMessage(from, to, apMsg, msSinceSent);
        }


        protected void _HandleApianGroupMessage(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {
            // Unlike BeamMessages (which are application specific, as is this module) - we don't know here
            // exactly what kind of ApinGroupMessage we are fielding, so well have to pass it in serialized form to the Apian
            // instance (which DOES know) to decide.
            // TODO: Hmm. We should be able to just ask the apian instance to decode it for us... mostly just because passing around the
            // JSON text is ugly.
            ApianGroupMessage genGrpMsg = JsonConvert.DeserializeObject<ApianGroupMessage>(clientMessage.payload); 
            ApianMessage apMsg = (client as BeamApian).DeserializeMessage(ApianMessage.kGroupMessage,genGrpMsg.groupMsgType,clientMessage.payload);          
            logger.Verbose($"_HandleApianGroupMessage() Type: {genGrpMsg.msgType}, Subtype: {genGrpMsg.groupMsgType} src: {(from==LocalP2pId()?"Local":from)}");                
            (client as BeamApian).OnApianMessage(from, to, apMsg, msSinceSent);
        }

        protected void _HandleApianClockMessage(string from, string to, long msSinceSent, GameNetClientMessage clientMessage)
        {
            ApianClockOffsetMsg apMsg =  JsonConvert.DeserializeObject<ApianClockOffsetMsg>(clientMessage.payload);           
            logger.Verbose($"_HandleApianClockMessage() Type: {apMsg.msgType}, src: {(from==LocalP2pId()?"Local":from)}");                
            (client as BeamApian).OnApianMessage(from, to, apMsg, msSinceSent);
        }      

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