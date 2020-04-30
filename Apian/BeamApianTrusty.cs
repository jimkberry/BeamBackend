using System.Collections.Generic;
using Newtonsoft.Json;
using GameNet;
using Apian;
using UniLog;
using UnityEngine;

namespace BeamBackend
{
    public class BeamApianTrusty : BeamApian
    {
        public struct PlaceBikeData // for things related to a bike and a place (like claim, hit)
        {
            public int x;
            public int z;
            public string bikeId;
            public override string ToString() => $"({x}, {z}, {bikeId})";

            public PlaceBikeData(int _x, int _z, string _bid) { x = _x; z=_z; bikeId=_bid; }
        }

        protected const long kDefaultVoteTimeoutMs = 300;

        protected ApianVoteMachine<PlaceBikeData> placeClaimVoteMachine;
        protected ApianVoteMachine<PlaceBikeData> placeHitVoteMachine;

        public BeamApianTrusty(IBeamGameNet _gn,  IBeamApianClient _client) : base(_gn, _client)
        {
            placeClaimVoteMachine = new ApianVoteMachine<PlaceBikeData>(kDefaultVoteTimeoutMs, kDefaultVoteTimeoutMs*2, Logger);
            placeHitVoteMachine = new ApianVoteMachine<PlaceBikeData>(kDefaultVoteTimeoutMs, kDefaultVoteTimeoutMs*2, Logger);
        }


        //
        // IBeamApian
        //

        public override void SendBikeTurnReq(IBike bike, TurnDir dir, Vector2 nextPt)
        {
            Logger.Debug($"SendBikeTurnReq) Bike: {bike.bikeId}");
            BikeTurnMsg msg = new BikeTurnMsg(ApianClock.CurrentTime, bike, dir, nextPt);
            ApianBikeTurnRequest req = new ApianBikeTurnRequest(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, req);
        }
        public override void SendBikeCommandReq(IBike bike, BikeCommand cmd, Vector2 nextPt)
        {
            Logger.Debug($"BeamGameNet.SendBikeCommand() Bike: {bike.bikeId}");
            BikeCommandMsg msg = new BikeCommandMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, cmd, nextPt);
            ApianBikeCommandRequest req = new ApianBikeCommandRequest(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, req);
        }
        public override void SendBikeCreateReq(IBike ib, List<Ground.Place> ownedPlaces, string destId = null)
        {
            Logger.Debug($"SendBikeCreateReq() - dest: {(destId??"bcast")}");
            // Broadcast this to send it to everyone
            BikeCreateDataMsg msg = new BikeCreateDataMsg(ApianClock.CurrentTime, ib, ownedPlaces);
            ApianBikeCreateRequest req = new ApianBikeCreateRequest(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(destId ?? ApianGroup.GroupId, req);
        }

        public override void SendPlaceClaimObs(IBike bike, int xIdx, int zIdx)
        {
            Logger.Debug($"SendPlaceClaimObs()");
            PlaceClaimMsg msg = new PlaceClaimMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, xIdx, zIdx);
            ApianPlaceClaimObservation obs = new ApianPlaceClaimObservation(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, obs);
        }

        public override void SendPlaceHitObs(IBike bike, int xIdx, int zIdx)
        {
            Logger.Debug($"SendPlaceHitObs()");
            PlaceHitMsg msg = new PlaceHitMsg(ApianClock.CurrentTime, bike.bikeId, bike.peerId, xIdx, zIdx);
            ApianPlaceHitObservation obs = new ApianPlaceHitObservation(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(ApianGroup.GroupId, obs);
        }

        public override void SendBikeDataQuery(string bikeId, string destId)
        {
            Logger.Verbose($"RequestBikeData()");
            BikeDataQueryMsg msg = new BikeDataQueryMsg(bikeId);
            ApianBikeDataQueryRequest req = new ApianBikeDataQueryRequest(ApianGroup?.GroupId, msg);
            BeamGameNet.SendApianMessage(destId, req);
        }

        public override void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay)
        {
            Logger.Verbose($"OnBikeTurnReq() - bike: {msg.bikeId}, source: {srcId}");
            if (ApianClock.IsIdle) // this is fugly
                return;
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                if (msg.ownerPeer != ApianGroup.LocalPeerId)
                {
                    Logger.Debug($"OnBikeTurnReq() - unknown bike: {msg.bikeId}, source: {srcId}");
                    client.OnUnknownBike(msg.bikeId, msg.ownerPeer);
                    SendBikeDataQuery(msg.bikeId, msg.ownerPeer);
                }
            } else {
                if ( bb.peerId == srcId)
                    client.OnBikeTurn(msg, msgDelay);
            }
        }

        public override void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay)
        {
            if (ApianClock.IsIdle) // this is fugly
                return;
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                if (msg.ownerPeer != ApianGroup.LocalPeerId)
                {
                    Logger.Verbose($"OnBikeCommandReq() - unknown bike: {msg.bikeId}, source: {srcId}");
                    client.OnUnknownBike(msg.bikeId, msg.ownerPeer);
                    SendBikeDataQuery(msg.bikeId, msg.ownerPeer);
                }
            } else {
                if (bb.peerId == srcId)
                    client.OnBikeCommand(msg, msgDelay);
            }
        }

        public override void OnBikeCreateReq(BikeCreateDataMsg msg, string srcId, long msgDelay)
        {
            if (ApianClock.IsIdle) // TODO: this is fugly. SHould replace with a check if the local ApianPeer obkect is ready
                return;

            Logger.Info($"OnCreateBikeReq() - got req from {srcId}"); // &&&&&&&
            if ( gameData.GetBaseBike(msg.bikeId) != null)
            {
                Logger.Verbose($"OnCreateBikeReq() Bike already exists: {msg.bikeId}.");
                return;
            }

            if (srcId == msg.peerId)
                client.OnCreateBike(msg, msgDelay);
        }

        public override void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay)
        {
            if (ApianClock.IsIdle) // this is fugly
                return;
           BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                if (msg.ownerPeer != ApianGroup.LocalPeerId)
                {
                    Logger.Verbose($"OnPlaceClaimObs() - unknown bike: {msg.bikeId}, source: {srcId}");
                    client.OnUnknownBike(msg.bikeId, msg.ownerPeer);
                    SendBikeDataQuery(msg.bikeId, msg.ownerPeer);
                }
            }

            Logger.Debug($"OnPlaceClaimObs() - Got ClaimObs from {srcId}. PeerCount: {client.GameData.Members.Count}");
            PlaceBikeData newPd = new PlaceBikeData(msg.xIdx, msg.zIdx, msg.bikeId);
            placeClaimVoteMachine.AddVote(newPd, srcId, msg.TimeStamp, client.GameData.Members.Count);
            VoteResult vr = placeClaimVoteMachine.GetResult(newPd);
            if (!vr.WasComplete && vr.Status == VoteStatus.Won && bb != null)
            {
                msg.TimeStamp = vr.TimeStamp;
                Logger.Debug($"OnPlaceClaimObs() - Calling OnPlaceClaim()");
                client.OnPlaceClaim(msg, msgDelay);
            }
        }


        public override void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay)
        {
            if (ApianClock.IsIdle) // this is fugly
                return;
            BaseBike bb = gameData.GetBaseBike(msg.bikeId);
            if (bb == null)
            {
                if (msg.ownerPeer != ApianGroup.LocalPeerId)
                {
                    Logger.Verbose($"OnPlaceHitObs() - unknown bike: {msg.bikeId}, source: {srcId}");
                    client.OnUnknownBike(msg.bikeId, msg.ownerPeer);
                    SendBikeDataQuery(msg.bikeId, msg.ownerPeer);
                    return;
                }
                // TODO: think about what happens if we get the bike data before the vote is done.
                // Should we: go ahead and count the incoming votes, but just not call OnPlaceHit() <- doing this now
                //      while the bike isn't there
                // or:  Add the ability to create a "poison" vote for this event
                // or: Never add a bike while it's involved in a vote (keep the data "pending" and check for when it's ok) <- probably bad
                // or: do nothing. <- probably bad
            }
            if (gameData.Ground.GetPlace(msg.xIdx, msg.zIdx) == null)
            {
                Logger.Warn($"OnPlaceHitObs() - unclaimed place: msg.xIdx, msg.zIdx");
                return;
            }

            Logger.Debug($"OnPlaceHitObs() - Got HitObs from {srcId}. PeerCount: {client.GameData.Members.Count}");
            PlaceBikeData newPd = new PlaceBikeData(msg.xIdx, msg.zIdx, msg.bikeId);
            placeHitVoteMachine.AddVote(newPd, srcId, msg.TimeStamp, client.GameData.Members.Count);
            VoteResult vr = placeHitVoteMachine.GetResult(newPd);
            if (!vr.WasComplete && vr.Status == VoteStatus.Won && bb != null)
            {
                msg.TimeStamp = vr.TimeStamp;
                Logger.Verbose($"OnPlaceHitObs() - Calling OnPlaceHit()");
                client.OnPlaceHit(msg, msgDelay);
            }

        }

        // IBeamApian  &&& new is above - - - - - - - - - -

        // public override void OnBikeDataQuery(BikeDataQueryMsg msg, string srcId, long msgDelay)
        // {
        //     if (ApianClock.IsIdle) // this is fugly
        //         return;
        //     IBike ib = client.GameData.GetBaseBike(msg.bikeId);
        //     Logger.Info($"OnBikeDataQuery() - bike: {msg.bikeId} {(ib==null?"is GONE! Ignoring.":"Sending")}");
        //     if (ib != null)
        //         client.PostBikeCreateData(ib, srcId);
        // }

    }

}