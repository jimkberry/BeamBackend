
using System;
using System.Collections.Generic;
using GameNet;
using Apian;
using Newtonsoft.Json;
using UniLog;

namespace BeamBackend
{
   public interface IBeamApianClient : IApianClient
    {
        // What Apian expect to call in the app instance 
        void OnCreateBike(BikeCreateDataMsg msg, long msgDelay);
        void OnPlaceHit(PlaceHitMsg msg, long msgDelay);        
        void OnPlaceClaim(PlaceClaimMsg msg, long msgDelay); // delay since the claim was originally made
        void OnBikeCommand(BikeCommandMsg msg, long msgDelay);
        void OnBikeTurn(BikeTurnMsg msg, long msgDelay);
        void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay);   // TODO: where does this (or stuff like it) go?      
    }

    public class BeamApianAssertion : ApianAssertion
    {
        public long messageDelay;
        public BeamMessage Message {get; private set;}        
        public BeamApianAssertion(BeamMessage msg, long seq, long msgdly) : base( seq) 
        {
            Message = msg;
            messageDelay = msgdly;
        }      
    }

    public class BeamApianPeer : ApianMember
    {
        public string AppHelloData {get; private set;} // TODO - currently this is app-level - it should be apian data
        public BeamApianPeer(string _p2pId, string _appHelloData) : base(_p2pId)
        {
            AppHelloData = _appHelloData;
        }
    }
 

    public abstract class BeamApian : ApianBase, IBeamGameNetClient
    {   
        public Dictionary<string, BeamApianPeer> apianPeers;  
        
        public IBeamGameNet BeamGameNet {get; private set;}
               
        protected BeamGameInstance client;
        protected BeamGameData gameData; // TODO: should be a read-only API. Apian writing to it is not allowed      
        protected long NextAssertionSequenceNumber {get; private set;}

        public void SetGameNetInstance(IGameNet gn) {} // IGameNetClient API call not used byt Apian (happens in ctor)

        public BeamApian(IBeamGameNet _gn, IBeamApianClient _client) : base(_gn)
        {
            apianPeers = new Dictionary<string, BeamApianPeer>();
            ApianClock = new DefaultApianClock(this);              
            BeamGameNet = _gn;   
            client = _client as BeamGameInstance; 
            gameData = client.gameData;
            NextAssertionSequenceNumber = 0;    

            // Add BeamApian-level ApianMsg handlers here
            // ApMsgHandlers[BeamMessage.kBikeCreateData] = (f,t,l,m) => this.HandleBikeCreateData(f,t,l,m),                      
        }

        public override void SendApianMessage(string toChannel, ApianMessage msg)
        {
           BeamGameNet.SendApianMessage(toChannel, msg);            
        }    

        public override void OnApianMessage(string msgType, string msgJson, string fromId, string toId, long lagMs)   
        {          
            logger.Debug(msgJson);
            ApMsgHandlers[msgType](msgJson, fromId, toId, lagMs);        
        }
        public override void Update()
        {
            ApianGroup?.Update();
            ApianClock?.Update();
        }
        public void OnGameCreated(string gameP2pChannel) => client.OnGameCreated(gameP2pChannel); // Awkward. Not needed for Apian, but part of GNClient

        public void OnGameJoined(string gameId, string localP2pId)
        {
            ApianGroup = new ApianBasicGroupManager(this, gameId, localP2pId);
        }

        public string LocalPeerData() => client.LocalPeerData();  

        public void OnPeerJoined(string p2pId, string peerHelloData) 
        {
            BeamApianPeer p = new BeamApianPeer(p2pId, peerHelloData);
            p.status = ApianMember.Status.kJoining;
            apianPeers[p2pId] = p; 
        }

        public void OnPeerSync(string p2pId, long clockOffsetMs, long netLagMs)
        {
            BeamApianPeer p = apianPeers[p2pId];

            ApianClock?.OnPeerSync(p2pId, clockOffsetMs, netLagMs); // TODO: should this be in ApianBase?

            switch (p.status)
            {
            case ApianMember.Status.kJoining:
                p.status = ApianMember.Status.kActive;
                client.OnPeerJoined(p2pId, p.AppHelloData);
                break;
            case ApianMember.Status.kActive:
                break;
            }
        }
        public void OnPeerLeft(string p2pId) // => client.OnPeerLeft(p2pId)
        {
           client.OnPeerLeft(p2pId);
        }

        public override void OnMemberJoinedGroup(string peerId)
        {
            logger.Info($"OnMemberJoinedGroup(): {peerId}");
            if ( ApianGroup.LocalP2pId == ApianGroup.GroupCreatorId) // we're the group creator
            {
                if (peerId == ApianGroup.LocalP2pId)
                {
                    ApianClock.Set(0); // we joined. Set the clock
                    client.OnGameJoined(ApianGroup.GroupId, peerId);
                } else {
                    // someone else joined - broadcast the clock offset
                    if (!ApianClock.IsIdle)
                        ApianClock.SendApianClockOffset();
                }
            }
        }

        public abstract void OnCreateBikeReq(BikeCreateDataMsg msg, string srcId, long msgDelay);
        public abstract void OnBikeDataReq(BikeDataReqMsg msg, string srcId, long msgDelay);      
        public abstract void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay); // delay since the msg was sent
        public abstract void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay); 
        public abstract void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay); 
        public abstract void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay);
        public abstract void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay);  // TODO: where does this (or stuff like it) go?

        protected void SendAssertion(BeamMessage msg, long msgDelay)
        {
            BeamApianAssertion aa = new BeamApianAssertion(msg, NextAssertionSequenceNumber++, msgDelay);
            client.OnApianAssertion(aa);
        }

    }


}