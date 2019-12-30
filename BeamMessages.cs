
namespace BeamBackend
{
    public class BeamMessage
    {
        public const string kGameCreated = "100";
        public const string kGameJoined = "101";
        public const string kPeerJoined = "102";
        public const string kPeerLeft = "103";      
        public const string kNewBikeInfo = "104";
        public const string kBikeInfoReq = "105";
        public const string kBikeUpdate = "106";
         
        public string msgType;
        public BeamMessage(string t) => msgType = t;
    }

    //
    // Basic connection messages
    //

    public class GameCreatedMsg : BeamMessage
    {
        public string gameId;
        public GameCreatedMsg(string _gameId) : base(kGameCreated) => gameId = _gameId;
    }
    
    public class GameJoinedMsg : BeamMessage
    {
        public string gameId;
        public string localId;
        public GameJoinedMsg(string _gameId, string _localId) :  base(kGameJoined)
        {
            gameId = _gameId; 
            localId = _localId;
        }
    }

    public class PeerJoinedMsg : BeamMessage
    {
        public BeamPeer peer;
        public PeerJoinedMsg(BeamPeer _p) : base(kPeerJoined) => peer = _p;
    }    

    public class PeerLeftMsg : BeamMessage
    {
        public BeamPeer peer;
        public PeerLeftMsg(BeamPeer _p) : base(kPeerLeft) => peer = _p;
    }      

    //
    // GameNet messages
    //
    //
    public class NewBikeInfoMsg : BeamMessage
    {
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

        public NewBikeInfoMsg(IBike ib) : base(kNewBikeInfo) 
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
        }

        public NewBikeInfoMsg() : base(kNewBikeInfo) 
        {
        }        
    }

    public class BikeInfoReqMsg : BeamMessage
    {
        public string bikeId;
        public BikeInfoReqMsg(string _id) : base(kBikeInfoReq) => bikeId = _id;        
    }

    public class BikeUpdateMsg : BeamMessage
    {
        public string bikeId; 
        public int score;     
        public float xPos;
        public float yPos;
        public Heading heading;   
        public float speed;  

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

}