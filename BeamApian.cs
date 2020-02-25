
using GameNet;

namespace BeamBackend
{
    public class ApianMessage
    {
        // These should be opaque to anything other than Apian
        public string Payload { get; private set; }
    }

    public interface IApian : IGameNetClient {}
    // The IApian interface IS an IGameNetClient
    // It's be nicer if the method names had "obs" and "req" in them to signify
    // they are observations and requests, but it really IS at this level IGameNet.

    public interface IApianClient : IGameNetClient {}
    // But the Apian client (the Business Logic/State instance) is *also* an IGamenetClient. 
    // That's kinda the point: Apian currently looks kinda like a passthru

    
    public interface IBeamApian : IApian
    {   
        void OnCreateBikeReq(BikeCreateDataMsg msg, string srcId, long msgDelay);
        void OnBikeDataReq(BikeDataReqMsg msg, string srcId, long msgDelay);      
        void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay); // delay since the msg was sent
        void OnPlaceClaimObs(PlaceClaimIdxMsg msg, string srcId, long msgDelay); 
        void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay); 
        void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay);

        void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay);  // TODO: where does this (or stuff like it) go?

    }

    public interface IBeamApianClient : IApianClient
    {
        // What Apian expect to call in the app instance 
        void OnCreateBike(BikeCreateDataMsg msg, long msgDelay);
        void OnPlaceHit(PlaceHitMsg msg, long msgDelay);        
        void OnPlaceClaim(PlaceClaimIdxMsg msg, long msgDelay); // delay since the claim was originally made
        void OnBikeCommand(BikeCommandMsg msg, long msgDelay);
        void OnBikeTurn(BikeTurnMsg msg, long msgDelay);

        void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay);   // TODO: where does this (or stuff like it) go?      
    }

}