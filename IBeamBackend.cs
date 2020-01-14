using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    public struct GameJoinedArgs {
        public string gameChannel;
        public string localP2pId;
        public GameJoinedArgs(string g, string p) {gameChannel=g; localP2pId=p;}        
    }
    public struct BikeRemovedData {
        public string bikeId; 
        public bool doExplode; 
        public BikeRemovedData(string i, bool b) {bikeId=i; doExplode=b;}
    }      

    public struct PlaceHitArgs
    {
        public Ground.Place p; 
        public IBike ib; 
        public PlaceHitArgs(Ground.Place _p, IBike _ib) { p=_p; ib=_ib; }        
    }


    public interface IBeamBackend {

        event EventHandler<string> GameCreatedEvt; // game channel
        event EventHandler<GameJoinedArgs> GameJoinedEvt; // local p2pid
        event EventHandler<BeamPeer> PeerJoinedEvt;
        event EventHandler<string> PeerLeftEvt; // peer p2pId
        event EventHandler PeersClearedEvt;
        event EventHandler<IBike> NewBikeEvt;   
        event EventHandler<BikeRemovedData> BikeRemovedEvt; 
        event EventHandler BikesClearedEvt;
        event EventHandler<Ground.Place> PlaceClaimedEvt;
        event EventHandler<PlaceHitArgs> PlaceHitEvt;    

        // The following events are  owned by the Ground instance:
        // event EventHandler<Ground.Place> PlaceFreedEvt;     
        // event EventHandler PlacesClearedEvt;

        Ground GetGround();

        // Stuff From FE
        // TODO: This is getting sparse - is it needed?
        void OnSwitchModeReq(int newMode, object modeParams);
        void OnTurnReq(string bikeId, TurnDir turn);
    }



}
