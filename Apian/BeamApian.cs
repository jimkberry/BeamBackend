
using System.Security.Cryptography;
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
        void OnGroupJoined(string groupId); // local peer has joined a group (status: Joining)
        void OnNewPlayer(NewPlayerMsg msg, long msgDelay);
        void OnPlayerLeft(string peerId);
        void OnCreateBike(BikeCreateDataMsg msg, long msgDelay);
        void OnPlaceHit(PlaceHitMsg msg, long msgDelay);
        void OnPlaceClaim(PlaceClaimMsg msg, long msgDelay); // delay since the claim was originally made
        void OnBikeCommand(BikeCommandMsg msg, long msgDelay);
        void OnBikeTurn(BikeTurnMsg msg, long msgDelay);
    }


    public class BeamApianPeer : ApianGroupMember
    {
        public BeamApianPeer(string _p2pId, string _appHelloData) : base(_p2pId, _appHelloData) { }
    }


    public abstract class BeamApian : ApianBase
    {
        protected enum SyncOrLiveCmd {
            kSync,
            kLive
        }

        protected Dictionary<string, Action<ApianCommand,string, string>> CommandHandlers;
        //protected Dictionary<string, Action<ApianCommand,string, string, SyncOrLiveCmd>> CommandHandlers;
        // Args are Command, fromId, groupChan, SyncOrLive ( set ApianClockVal before applying?)

        public Dictionary<string, BeamApianPeer> apianPeers;
        public IBeamGameNet BeamGameNet {get; private set;}
        protected BeamGameInstance client;
        protected BeamGameData gameData; // TODO: should be a read-only interface. Apian writing to it is not allowed
                                        // TODO: ALSO - this is currently only ever referenced when set. ie - it's not used. Maybe make it go away?

        protected bool LocalPeerIsActive {get => (ApianGroup != null) && (ApianGroup.LocalMember?.CurStatus == ApianGroupMember.Status.Active); }

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
                {BeamMessage.kNewPlayer, (m,f,g) => OnNewPlayerCmd(m as ApianNewPlayerCommand,f,g) },
                {BeamMessage.kBikeCommandMsg, (m,f,g) => OnBikeCommandCmd(m as ApianBikeCommandCommand,f,g) },
                {BeamMessage.kBikeTurnMsg, (m,f,g) => OnBikeTurnCmd(m as ApianBikeTurnCommand,f,g) },
                {BeamMessage.kBikeCreateData, (m,f,g) => OnBikeCreateCmd(m as ApianBikeCreateCommand, f, g) },
                {BeamMessage.kPlaceClaimMsg, (m,f,g) =>  OnPlaceClaimCmd(m as ApianPlaceClaimCommand,f,g) },
                {BeamMessage.kPlaceHitMsg, (m,f,g) => OnPlaceHitCmd(m as ApianPlaceHitCommand,f,g) },
            };

        }


        protected long FakeSyncApianTime { get; set;}
        public long CurrentApianTime()
        {
            // This is for BeamGameInstance, if the peer is active and running it will just return the current ApianClock time
            // If the peer is not active then it returns a ratchect-forward-only value that gets set as each command
            // is Applied (it's read from the BeamMessage contained in the command)
            if (LocalPeerIsActive)
                return ApianClock.CurrentTime;
            else
                return FakeSyncApianTime;

        }

        private void SetFakeSyncApianTime(long newTime)
        {
            // Expected to be called with ApianTime values from BeamMessages wrapped as ApianCoMmands
            // while the local peer is syncing and applying commands to catch up.
            // Only logic here is that it cannot go backwards
            // It's entirely possible - ad ok - for the ApianTime fields in BeamMessages not to respect the
            // ApianCommand SequenceNumber order.
            FakeSyncApianTime = Math.Max(FakeSyncApianTime, newTime);
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

        // Send/Handle ApianMessages

        public override void SendApianMessage(string toChannel, ApianMessage msg)
        {
            Logger.Verbose($"SendApianMsg() To: {toChannel} MsgType: {msg.MsgType} {((msg.MsgType==ApianMessage.GroupMessage)? "GrpMsgTYpe: "+(msg as ApianGroupMessage).GroupMsgType:"")}");
            BeamGameNet.SendApianMessage(toChannel, msg);
        }

        public override void OnApianMessage(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            ApMsgHandlers[msg.MsgType](fromId, toId, msg, lagMs);
        }

        // Called FROM GroupManager
        public override void OnGroupMemberJoined(ApianGroupMember member) // ATM Beam doesn't care
        {
            base.OnGroupMemberJoined(member);

            // Only care about local peer's group membership status
            if (member.PeerId == GameNet.LocalP2pId())
            {
                client.OnGroupJoined(ApianGroup.GroupId);
            }
        }
        public override void OnGroupMemberStatusChange(ApianGroupMember member, ApianGroupMember.Status prevStatus)
        {
            Logger.Info($"OnGroupMemberStatusChange(): {member.PeerId} went from {prevStatus} to {member.CurStatus}");

            // Beam-specific handling.
            // Joining->Active : PlayerJoined
            // Active->Removed : PlayerLeft
            // TODO: Deal with "missing" (future work)
            switch(prevStatus)
            {
            case ApianGroupMember.Status.Joining:
                if (member.CurStatus == ApianGroupMember.Status.Active)
                {
                    // This is the criterion for a player join
                    // NOTE: currently GroupMgr cant send this directly because BeamPlayerJoined is a BEAM message
                    // and the GroupMgrs don;t know about beam stuff. THIS INCLUDEs PLAYER JOIN CRITERIA!
                    // TODO: This is really awkward because:
                    //      a) We can't have the group manager isntances knowing about the client app
                    //      b) we can't create a command without a "next sequence number")

                    // Try this: it's an OBSERVATION! So it'll get routed to GroupMgr, which will send a command
                    // when appropriate.
                    SendNewPlayerObs(BeamPlayer.FromBeamJson(member.AppDataJson));
                }
                break;
            case ApianGroupMember.Status.Syncing:
                if (member.CurStatus == ApianGroupMember.Status.Active)
                {
                    SendNewPlayerObs(BeamPlayer.FromBeamJson(member.AppDataJson));
                }
                break;
            case ApianGroupMember.Status.Active:
                if (member.CurStatus == ApianGroupMember.Status.Removed)
                    (Client as IBeamApianClient).OnPlayerLeft(member.PeerId); // TODO: needs an ApianCommand
                break;
            }
        }

        public override void ApplyStashedApianCommand(ApianCommand cmd)
        {
            Logger.Info($"BeamApian.ApplyApianCommand() Group: {cmd.DestGroupId}, Applying STASHED Seq#: {cmd.SequenceNum} Type: {cmd.CliMsgType}");
            SetFakeSyncApianTime((cmd as ApianWrappedClientMessage).CliMsgTimeStamp);
            CommandHandlers[cmd.CliMsgType](cmd, ApianGroup.GroupCreatorId, GroupId);
        }

        // Incoming ApianMessage handlers
        public void OnApianClockOffsetMsg(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            Logger.Verbose($"OnApianClockOffsetMsg(): from {fromId}");
            ApianClock?.OnApianClockOffset(fromId, (msg as ApianClockOffsetMsg).ClockOffset);
        }

        public void OnApianGroupMessage(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            Logger.Debug($"OnApianGroupMessage(): {((msg as ApianGroupMessage).GroupMsgType)}");
            ApianGroup.OnApianMessage(msg, fromId, toId);
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
            ApianCommandStatus cmdStat = ApianGroup.EvaluateCommand(cmd, fromId, toId);

            switch (cmdStat)
            {
            case ApianCommandStatus.kLocalPeerNotReady:
                Logger.Warn($"BeamApian.OnApianCommand(): Local peer not a group member yet");
                break;
            case ApianCommandStatus.kShouldApply:
                Logger.Info($"BeamApian.OnApianCommand() Group: {cmd.DestGroupId}, Applying Seq#: {cmd.SequenceNum} Type: {cmd.CliMsgType}");
                CommandHandlers[cmd.CliMsgType](cmd, fromId, toId);
                break;
            case ApianCommandStatus.kStashedInQueued:
                Logger.Verbose($"BeamApian.OnApianCommand() Group: {cmd.DestGroupId}, Stashing Seq#: {cmd.SequenceNum} Type: {cmd.CliMsgType}");
                break;
            case ApianCommandStatus.kAlreadyReceived:
                Logger.Error($"BeamApian.OnApianCommand(): Command Already Received: {fromId} Group: {cmd.DestGroupId}, Seq#: {cmd.SequenceNum} Type: {cmd.CliMsgType}");
                break;
            default:
                Logger.Error($"BeamApian.OnApianCommand(): BAD COMMAND SOURCE: {fromId} Group: {cmd.DestGroupId}, Seq#: {cmd.SequenceNum} Type: {cmd.CliMsgType}");
                break;

            }
        }

        // Beam command handlers
        public void OnNewPlayerCmd(ApianNewPlayerCommand cmd, string srcId, string groupChan)
        {
            client.OnNewPlayer(cmd.newPlayerMsg, 0);
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

        protected void SendRequestOrObservation(string destCh, ApianMessage msg)
        {
            // This func is just to make sure these only get sent out if we are ACTIVE.
            // It wouldn't happen anyway, since the groupmgr would not make it into a command
            // after seeing we aren;t active - but there's a lot of message traffic between the 2
            if (ApianGroup.LocalMember?.CurStatus != ApianGroupMember.Status.Active)
            {
                Logger.Warn($"SendRequestOrObservation() - outgoing message IGNORED: We are not ACTIVE.");
                return;
            }
            BeamGameNet.SendApianMessage(destCh, msg);
        }

        public void SendNewPlayerObs(BeamPlayer newPlayer)
        {
            Logger.Debug($"SendPlaceHitObs()");
            NewPlayerMsg msg = new NewPlayerMsg( newPlayer);
            ApianNewPlayerObservation obs = new ApianNewPlayerObservation(ApianGroup?.GroupId, msg);
            SendRequestOrObservation(ApianGroup.GroupId, obs);
        }

        public void SendPlaceHitObs(IBike bike, int xIdx, int zIdx)
        {
            Logger.Debug($"SendPlaceHitObs()");
            PlaceHitMsg msg = new PlaceHitMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, xIdx, zIdx);
            ApianPlaceHitObservation obs = new ApianPlaceHitObservation(ApianGroup?.GroupId, msg);
            SendRequestOrObservation(ApianGroup.GroupId, obs);
        }

        public  void SendBikeTurnReq(IBike bike, TurnDir dir, Vector2 nextPt)
        {
            Logger.Debug($"SendBikeTurnReq) Bike: {bike.bikeId}");
            BikeTurnMsg msg = new BikeTurnMsg(ApianClock.CurrentTime, bike, dir, nextPt);
            ApianBikeTurnRequest req = new ApianBikeTurnRequest(ApianGroup?.GroupId, msg);
            SendRequestOrObservation(ApianGroup.GroupId, req);
        }
        public  void SendBikeCommandReq(IBike bike, BikeCommand cmd, Vector2 nextPt)
        {
            Logger.Debug($"BeamGameNet.SendBikeCommand() Bike: {bike.bikeId}");
            BikeCommandMsg msg = new BikeCommandMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, cmd, nextPt);
            ApianBikeCommandRequest req = new ApianBikeCommandRequest(ApianGroup?.GroupId, msg);
            SendRequestOrObservation(ApianGroup.GroupId, req);
        }
        public  void SendBikeCreateReq(IBike ib, List<Ground.Place> ownedPlaces, string destId = null)
        {
            Logger.Debug($"SendBikeCreateReq() - dest: {(destId??"bcast")}");
            // Broadcast this to send it to everyone
            BikeCreateDataMsg msg = new BikeCreateDataMsg(ApianClock.CurrentTime, ib, ownedPlaces);
            ApianBikeCreateRequest req = new ApianBikeCreateRequest(ApianGroup?.GroupId, msg);
            SendRequestOrObservation(destId ?? ApianGroup.GroupId, req);
        }

        public  void SendPlaceClaimObs(IBike bike, int xIdx, int zIdx)
        {
            Logger.Debug($"SendPlaceClaimObs()");
            PlaceClaimMsg msg = new PlaceClaimMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, xIdx, zIdx);
            ApianPlaceClaimObservation obs = new ApianPlaceClaimObservation(ApianGroup?.GroupId, msg);
            SendRequestOrObservation(ApianGroup.GroupId, obs);
        }


    }


}