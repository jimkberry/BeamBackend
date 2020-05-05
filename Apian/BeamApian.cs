
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

        protected Dictionary<string, Action<ApianCommand,string, string>> CommandHandlers;
        // Args are ClientMsg, fromId, groupChan

        public Dictionary<string, BeamApianPeer> apianPeers;
        public IBeamGameNet BeamGameNet {get; private set;}
        protected BeamGameInstance client;
        protected BeamGameData gameData; // TODO: should be a read-only interface. Apian writing to it is not allowed
                                        // TODO: ALSO - this is currently only ever referenced when set. ie - it's not used. Maybe make it go away?

        public BeamApian(IBeamGameNet _gn, IBeamApianClient _client) : base(_gn, _client)
        {
            BeamGameNet = _gn;
            client = _client as BeamGameInstance;
            gameData = client.GameData;
            ApianClock = new DefaultApianClock(this);
           apianPeers = new Dictionary<string, BeamApianPeer>();

            // Add BeamApian-level ApianMsg handlers here
            // params are:  from, to, apMsg, msSinceSent
            ApMsgHandlers[ApianMessage.CliRequest] = (f,t,m,d) => this.OnApianRequest(f,t,m,d);
            ApMsgHandlers[ApianMessage.CliObservation] = (f,t,m,d) => this.OnApianObservation(f,t,m,d);
            ApMsgHandlers[ApianMessage.CliCommand] = (f,t,m,d) => this.OnApianCommand(f,t,m,d);
            ApMsgHandlers[ApianMessage.GroupMessage] = (f,t,m,d) => this.OnApianGroupMessage(f,t,m,d);
            ApMsgHandlers[ApianMessage.ApianClockOffset] = (f,t,m,d) => this.OnApianClockOffsetMsg(f,t,m,d);

            CommandHandlers = new Dictionary<string, Action<ApianCommand,string, string>>() {
                {BeamMessage.kBikeCommandMsg, (m,f,g) => OnBikeCommandCmd(m as ApianBikeCommandCommand,f,g) },
                {BeamMessage.kBikeTurnMsg, (m,f,g) => OnBikeTurnCmd(m as ApianBikeTurnCommand,f,g) },
                {BeamMessage.kBikeCreateData, (m,f,g) => OnBikeCreateCmd(m as ApianBikeCreateCommand, f, g) },
                {BeamMessage.kPlaceClaimMsg, (m,f,g) =>  OnPlaceClaimCmd(m as ApianPlaceClaimCommand,f,g) },
                {BeamMessage.kPlaceHitMsg, (m,f,g) => OnPlaceHitCmd(m as ApianPlaceHitCommand,f,g) },
            };

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
            ApianGroup.OnApianRequest(msg as ApianRequest, fromId, toId);
        }

        protected void OnApianObservation(string fromId, string toId, ApianMessage msg, long delayMs)
        {
            ApianGroup.OnApianObservation(msg as ApianObservation, fromId, toId);
        }

       protected void OnApianCommand(string fromId, string toId, ApianMessage msg, long delayMs)
        {
            ApianCommand cmd = msg as ApianCommand;
            if (ApianGroup.ValidateCommand(cmd, fromId, toId))
            {
                CommandHandlers[cmd.CliMsgType](cmd, fromId, toId);
            }
        }


        public void OnBikeCommandCmd(ApianBikeCommandCommand cmd, string srcId, string groupChan)
        {
            client.OnBikeCommand(cmd.bikeCommandMsg, 0);
        }

        public void OnBikeTurnCmd(ApianBikeTurnCommand cmd, string srcId, string groupChan)
        {
            Logger.Debug($"OnBikeTurnReq() - bike: {cmd.bikeTurnMsg.bikeId}");
            client.OnBikeTurn(cmd.bikeTurnMsg, 0);
        }

        public void OnBikeCreateCmd(ApianBikeCreateCommand cmd, string srcId, string groupChan)
        {
            client.OnCreateBike(cmd.bikeCreateDataMsg, 0);
        }

        public void OnPlaceClaimCmd(ApianPlaceClaimCommand cmd, string srcId, string groupChan)
        {
            client.OnPlaceClaim(cmd.placeClaimMsg, 0);
        }

        public void OnPlaceHitCmd(ApianPlaceHitCommand cmd, string srcId, string groupChan)
        {
            Logger.Verbose($"OnPlaceHitObs() - Calling OnPlaceHit()");
            client.OnPlaceHit(cmd.placeHitMsg, 0);
        }

        // - - - - -

        public void SendPlaceHitObs(IBike bike, int xIdx, int zIdx)
        {
            Logger.Debug($"SendPlaceHitObs()");
            PlaceHitMsg msg = new PlaceHitMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, xIdx, zIdx);
            ApianPlaceHitObservation obs = new ApianPlaceHitObservation(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, obs);
        }

        public  void SendBikeTurnReq(IBike bike, TurnDir dir, Vector2 nextPt)
        {
            Logger.Debug($"SendBikeTurnReq) Bike: {bike.bikeId}");
            BikeTurnMsg msg = new BikeTurnMsg(ApianClock.CurrentTime, bike, dir, nextPt);
            ApianBikeTurnRequest req = new ApianBikeTurnRequest(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, req);
        }
        public  void SendBikeCommandReq(IBike bike, BikeCommand cmd, Vector2 nextPt)
        {
            Logger.Debug($"BeamGameNet.SendBikeCommand() Bike: {bike.bikeId}");
            BikeCommandMsg msg = new BikeCommandMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, cmd, nextPt);
            ApianBikeCommandRequest req = new ApianBikeCommandRequest(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, req);
        }
        public  void SendBikeCreateReq(IBike ib, List<Ground.Place> ownedPlaces, string destId = null)
        {
            Logger.Debug($"SendBikeCreateReq() - dest: {(destId??"bcast")}");
            // Broadcast this to send it to everyone
            BikeCreateDataMsg msg = new BikeCreateDataMsg(ApianClock.CurrentTime, ib, ownedPlaces);
            ApianBikeCreateRequest req = new ApianBikeCreateRequest(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(destId ?? ApianGroup.GroupId, req);
        }

        public  void SendPlaceClaimObs(IBike bike, int xIdx, int zIdx)
        {
            Logger.Debug($"SendPlaceClaimObs()");
            PlaceClaimMsg msg = new PlaceClaimMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, xIdx, zIdx);
            ApianPlaceClaimObservation obs = new ApianPlaceClaimObservation(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, obs);
        }


    }


}