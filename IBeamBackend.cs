using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    //
    // Event args
    //
    public struct PeerJoinedGameArgs {
        public string gameChannel;
        public BeamPeer peer;
        public PeerJoinedGameArgs(string g, BeamPeer p) {gameChannel=g; peer=p;}        
    }
    public struct PeerLeftGameArgs {
        public string gameChannel;
        public string p2pId;
        public PeerLeftGameArgs(string g, string p) {gameChannel=g; p2pId=p;}        
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
        event EventHandler<PeerJoinedGameArgs> PeerJoinedGameEvt; 
        event EventHandler<PeerLeftGameArgs> PeerLeftGameEvt;
        event EventHandler PeersClearedEvt;
        event EventHandler<IBike> NewBikeEvt;   
        event EventHandler<BikeRemovedData> BikeRemovedEvt; 
        event EventHandler BikesClearedEvt;
        event EventHandler<Ground.Place> PlaceClaimedEvt;
        event EventHandler<PlaceHitArgs> PlaceHitEvt;    

        // Instigated by game mode code
        event EventHandler ReadyToPlayEvt;          
        event EventHandler RespawnPlayerEvt;           
		void RaiseReadyToPlay(); 
		void RaiseRespawnPlayer(); 

        // The following events are  owned by the Ground instance:
        // event EventHandler<Ground.Place> PlaceFreedEvt;     
        // event EventHandler PlacesClearedEvt;


        Ground GetGround();

        // Stuff From FE
        // TODO: This is getting sparse - is it needed?
        void OnSwitchModeReq(int newMode, object modeParams);
        void PostBikeCommand(IBike bike, BikeCommand cmd);
        void PostBikeTurn(IBike bike, TurnDir dir);      
        void PostBikeCreateData(IBike ib, string destId);  
    }



}
