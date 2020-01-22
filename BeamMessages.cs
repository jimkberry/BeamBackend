using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    public class BeamMessage
    {
//        public const string kGameCreated = "100";
//        public const string kGameJoined = "101";
//        public const string kPeerJoined = "102";
//        public const string kPeerLeft = "103";      
        public const string kBikeCreateData = "104";
        public const string kBikeDataReq = "105";    
        public const string kBikeUpdate = "106";
        public const string kBikeTurnMsg = "107";        
        public const string kBikeCommandMsg = "108";        
        public const string kPlaceClaimReport = "109";
        public const string kPlaceHitReport = "110";
         
        public string msgType;
        public BeamMessage(string t) => msgType = t;
    }


    //
    // GameNet messages
    //
    //
    public class BikeCreateDataMsg : BeamMessage
    {
        public class PlaceCreateData // Don;t need bikeId since this is part of a bike data msg
        {
            public int xIdx;
            public int zIdx;
            public float secsLeft;

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

    public class PlaceClaimReportMsg : BeamMessage
    {
        public string bikeId;
        public float xPos;
        public float zPos;
        public PlaceClaimReportMsg(string _bikeId, float  _xPos, float _zPos) : base(kPlaceClaimReport) 
        { 
            bikeId = _bikeId; 
            xPos = _xPos;
            zPos = _zPos;
        }
    }

    public class PlaceHitReportMsg : BeamMessage
    {
        public string bikeId;
        public int xIdx;
        public int zIdx;
        public PlaceHitReportMsg(string _bikeId, int _xIdx, int _zIdx) : base(kPlaceHitReport) 
        { 
            bikeId = _bikeId; 
            xIdx=_xIdx;
            zIdx=_zIdx;
        }
    }
 

}