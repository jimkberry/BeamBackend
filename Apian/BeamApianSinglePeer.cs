using System.Collections.Generic;
using Newtonsoft.Json;
using GameNet;
using Apian;
using UniLog;
using UnityEngine;

namespace BeamBackend
{
    public class BeamApianSinglePeer : BeamApian
    {
        // TODO: Either get rid of this or move it to BeamApian - or somewhere else
        public struct PlaceBikeData // for things related to a bike and a place (like claim, hit)
        {
            public int x;
            public int z;
            public string bikeId;

            public override string ToString() => $"({x}, {z}, {bikeId})";
            public PlaceBikeData(int _x, int _z, string _bid) { x = _x; z=_z; bikeId=_bid; }
        }


        public BeamApianSinglePeer(IBeamGameNet _gn,  IBeamApianClient _client) : base(_gn, _client)
        {
            ApianGroup = new SinglePeerGroupManager(this);
            ApianClock.Set(0); // Need to start it running
        }

        //
        // ApianBase
        //

        //
        // IBeamApian
        //

        // TODO: much of this can be in BeamApian (at least the requests and observations?)

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
            Logger.Debug($"OnBikeTurnReq() - bike: {msg.bikeId}");
            client.OnBikeTurn(msg, msgDelay);
        }

        public override void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay)
        {
            client.OnBikeCommand(msg, msgDelay);
        }

        public override void OnBikeCreateReq(BikeCreateDataMsg msg, string srcId, long msgDelay)
        {
            client.OnCreateBike(msg, msgDelay);
        }

        public override void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay)
        {
            client.OnPlaceClaim(msg, msgDelay);
        }


        public override void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay)
        {
            Logger.Verbose($"OnPlaceHitObs() - Calling OnPlaceHit()");
            client.OnPlaceHit(msg, msgDelay);
        }

    }

}