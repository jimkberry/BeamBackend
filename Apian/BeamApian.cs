
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
   public interface IBeamApianClient : IApianClient
    {
        // What Apian expect to call in the app instance
        void OnCreateBike(BikeCreateDataMsg msg, long msgDelay);
        void OnPlaceHit(PlaceHitMsg msg, long msgDelay);
        void OnPlaceClaim(PlaceClaimMsg msg, long msgDelay); // delay since the claim was originally made
        void OnBikeCommand(BikeCommandMsg msg, long msgDelay);
        void OnBikeTurn(BikeTurnMsg msg, long msgDelay);
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
        protected BeamGameData gameData; // TODO: should be a read-only interface. Apian writing to it is not allowed
        protected long NextAssertionSequenceNumber {get; private set;}

        public void SetGameNetInstance(IGameNet gn) {} // IGameNetClient API call not used byt Apian (happens in ctor)

        public BeamApian(IBeamGameNet _gn, IBeamApianClient _client) : base(_gn, _client)
        {
            BeamGameNet = _gn;
            client = _client as BeamGameInstance;
            gameData = client.gameData;

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
            ApianGroup = null;
        }


        public override void SendApianMessage(string toChannel, ApianMessage msg)
        {
            BeamGameNet.SendApianMessage(toChannel, msg);
        }

        public override void OnApianMessage(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            ApMsgHandlers[msg.MsgType](fromId, toId, msg, lagMs);
        }

        public override ApianMessage DeserializeMessage(string msgType,string subType, string json)
        {
            switch (msgType)
            {
            case ApianMessage.GroupMessage:
                return ApianGroup.DeserializeMessage(subType, json);

            case ApianMessage.CliRequest:
            case ApianMessage.CliObservation:
            case ApianMessage.CliCommand:
                return BeamMessageDeserializer.FromJSON(msgType+subType, json);
            default:
                return null;
            }
        }

        public override void Update()
        {
            ApianGroup?.Update();
            ApianClock?.Update();
        }
        public void OnGameCreated(string gameP2pChannel) => client.OnGameCreated(gameP2pChannel); // Awkward. Not needed for Apian, but part of GNClient

        protected void AddApianPeer(string p2pId, string peerHelloData)
        {
            BeamApianPeer p = new BeamApianPeer(p2pId, peerHelloData);
            p.CurStatus = ApianMember.Status.Syncing;
            apianPeers[p2pId] = p;
        }

        public string LocalPeerData() => client.LocalPeerData();

        public void OnPeerJoinedGame(string p2pId, string gameId, string peerHelloData)
        {
            Logger.Info($"OnPeerJoinedGame() - {(p2pId==GameNet.LocalP2pId()?"Local":"Remote")} Peer: {p2pId}, Game: {gameId}");

            if (ApianGroup == null)
                ApianGroup = new ApianBasicGroupManager(this, gameId, GameNet.LocalP2pId());

            AddApianPeer( p2pId, peerHelloData); // doesn't add to group

            if (gameId == "localgame") // TODO: YUUUK!!! Make this be a param
            {
                Logger.Info($"OnGameJoined(): Local-only group");
                ApianGroup.StartLocalOnlyGroup();
            }
        }

        public void OnPeerLeftGame(string p2pId, string gameId)
        {
            Logger.Info($"OnPeerLeftGame() - {(p2pId==GameNet.LocalP2pId()?"Local":"Remote")} Peer: {p2pId}, Game: {gameId}");
            client.OnPeerLeftGame(p2pId, gameId);

            ApianGroup?.OnApianMessage( new BasicGroupMessages.GroupMemberLefttMsg(ApianGroup?.GroupId, p2pId), GameNet.LocalP2pId(), ApianGroup?.GroupId);

            if (p2pId == GameNet.LocalP2pId())
            {
                InitApianVars();
            }
        }

        public void OnPeerSync(string p2pId, long clockOffsetMs, long netLagMs)
        {
            BeamApianPeer p = apianPeers[p2pId];
            ApianClock?.OnPeerSync(p2pId, clockOffsetMs, netLagMs); // TODO: should this be in ApianBase?
            switch (p.CurStatus)
            {
            case ApianMember.Status.Syncing:
                p.CurStatus = ApianMember.Status.Joining;
                break;
            case ApianMember.Status.Joining:
            case ApianMember.Status.Active:
                break;
            }
        }

        public void OnApianClockOffsetMsg(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            if (fromId == ApianGroup.LocalP2pId)
            {
                Logger.Verbose($"OnApianClockOffsetMsg(). Oops. It's me. Bailing");
                return;
            }
            Logger.Info($"OnApianClockOffsetMsg() - From: {fromId}");
            BeamApianPeer p = apianPeers[fromId];
            ApianClock.OnApianClockOffset(fromId, (msg as ApianClockOffsetMsg).ClockOffset);

            if (p.CurStatus == ApianMember.Status.Joining)
            {
                p.CurStatus = ApianMember.Status.Active;
                Logger.Info($"OnApianClockOffsetMsg(): Reporting {fromId} as ready to play.");
                client.OnPeerJoinedGame(fromId, ApianGroup.GroupId, p.AppHelloData);  // Inform the client app
            }

            // Are we newly sync'ed now?
            p = apianPeers[ ApianGroup.LocalP2pId];
            Logger.Info($"OnApianClockOffsetMsg(): local peer status: {p.CurStatus}");
            if (p.CurStatus != ApianMember.Status.Active)
            {
                p.CurStatus = ApianMember.Status.Active;
                Logger.Info($"OnApianClockOffsetMsg(): Reporting local peer as ready to play.");
                client.OnPeerJoinedGame(p.P2pId, ApianGroup.GroupId, p.AppHelloData);  // Inform the client app
            }

        }

        public void OnApianGroupMessage(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            Logger.Debug($"OnApianGroupMessage(): {((msg as ApianGroupMessage).GroupMsgType)}");
            ApianGroup.OnApianMessage(msg, fromId, toId);
        }


        public override void OnMemberJoinedGroup(string peerId)
        {
            // Note: this message is FROM the group manager
            Logger.Info($"OnMemberJoinedGroup(): {peerId}");

            if (peerId == ApianGroup.LocalP2pId)
            {
                // It's us that joined.
                if ( ApianGroup.LocalP2pId == ApianGroup.GroupCreatorId) // we're the group creator
                {
                    BeamApianPeer p = apianPeers[peerId];
                    p.CurStatus = ApianMember.Status.Active;
                    // ...and we are the group creator (and so the original source for the clock)
                    ApianClock.Set(0); // we joined. Set the clock
                    Logger.Info($"OnMemberJoinedGroup(): Reporting local peer (clock owner) as ready to play.");
                    client.OnPeerJoinedGame(peerId, ApianGroup.GroupId, p.AppHelloData);  // Inform the client app
                }

            } else {
                // someone else joined - broadcast the clock offset if we have one
                if (!ApianClock.IsIdle)
                    ApianClock.SendApianClockOffset();
            }

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

        public abstract void OnBikeDataQuery(BikeDataQueryMsg msg, string srcId, long msgDelay);



        public abstract void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay);  // TODO: where does this (or stuff like it) go?


    }


}