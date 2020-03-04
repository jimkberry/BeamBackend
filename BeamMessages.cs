using System;
using System.Collections.Generic;
using UnityEngine;
using Apian;

namespace BeamBackend
{
    public class BeamMessage
    {     
        public const string kApianMsg = "B101"; // An Apian-defined message 
        public const string kBikeCreateData = "B104";
        public const string kBikeDataReq = "B105";    
        public const string kBikeUpdate = "B106";
        public const string kBikeTurnMsg = "B107";        
        public const string kBikeCommandMsg = "B108";        
        public const string kPlaceClaimMsg = "B109";
        public const string kPlaceHitMsg = "B110";

        public string MsgType {get; private set;}
        public BeamMessage(string t) => MsgType = t;
    }


    //
    // GameNet messages
    //
    //


    public class BeamApianMessage : BeamMessage 
    {
        public string apianMsgType;
        public string apianMsgJson;

        public BeamApianMessage() : base(kApianMsg) {}      
        public BeamApianMessage( string apMsgType, string msgJson) : base(kApianMsg)
        {
            apianMsgType = apMsgType;
            apianMsgJson = msgJson;        
        }
    }

    public class BikeCreateDataMsg : BeamMessage
    {
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

        public string bikeId; 
        public string peerId;
        public string name;
        public Team team;
        public int score;     
        public int ctrlType;
        public float xPos;
        public float yPos;
        public Heading heading;     
        public float speed;

        public List<PlaceCreateData> ownedPlaces;

        public BikeCreateDataMsg(IBike ib, List<Ground.Place> places = null) : base(kBikeCreateData) 
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
            speed = ib.speed;
            ownedPlaces = new List<PlaceCreateData>();
            if (places != null)
                foreach (Ground.Place p in places)
                    ownedPlaces.Add(new PlaceCreateData(p));
        }

        public BikeCreateDataMsg() : base(kBikeCreateData) 
        {
        }        

        public IBike ToBike(BeamGameInstance gi)
        {
            // Remote bikes always get control type: BikeFactory.RemoteCrtl
            return new BaseBike(gi, bikeId, peerId , name, team, peerId != gi.LocalPeerId ? BikeFactory.RemoteCtrl : ctrlType, new Vector2(xPos, yPos), heading, speed);
        }
    }

    public class BikeDataReqMsg : BeamMessage
    {
        public string bikeId;
        public BikeDataReqMsg(string _id) : base(kBikeDataReq) => bikeId = _id;        
    }

    public class BikeUpdateMsg : BeamMessage
    {
        public string bikeId; 
        public int score;     
        public float xPos;
        public float yPos;
        public Heading heading;   
        public float speed;  

        public BikeUpdateMsg() : base(kBikeUpdate)  {}

        public BikeUpdateMsg(IBike ib) : base(kBikeUpdate) 
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
        public TurnDir dir;
        public float nextPtX;
        public float nextPtZ;  

        public BikeTurnMsg() : base(kBikeTurnMsg)  {}

        public BikeTurnMsg(string _bikeId, TurnDir _dir, Vector2 nextGridPt) : base(kBikeTurnMsg) 
        {
            bikeId = _bikeId;
            dir = _dir;
            nextPtX = nextGridPt.x;
            nextPtZ = nextGridPt.y;
        }
    }    

    public class BikeCommandMsg : BeamMessage
    {
        // TODO: use place hashes instad of positions?
        public string bikeId;   
        public BikeCommand cmd;
        public float nextPtX;
        public float nextPtZ;  

        public BikeCommandMsg() : base(kBikeCommandMsg)  {}

        public BikeCommandMsg(string _bikeId, BikeCommand _cmd, Vector2 nextGridPt) : base(kBikeCommandMsg) 
        {
            bikeId = _bikeId;
            cmd = _cmd;
            nextPtX = nextGridPt.x;
            nextPtZ = nextGridPt.y;
        }
    }  

    public class PlaceClaimMsg : BeamMessage
    {
        public string bikeId;
        public int xIdx;
        public int zIdx;
        public PlaceClaimMsg(string _bikeId, int  _xIdx, Int32 _zIdx) : base(kPlaceClaimMsg) 
        { 
            bikeId = _bikeId; 
            xIdx = _xIdx;
            zIdx = _zIdx;
        }
    }

    public class PlaceHitMsg : BeamMessage
    {
        public string bikeId;
        public int xIdx;
        public int zIdx;
        public PlaceHitMsg(string _bikeId, int _xIdx, int _zIdx) : base(kPlaceHitMsg) 
        { 
            bikeId = _bikeId; 
            xIdx=_xIdx;
            zIdx=_zIdx;
        }
    }
 

}