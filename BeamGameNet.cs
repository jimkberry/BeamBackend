using Newtonsoft.Json;
using GameNet;

namespace BeamBackend
{
    public interface IBeamGameNet : IGameNet
    {
        void SendNewBikeInfo(IBike ib, string destId = null);
        void RequestBikeInfo(string bikeId, string destId);
        void SendBikeUpdate(IBike ib, string destId = null);
    }

    public interface IBeamGameNetClient : IGameNetClient
    {
        void OnNewBikeInfo(NewBikeInfoMsg msg, string srcId);
        void OnBikeInfoReq(BikeInfoReqMsg msg, string srcId);
        void OnBikeUpdate(BikeUpdateMsg msg, string srcId);
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

        // IBeamGameNet
        public void SendNewBikeInfo(IBike ib, string destId = null)
        {
            NewBikeInfoMsg msg = new NewBikeInfoMsg(ib);
            _SendClientMessage( destId ?? CurrentGameId(), msg.msgType.ToString(), JsonConvert.SerializeObject(msg));
        }
        public void RequestBikeInfo(string bikeId, string destId)
        {
            BikeInfoReqMsg msg = new BikeInfoReqMsg(bikeId);
            _SendClientMessage( destId, msg.msgType.ToString(), JsonConvert.SerializeObject(msg));
        }

        public void SendBikeUpdate(IBike ib, string destId = null)
        {
            BikeUpdateMsg msg = new BikeUpdateMsg(ib);
            _SendClientMessage( destId ?? CurrentGameId(), msg.msgType.ToString(), JsonConvert.SerializeObject(msg));            
        }

        protected override void _HandleClientMessage(string from, string to, GameNetClientMessage clientMessage)
        {
            // TODO: write a dispatch table
            switch (clientMessage.clientMsgType)
            {
                case BeamMessage.kNewBikeInfo:
                    (client as IBeamGameNetClient).OnNewBikeInfo(JsonConvert.DeserializeObject<NewBikeInfoMsg>(clientMessage.payload), to);
                    break;
                case BeamMessage.kBikeInfoReq:
                    (client as IBeamGameNetClient).OnBikeInfoReq(JsonConvert.DeserializeObject<BikeInfoReqMsg>(clientMessage.payload), to);
                    break;
                case BeamMessage.kBikeUpdate:
                    (client as IBeamGameNetClient).OnBikeUpdate(JsonConvert.DeserializeObject<BikeUpdateMsg>(clientMessage.payload), to);
                    break;                                        
            }
        }
    }
}