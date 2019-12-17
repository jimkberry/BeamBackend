using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    public class Player
    {
        public string PeerId { get; private set;} 
        public string PlayerId { get; private set;}    
        public string ScreenName { get; private set;}
        public Team Team {get; private set;}
        public bool IsLocal {get; private set;} // Helper

        public string bikeId {get; set;} // publicly settable

        public Player(string peerId, string playerId, string name, Team t = null, bool isLocalPlayer = false)
        { 
            PlayerId = playerId;
            PeerId = peerId;
            ScreenName = name;
            Team = (t != null) ? t : Team.teamData[(int)UnityEngine.Random.Range(0,Team.teamData.Count)];;
            IsLocal = isLocalPlayer;
        }

    }
}
