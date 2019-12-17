using GameModeMgr;
namespace BeamBackend
{
    public class BeamMessage
    {
        public enum MsgType {
            kGameCreated = 100,
            kGameJoined = 101,
            kPeerJoined = 102,  
            kPeerLeft = 103,           
        }            
        public MsgType msgType;
        public BeamMessage(MsgType t) => msgType = t;
    }


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
}