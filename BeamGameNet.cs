
using GameNet;

namespace BeamBackend
{
    public interface IBeamGameNet : IGameNet
    {

    }

    public interface IBeamGameNetClient : IGameNetClient
    {
        
    }    

    public class BeamGameNet : GameNetBase, IBeamGameNet
    {
        public class GameCreationData {}

        public BeamGameNet() : base() {}

        public override void  CreateGame<GameCreationData>(GameCreationData data)
        {
            logger.Info($"BeamGameNet.CreateGame()");
            _SyncTrivialNewGame(); // Creates/sets an ID and enqueues OnGameCreated()
        }        
      
    }
}