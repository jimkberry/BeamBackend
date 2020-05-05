using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Apian;

namespace BeamBackend
{
    public class BeamMessage : ApianClientMsg
    {
        public const string kApianMsg = "B101"; // An Apian-defined message
        public const string kBikeCreateData = "B104";
        public const string kBikeDataQuery = "B105";
        public const string kBikeUpdate = "B106";
        public const string kBikeTurnMsg = "B107";
        public const string kBikeCommandMsg = "B108";
        public const string kPlaceClaimMsg = "B109";
        public const string kPlaceHitMsg = "B110";

        // Data classes
        public class BikeState
        {
            public int score;
            public float xPos;
            public float yPos;
            public Heading heading;
            public float speed;

            public BikeState() {}

            public BikeState(IBike ib)
            {
                score = ib.score;
                xPos = ib.position.x;
                yPos = ib.position.y;
                heading = ib.heading;
                speed = ib.speed;
            }

            public BikeState(int _score, float _xPos, float _yPos, Heading _heading, float _speed)
            {
                score = _score;
                xPos = _xPos;
                yPos = _yPos;
                heading = _heading;
                speed = _speed;
            }
        }


        public class PlaceCreateData // Don't need bikeId since this is part of a bike data msg
        {
            public int xIdx;
            public int zIdx;
            public float secsLeft;

            public PlaceCreateData() {} // need a default ctor to deserialize
            public PlaceCreateData(Ground.Place p)
            {
                xIdx = p.xIdx;
                zIdx = p.zIdx;
                secsLeft = p.secsLeft;
            }
        }

        public BeamMessage(string t, long ts) : base(t,ts) {}
        public BeamMessage() : base() {}
    }


    //
    // GameNet messages
    //
    //

    public class BikeCreateDataMsg : BeamMessage
    {
        public string bikeId;
        public string peerId;
        public string name;
        public Team team;
        public int score;
        public string ctrlType;
        public float xPos;
        public float yPos;
        public Heading heading;
        public TurnDir pendingTurn;
        public float speed;

        public List<PlaceCreateData> ownedPlaces;

        public BikeCreateDataMsg(long ts, IBike ib, List<Ground.Place> places = null) : base(kBikeCreateData, ts)
        {
            bikeId = ib.bikeId;
            peerId = ib.peerId;
            name = ib.name;
            team = ib.team;
            score = ib.score;
            ctrlType = ib.ctrlType;
            xPos = ib.position.x;
            yPos = ib.position.y;
            heading = ib.heading;
            pendingTurn = ib.pendingTurn;
            speed = ib.speed;
            ownedPlaces = new List<PlaceCreateData>();
            if (places != null)
                foreach (Ground.Place p in places)
                    ownedPlaces.Add(new PlaceCreateData(p));
        }

        public BikeCreateDataMsg() : base() {}

        public IBike ToBike(BeamGameInstance gi)
        {
            // Remote bikes always get control type: BikeFactory.RemoteCrtl
            return new BaseBike(gi, bikeId, peerId , name, team, peerId != gi.LocalPeerId ? BikeFactory.RemoteCtrl : ctrlType,
                                new Vector2(xPos, yPos), heading, speed, pendingTurn);
        }
    }

    public class ApianBikeCreateRequest : ApianRequest
    {
        public BikeCreateDataMsg bikeCreateDataMsg;
        public ApianBikeCreateRequest(string gid, BikeCreateDataMsg _bikeCreateMsg) : base(gid, _bikeCreateMsg) {bikeCreateDataMsg=_bikeCreateMsg;}
        public ApianBikeCreateRequest() : base() {}
        public override ApianCommand ToCommand() => new ApianBikeCreateCommand(DestGroupId, bikeCreateDataMsg);
    }

    public class ApianBikeCreateCommand : ApianCommand
    {
        public BikeCreateDataMsg bikeCreateDataMsg;
        public ApianBikeCreateCommand(string gid, BikeCreateDataMsg _bikeCreateMsg) : base(gid, _bikeCreateMsg) {bikeCreateDataMsg=_bikeCreateMsg;}
        public ApianBikeCreateCommand() : base() {}

    }

    public class BikeDataQueryMsg : BeamMessage
    {
        public string bikeId;
        public BikeDataQueryMsg(string _id) : base(kBikeDataQuery, 0) => bikeId = _id; // TimeStanp usnused
        public BikeDataQueryMsg() : base() {}
    }

    // public class ApianBikeDataQueryRequest : ApianRequest // &&&& This message should not exist and does not fit at all with a real apian implementation
    // {
    //     // Asking a specific peer for a BikeCreateRequest for a particular bike
    //     // Anyone who already has it will ignore the response.
    //     // Should not ever happen in a REAL Apian implmentation (just a crutch for the Trusty pseudo-Apian-thingy)
    //     public BikeDataQueryMsg bikeDataQueryMsg;
    //     public ApianBikeDataQueryRequest(string gid, BikeDataQueryMsg _bikeDataQueryMsg) : base(gid, _bikeDataQueryMsg) {bikeDataQueryMsg=_bikeDataQueryMsg;}
    //     public ApianBikeDataQueryRequest() : base() {}
    // }

    public class BikeTurnMsg : BeamMessage
    {
        // TODO: use place hashes instad of positions?
        public string bikeId;
        public string ownerPeer;
        public BikeState bikeState;
        public TurnDir dir;
        public float nextPtX;
        public float nextPtZ;
        public BikeTurnMsg() : base()  {}

        public BikeTurnMsg(long ts, IBike ib, TurnDir _dir, Vector2 nextGridPt) : base(kBikeTurnMsg, ts)
        {
            bikeId = ib.bikeId;
            ownerPeer = ib.peerId;
            bikeState = new BikeState(ib);
            dir = _dir;
            nextPtX = nextGridPt.x;
            nextPtZ = nextGridPt.y;
        }
    }

    public class ApianBikeTurnRequest : ApianRequest
    {
        public BikeTurnMsg bikeTurnMsg;
        public ApianBikeTurnRequest(string gid, BikeTurnMsg _bikeTurnMsg) : base(gid, _bikeTurnMsg) {bikeTurnMsg=_bikeTurnMsg;}
        public ApianBikeTurnRequest() : base() {}

        public override ApianCommand ToCommand() => new ApianBikeTurnCommand(DestGroupId, bikeTurnMsg);
    }
    public class ApianBikeTurnCommand : ApianCommand
    {
        public BikeTurnMsg bikeTurnMsg;
        public ApianBikeTurnCommand(string gid, BikeTurnMsg _bikeTurnMsg) : base(gid, _bikeTurnMsg) {bikeTurnMsg=_bikeTurnMsg;}
        public ApianBikeTurnCommand() : base() {}
    }

    public class BikeCommandMsg : BeamMessage
    {
        // TODO: use place hashes instad of positions?
        public string bikeId;
        public string ownerPeer;
        public BikeCommand cmd;
        public float nextPtX;
        public float nextPtZ;
        public BikeCommandMsg() : base()  {}
        public BikeCommandMsg(long ts, string _bikeId, string _ownerPeer, BikeCommand _cmd, Vector2 nextGridPt) : base(kBikeCommandMsg, ts)
        {
            bikeId = _bikeId;
            ownerPeer = _ownerPeer;
            cmd = _cmd;
            nextPtX = nextGridPt.x;
            nextPtZ = nextGridPt.y;
        }
    }

    public class ApianBikeCommandRequest : ApianRequest
    {
        public BikeCommandMsg bikeCommandMsg;
        public ApianBikeCommandRequest(string gid, BikeCommandMsg _bikeCommandMsg) : base(gid, _bikeCommandMsg) {bikeCommandMsg=_bikeCommandMsg;}
        public ApianBikeCommandRequest() : base() {}
        public override ApianCommand ToCommand() => new ApianBikeCommandCommand(DestGroupId, bikeCommandMsg);
    }

    public class ApianBikeCommandCommand : ApianCommand  // Gee, no - that's not stupid-sounding at all]
    {
        public BikeCommandMsg bikeCommandMsg;
        public ApianBikeCommandCommand(string gid, BikeCommandMsg _bikeCommandMsg) : base(gid, _bikeCommandMsg) {bikeCommandMsg=_bikeCommandMsg;}
        public ApianBikeCommandCommand() : base() {}
    }

    public class PlaceClaimMsg : BeamMessage
    {
        public string bikeId;
        public string ownerPeer;
        public int xIdx;
        public int zIdx;
        public PlaceClaimMsg() : base() {}
        public PlaceClaimMsg(long ts, string _bikeId, string _ownerPeer, int  _xIdx, Int32 _zIdx) : base(kPlaceClaimMsg, ts)
        {
            bikeId = _bikeId;
            ownerPeer = _ownerPeer;
            xIdx = _xIdx;
            zIdx = _zIdx;
        }
    }

    public class ApianPlaceClaimObservation : ApianObservation
    {
        public PlaceClaimMsg placeClaimMsg;
        public ApianPlaceClaimObservation(string gid, PlaceClaimMsg _placeClaimMsg) : base(gid, _placeClaimMsg) {placeClaimMsg=_placeClaimMsg;}
        public ApianPlaceClaimObservation() : base() {}
        public override ApianCommand ToCommand() => new ApianPlaceClaimCommand(DestGroupId, placeClaimMsg);
    }

    public class ApianPlaceClaimCommand : ApianCommand
    {
        public PlaceClaimMsg placeClaimMsg;
        public ApianPlaceClaimCommand(string gid, PlaceClaimMsg _placeClaimMsg) : base(gid, _placeClaimMsg) {placeClaimMsg=_placeClaimMsg;}
        public ApianPlaceClaimCommand() : base() {}
    }

    public class PlaceHitMsg : BeamMessage
    {
        public string bikeId;
        public string ownerPeer;
        public int xIdx;
        public int zIdx;
        public PlaceHitMsg() : base() {}
        public PlaceHitMsg(long ts, string _bikeId, string _ownerPeer, int _xIdx, int _zIdx) : base(kPlaceHitMsg, ts)
        {
            bikeId = _bikeId;
            ownerPeer = _ownerPeer;
            xIdx=_xIdx;
            zIdx=_zIdx;
        }
    }

    public class ApianPlaceHitObservation : ApianObservation
    {
        public PlaceHitMsg placeHitMsg;
        public ApianPlaceHitObservation(string gid, PlaceHitMsg _placeHitMsg) : base(gid, _placeHitMsg) {placeHitMsg=_placeHitMsg;}
        public ApianPlaceHitObservation() : base() {}
        public override ApianCommand ToCommand() => new ApianPlaceHitCommand(DestGroupId, placeHitMsg);
    }
    public class ApianPlaceHitCommand : ApianCommand
    {
        public PlaceHitMsg placeHitMsg;
        public ApianPlaceHitCommand(string gid, PlaceHitMsg _placeHitMsg) : base(gid, _placeHitMsg) {placeHitMsg=_placeHitMsg;}
        public ApianPlaceHitCommand() : base() {}
    }

    static public class BeamApianMessageDeserializer
    {
        // TODO: Come up with a sane way of desrializing messages
        //(prefereably without having to include class type info in the JSON)
        public static Dictionary<string, Func<string, ApianMessage>> beamDeserializers = new  Dictionary<string, Func<string, ApianMessage>>()
        {
            {ApianMessage.CliRequest+BeamMessage.kBikeTurnMsg, (s) => JsonConvert.DeserializeObject<ApianBikeTurnRequest>(s) },
            {ApianMessage.CliRequest+BeamMessage.kBikeCommandMsg, (s) => JsonConvert.DeserializeObject<ApianBikeCommandRequest>(s) },
            {ApianMessage.CliRequest+BeamMessage.kBikeCreateData, (s) => JsonConvert.DeserializeObject<ApianBikeCreateRequest>(s) },
            {ApianMessage.CliObservation+BeamMessage.kPlaceClaimMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceClaimObservation>(s) },
            {ApianMessage.CliObservation+BeamMessage.kPlaceHitMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceHitObservation>(s) },

            {ApianMessage.CliCommand+BeamMessage.kBikeTurnMsg, (s) => JsonConvert.DeserializeObject<ApianBikeTurnCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kBikeCommandMsg, (s) => JsonConvert.DeserializeObject<ApianBikeCommandCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kBikeCreateData, (s) => JsonConvert.DeserializeObject<ApianBikeCreateCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kPlaceClaimMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceClaimCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kPlaceHitMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceHitCommand>(s) },
        };

        public static ApianMessage FromJSON(string msgType, string json)
        {
            // Deserialize once. May have to do it again
            ApianMessage aMsg = ApianMessageDeserializer.FromJSON(msgType, json);

            string subType = ApianMessageDeserializer.GetSubType(aMsg);

            return  aMsg.MsgType == ApianMessage.GroupMessage ? ApianGroupMessageDeserializer.FromJson(subType, json) :
                subType == null ? aMsg :
                     beamDeserializers[msgType+subType](json) as ApianMessage;
        }

    }


}