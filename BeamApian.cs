
using System.Collections.Generic;
using GameNet;
using Apian;
using UniLog;

namespace BeamBackend
{
   public interface IBeamApianClient : IApianClient
    {
        // What Apian expect to call in the app instance 
        void OnCreateBike(BikeCreateDataMsg msg, long msgDelay);
        void OnPlaceHit(PlaceHitMsg msg, long msgDelay);        
        void OnPlaceClaim(PlaceClaimMsg msg, long msgDelay); // delay since the claim was originally made
        void OnBikeCommand(BikeCommandMsg msg, long msgDelay);
        void OnBikeTurn(BikeTurnMsg msg, long msgDelay);

        void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay);   // TODO: where does this (or stuff like it) go?      
    }

    public class BeamApianAssertion : ApianAssertion
    {
        public long messageDelay;
        public BeamApianAssertion(BeamMessage msg, long seq, long msgdly) : base(msg, seq) 
        {
            messageDelay = msgdly;
        }      
    }

    public class BeamApianPeer : ApianPeer
    {
        public string AppHelloData {get; private set;} // TODO - currently this is app-level - it should be apian data
        public BeamApianPeer(string _p2pId, string _appHelloData) : base(_p2pId)
        {
            AppHelloData = _appHelloData;
        }
    }
 

    public abstract class BeamApian : ApianBase, IBeamGameNetClient
    {   
        public Dictionary<string, BeamApianPeer> apianPeers;         
        protected IBeamGameNet gameNet;
        protected BeamGameInstance client;
        protected BeamGameData gameData; // can read it - Apian writing to it is not allowed
        public UniLogger logger;        
        protected long NextAssertionSequenceNumber {get; private set;}

        public BeamApian(IBeamGameNet _gn, IBeamApianClient _client)
        {
            apianPeers = new Dictionary<string, BeamApianPeer>();
            gameNet = _gn;     
            client = _client as BeamGameInstance;
            gameData = client.gameData;
            logger = UniLogger.GetLogger("Apian");  
            NextAssertionSequenceNumber = 0;              
        }

        public void SetGameNetInstance(IGameNet iGameNet) =>  gameNet = (IBeamGameNet)iGameNet;

        public void OnGameCreated(string gameP2pChannel) => client.OnGameCreated(gameP2pChannel);

        public void OnGameJoined(string gameId, string localP2pId) => client.OnGameJoined(gameId, localP2pId);

        public string LocalPeerData() => client.LocalPeerData();  

        public void OnPeerJoined(string p2pId, string peerHelloData) 
        {
            BeamApianPeer p = new BeamApianPeer(p2pId, peerHelloData);
            p.status = ApianPeer.Status.kJoining;
            apianPeers[p2pId] = p; 
        }

        public void OnPeerSync(string p2pId, long clockOffsetMs, long netLagMs)
        {
            BeamApianPeer p = apianPeers[p2pId];
            if (p.status == ApianPeer.Status.kJoining)
            {
                p.status = ApianPeer.Status.kActive;
                client.OnPeerJoined(p2pId, p.AppHelloData);
            }
        }
        public void OnPeerLeft(string p2pId) // => client.OnPeerLeft(p2pId)
        {
           client.OnPeerLeft(p2pId);
        }

        public abstract void OnCreateBikeReq(BikeCreateDataMsg msg, string srcId, long msgDelay);
        public abstract void OnBikeDataReq(BikeDataReqMsg msg, string srcId, long msgDelay);      
        public abstract void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay); // delay since the msg was sent
        public abstract void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay); 
        public abstract void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay); 
        public abstract void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay);
        public abstract void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay);  // TODO: where does this (or stuff like it) go?

        protected void SendAssertion(BeamMessage msg, long msgDelay)
        {
            BeamApianAssertion aa = new BeamApianAssertion(msg, NextAssertionSequenceNumber++, msgDelay);
            client.OnApianAssertion(aa);
        }

    }


}