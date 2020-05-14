using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    //
    // Event args
    //
    public struct PlayerJoinedArgs {
        public string gameChannel;
        public BeamPlayer player;
        public PlayerJoinedArgs(string g, BeamPlayer p) {gameChannel=g; player=p;}
    }
    public struct PLyerLeftArgs {
        public string gameChannel;
        public string p2pId;
        public PLyerLeftArgs(string g, string p) {gameChannel=g; p2pId=p;}
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


    public interface IBeamGameInstance {

        // Events
        event EventHandler<string> GroupJoinedEvt;
        event EventHandler PlayersClearedEvt;
        event EventHandler<IBike> NewBikeEvt;
        event EventHandler<BikeRemovedData> BikeRemovedEvt;
        event EventHandler BikesClearedEvt;
        event EventHandler<Ground.Place> PlaceClaimedEvt;
        event EventHandler<PlaceHitArgs> PlaceHitEvt;
        event EventHandler<string> UnknownBikeEvt;

        // Instigated by game mode code
        event EventHandler ReadyToPlayEvt;
        event EventHandler RespawnPlayerEvt;
		void RaiseReadyToPlay();
		void RaiseRespawnPlayer();

        // Access
        Ground GetGround();

        string LocalPeerId {get;}
        BeamGameData GameData {get;}

        // Requests from FE
        // TODO: This is getting sparse - is it needed?
        void PostBikeCommand(IBike bike, BikeCommand cmd);
        void PostBikeTurn(IBike bike, TurnDir dir);
        void PostBikeCreateData(IBike ib, string destId);
    }



}
