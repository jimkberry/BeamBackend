using GameModeMgr;
namespace BeamBackend
{
    public class BeamMessage
    {
        public enum MsgType {
            kGameCreated = 100,
            kGameJoined = 101,
            kPlayerJoined = 102,  
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

    public class PlayerJoinedMsg : BeamMessage
    {
        public Player player;
        public PlayerJoinedMsg(Player _p) : base(MsgType.kPlayerJoined) => player = _p;
    }    
}