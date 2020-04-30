
using System.Text.RegularExpressions;
using System.Reflection.Emit;
using System;
using System.Collections.Generic;
using GameNet;
using Apian;
using Newtonsoft.Json;
using UnityEngine;
using UniLog;

namespace BeamBackend
{
   public interface IBeamApianClient : IApianClientApp
    {
        // What Apian expects to call in the app instance
        void OnCreateBike(BikeCreateDataMsg msg, long msgDelay);
        void OnPlaceHit(PlaceHitMsg msg, long msgDelay);
        void OnPlaceClaim(PlaceClaimMsg msg, long msgDelay); // delay since the claim was originally made
        void OnBikeCommand(BikeCommandMsg msg, long msgDelay);
        void OnBikeTurn(BikeTurnMsg msg, long msgDelay);
    }


    public class BeamApianPeer : ApianGroupMember
    {
        public string AppHelloData {get; private set;} // TODO - currently this is app-level - it should be apian data
        public BeamApianPeer(string _p2pId, string _appHelloData) : base(_p2pId)
        {
            AppHelloData = _appHelloData;
        }
    }


    public abstract class BeamApian : ApianBase
    {
        public Dictionary<string, BeamApianPeer> apianPeers;
        public IBeamGameNet BeamGameNet {get; private set;}

        protected BeamGameInstance client;
        protected BeamGameData gameData; // TODO: should be a read-only interface. Apian writing to it is not allowed
        protected long NextAssertionSequenceNumber {get; private set;}

        public BeamApian(IBeamGameNet _gn, IBeamApianClient _client) : base(_gn, _client)
        {
            BeamGameNet = _gn;
            client = _client as BeamGameInstance;
            gameData = client.GameData;

            // Add BeamApian-level ApianMsg handlers here
            // params are:  from, to, apMsg, msSinceSent
            ApMsgHandlers[ApianMessage.CliRequest] = (f,t,m,d) => this.OnApianRequest(f,t,m,d);
            ApMsgHandlers[ApianMessage.CliObservation] = (f,t,m,d) => this.OnApianObservation(f,t,m,d);
            //ApMsgHandlers[ApianMessage.kCliCommand] = (f,t,m,d) => this.OnApianCommand(f,t,m,d);
            ApMsgHandlers[ApianMessage.GroupMessage] = (f,t,m,d) => this.OnApianGroupMessage(f,t,m,d);
            ApMsgHandlers[ApianMessage.ApianClockOffset] = (f,t,m,d) => this.OnApianClockOffsetMsg(f,t,m,d);

            InitApianVars();
        }

        public void InitApianVars()
        {
            apianPeers = new Dictionary<string, BeamApianPeer>();
            ApianClock = new DefaultApianClock(this);
            NextAssertionSequenceNumber = 0;
        }


        public override void SendApianMessage(string toChannel, ApianMessage msg)
        {
            BeamGameNet.SendApianMessage(toChannel, msg);
        }

        public override void OnApianMessage(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            ApMsgHandlers[msg.MsgType](fromId, toId, msg, lagMs);
        }

        public override void Update()
        {
            ApianGroup?.Update();
            ApianClock?.Update();
        }

        protected void AddApianPeer(string p2pId, string peerHelloData)
        {
            BeamApianPeer p = new BeamApianPeer(p2pId, peerHelloData);
            apianPeers[p2pId] = p;
        }


        public void OnApianClockOffsetMsg(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            ApianClock?.OnApianClockOffset(fromId, (msg as ApianClockOffsetMsg).ClockOffset);
        }

        public void OnApianGroupMessage(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            Logger.Debug($"OnApianGroupMessage(): {((msg as ApianGroupMessage).GroupMsgType)}");
            ApianGroup.OnApianMessage(msg, fromId, toId);
        }


        public override void OnGroupMemberJoined(string memberDataJson)
        {
            BeamGroupMember member = BeamGroupMember.FromApianSerialized(memberDataJson);
            Logger.Info($"OnMemberJoinedGroup(): {member.PeerId}");
            Client.OnMemberJoined(member);
        }

        protected void OnApianRequest(string fromId, string toId, ApianMessage msg, long delayMs)
        {
            // TODO: use dispatch table instead of switch
            ApianRequest req = msg as ApianRequest;
            switch (req.CliMsgType)
            {
                case BeamMessage.kBikeTurnMsg:
                    OnBikeTurnReq((req as ApianBikeTurnRequest).bikeTurnMsg, fromId, delayMs);
                    break;
                case BeamMessage.kBikeCommandMsg:
                    OnBikeCommandReq((req as ApianBikeCommandRequest).bikeCommandMsg, fromId, delayMs);
                    break;
                case BeamMessage.kBikeCreateData:
                    OnBikeCreateReq((req as ApianBikeCreateRequest).bikeCreateDataMsg, fromId, delayMs);
                    break;
            }
        }

        protected void OnApianObservation(string fromId, string toId, ApianMessage msg, long delayMs)
        {
            ApianObservation obs = msg as ApianObservation;
            switch (obs.CliMsgType)
            {
                case BeamMessage.kPlaceClaimMsg:
                    OnPlaceClaimObs((obs as ApianPlaceClaimObservation).placeClaimMsg, fromId, delayMs);
                    break;
                case BeamMessage.kPlaceHitMsg:
                    OnPlaceHitObs((obs as ApianPlaceHitObservation).placeHitMsg, fromId, delayMs);
                    break;
            }
        }

        public abstract void SendBikeDataQuery(string bikeId, string destId);

        public abstract void SendBikeTurnReq(IBike bike, TurnDir dir, Vector2 nextPt);
        public abstract void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay);
        public abstract void SendBikeCommandReq(IBike bike, BikeCommand cmd, Vector2 nextPt);
        public abstract void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay);
        public abstract void SendBikeCreateReq(IBike ib, List<Ground.Place> ownedPlaces, string destId = null);
        public abstract void OnBikeCreateReq(BikeCreateDataMsg msg, string srcId, long msgDelay);
        public abstract void SendPlaceClaimObs(IBike bike, int xIdx, int zIdx);
        public abstract void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay);
        public abstract void SendPlaceHitObs(IBike bike, int xIdx, int zIdx);
        public abstract void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay); // delay since the msg was sent

        // &&&----------------


    }


}