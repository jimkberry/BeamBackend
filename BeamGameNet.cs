using Newtonsoft.Json;
using GameNet;

namespace BeamBackend
{
    public interface IBeamGameNet : IGameNet
    {
        void SendBikeCreateData(IBike ib, string destId = null);
        void RequestBikeData(string bikeId, string destId);
        void SendBikeUpdate(IBike ib, string destId = null);
    }

    public interface IBeamGameNetClient : IGameNetClient
    {
        void OnBikeCreateData(BikeCreateDataMsg msg, string srcId);
        void OnBikeDataReq(BikeDataReqMsg msg, string srcId);
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
        public void SendBikeCreateData(IBike ib, string destId = null)
        {
            // Info to create a bike.
            // Broadcast this to send it to everyone
            BikeCreateDataMsg msg = new BikeCreateDataMsg(ib);
            _SendClientMessage( destId ?? CurrentGameId(), msg.msgType.ToString(), JsonConvert.SerializeObject(msg));
        }
        public void RequestBikeData(string bikeId, string destId)
        {
            BikeDataReqMsg msg = new BikeDataReqMsg(bikeId);
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
                case BeamMessage.kBikeCreateData:
                    (client as IBeamGameNetClient).OnBikeCreateData(JsonConvert.DeserializeObject<BikeCreateDataMsg>(clientMessage.payload), to);
                    break;
                case BeamMessage.kBikeDataReq:
                    (client as IBeamGameNetClient).OnBikeDataReq(JsonConvert.DeserializeObject<BikeDataReqMsg>(clientMessage.payload), to);
                    break;
                case BeamMessage.kBikeUpdate:
                    (client as IBeamGameNetClient).OnBikeUpdate(JsonConvert.DeserializeObject<BikeUpdateMsg>(clientMessage.payload), to);
                    break;                                        
            }
        }
    }
}