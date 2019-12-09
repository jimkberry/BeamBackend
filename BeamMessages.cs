namespace BeamBackend
{
    public class BeamMessages
    {
        public class GameCreatedMsg
        {
            public string gameId;
            public GameCreatedMsg(string _gameId) => gameId = _gameId;
        }
        
        public class GameJoinedMsg
        {
            public string gameId;
            public string localId;
            public GameJoinedMsg(string _gameId, string _localId) 
            {
                gameId = _gameId; 
                localId = _localId;
            }
        }

        public class RespawnMsg {}


    }
}