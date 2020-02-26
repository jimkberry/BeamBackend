
using Apian;

namespace BeamBackend
{
   
    public interface IBeamApian : IApian
    {   
        void OnCreateBikeReq(BikeCreateDataMsg msg, string srcId, long msgDelay);
        void OnBikeDataReq(BikeDataReqMsg msg, string srcId, long msgDelay);      
        void OnPlaceHitObs(PlaceHitMsg msg, string srcId, long msgDelay); // delay since the msg was sent
        void OnPlaceClaimObs(PlaceClaimMsg msg, string srcId, long msgDelay); 
        void OnBikeCommandReq(BikeCommandMsg msg, string srcId, long msgDelay); 
        void OnBikeTurnReq(BikeTurnMsg msg, string srcId, long msgDelay);

        void OnRemoteBikeUpdate(BikeUpdateMsg msg, string srcId, long msgDelay);  // TODO: where does this (or stuff like it) go?

    }

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

}