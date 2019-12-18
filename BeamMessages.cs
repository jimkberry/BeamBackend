using GameModeMgr;
using UnityEngine;

namespace BeamBackend
{
    public class BeamMessage
    {
        public enum MsgType {
            kGameCreated = 100,
            kGameJoined = 101,
            kPeerJoined = 102,  
            kPeerLeft = 103,       
            kBikeInfo = 104,
            kBikeInfoReq = 105,
            kBikeUpdate = 106,

        }            
        public MsgType msgType;
        public BeamMessage(MsgType t) => msgType = t;
    }

    //
    // Basic connection messages
    //

    public class GameCreatedMsg : BeamMessage
    {
        public string gameId;
        public GameCreatedMsg(string _gameId) : base(MsgType.kGameCreated) => gameId = _gameId;
    }
    
    public class GameJoinedMsg : BeamMessage
    {
        public string gameId;
        public string localId;
        public GameJoinedMsg(string _gameId, string _localId) :  base(MsgType.kGameJoined)
        {
            gameId = _gameId; 
            localId = _localId;
        }
    }

    public class PeerJoinedMsg : BeamMessage
    {
        public BeamPeer peer;
        public PeerJoinedMsg(BeamPeer _p) : base(MsgType.kPeerJoined) => peer = _p;
    }    

    public class PeerLeftMsg : BeamMessage
    {
        public BeamPeer peer;
        public PeerLeftMsg(BeamPeer _p) : base(MsgType.kPeerLeft) => peer = _p;
    }      

    //
    // Bike-related messages
    //
    public class BikeInfoMsg : BeamMessage
    {
        public string bikeId; 
        public string peerId;
        public string name;
        public Team team;
        public int score;     
        public int ctrlType;
        public Vector2 position;
        public Heading heading;     
        public float speed;

        public BikeInfoMsg(IBike ib) : base(MsgType.kBikeInfo) 
        {
            bikeId = ib.bikeId;
            peerId = ib.peerId;
            name = ib.name;
            team = ib.team;
            score = ib.score;
            ctrlType = ib.ctrlType;
            position = ib.position;
            heading = ib.heading;
            speed = ib.speed;
        }
    }

    public class BikeInfoReq : BeamMessage
    {
        public string bikeId;
        public BikeInfoReq(string _id) : base(MsgType.kBikeInfoReq) => bikeId = _id;        
    }

    public class BikeUpdateMsg : BeamMessage
    {
        public string bikeId; 
        public int score;     
        public Vector2 position;
        public Heading heading;   
        public float speed;  

        public BikeUpdateMsg(IBike ib) : base(MsgType.kBikeUpdate) 
        {
            bikeId = ib.bikeId;
            score = ib.score;
            position = ib.position;
            heading = ib.heading;
            speed = ib.speed;
        }
    }

}