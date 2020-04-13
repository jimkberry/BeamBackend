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
    }


    //
    // GameNet messages
    //
    //

    // &&& This (below) needs to go away.It's going to be turned inside-out
    public class BeamApianMessage : BeamMessage 
    {
        public string apianMsgType;
        public string apianMsgJson;

        public BeamApianMessage() : base(kApianMsg, 0) {}      
        public BeamApianMessage(long ts, string apMsgType, string msgJson) : base(kApianMsg, ts)
        {
            apianMsgType = apMsgType;
            apianMsgJson = msgJson;        
        }
    }

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

        public BikeCreateDataMsg() : base(kBikeCreateData, 0) {}        

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
        public ApianBikeCreateRequest(BikeCreateDataMsg _bikeCreateMsg) : base(_bikeCreateMsg.MsgType) {bikeCreateDataMsg=_bikeCreateMsg;}
        public ApianBikeCreateRequest() : base() {}        
    }


    public class BikeDataQueryMsg : BeamMessage
    {
        public string bikeId;
        public BikeDataQueryMsg(long ts, string _id) : base(kBikeDataQuery, ts) => bikeId = _id;        
    }

    public class BikeUpdateMsg : BeamMessage
    {
        public string bikeId; 
        public int score;     
        public float xPos;
        public float yPos;
        public Heading heading;   
        public float speed;  

        public BikeUpdateMsg() : base(kBikeUpdate, 0)  {}

        public BikeUpdateMsg(long ts, IBike ib) : base(kBikeUpdate, ts) 
        {
            bikeId = ib.bikeId;
            score = ib.score;
            xPos = ib.position.x;
            yPos = ib.position.y;
            heading = ib.heading;
            speed = ib.speed;
        }
    }

    public class BikeTurnMsg : BeamMessage
    {
        // TODO: use place hashes instad of positions?
        public string bikeId;  
        public string ownerPeer;     
        public BikeState bikeState;
        public TurnDir dir;
        public float nextPtX;
        public float nextPtZ;  
        public BikeTurnMsg() : base(kBikeTurnMsg, 0)  {}

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
        public ApianBikeTurnRequest(BikeTurnMsg _bikeTurnMsg) : base(_bikeTurnMsg.MsgType) {bikeTurnMsg=_bikeTurnMsg;}
        public ApianBikeTurnRequest() : base() {}        
    }

    public class BikeCommandMsg : BeamMessage
    {
        // TODO: use place hashes instad of positions?
        public string bikeId;   
        public string ownerPeer;          
        public BikeCommand cmd;
        public float nextPtX;
        public float nextPtZ;  
        public BikeCommandMsg() : base(kBikeCommandMsg, 0)  {}
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
        public ApianBikeCommandRequest(BikeCommandMsg _bikeCommandMsg) : base(_bikeCommandMsg.MsgType) {bikeCommandMsg=_bikeCommandMsg;}
        public ApianBikeCommandRequest() : base() {}        
    }    

    public class PlaceClaimMsg : BeamMessage
    {
        public string bikeId;
        public string ownerPeer;          
        public int xIdx;
        public int zIdx;
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
        public ApianPlaceClaimObservation(PlaceClaimMsg _placeClaimMsg) : base(_placeClaimMsg.MsgType) {placeClaimMsg=_placeClaimMsg;}
        public ApianPlaceClaimObservation() : base() {}        
    }        

    public class PlaceHitMsg : BeamMessage
    {
        public string bikeId;
        public string ownerPeer;          
        public int xIdx;
        public int zIdx;
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
        public ApianPlaceHitObservation(PlaceHitMsg _placeHitMsg) : base(_placeHitMsg.MsgType) {placeHitMsg=_placeHitMsg;}
        public ApianPlaceHitObservation() : base() {}        
    }  

    static public class BeamMessageDeserializer
    {
        public static Dictionary<string, Func<string, ApianMessage>> deserializers = new  Dictionary<string, Func<string, ApianMessage>>()
        {
            {ApianMessage.kCliRequest+BeamMessage.kBikeTurnMsg, (s) => JsonConvert.DeserializeObject<ApianBikeTurnRequest>(s) },
            {ApianMessage.kCliRequest+BeamMessage.kBikeCommandMsg, (s) => JsonConvert.DeserializeObject<ApianBikeCommandRequest>(s) },
            {ApianMessage.kCliRequest+BeamMessage.kBikeCreateData, (s) => JsonConvert.DeserializeObject<ApianBikeCreateRequest>(s) },            
            {ApianMessage.kCliObservation+BeamMessage.kPlaceClaimMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceClaimObservation>(s) },            
            {ApianMessage.kCliObservation+BeamMessage.kPlaceHitMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceHitObservation>(s) },             
        };

        public static ApianMessage FromJSON(string msgId, string json)
        {
            return deserializers[msgId](json) as ApianMessage;
        }

    }


}