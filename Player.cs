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

    public static class DemoPlayerData
    {
        private static readonly List<string> firstNames = new List<string>() {
            "Alice", "Bob", "Carol", "Don", "Evan", "Frank", "Gayle", "Herb",
            "Inez", "Jim", "Kayla", "Lara", "Mike", "Noel", "Orlando", "Paul",
            "Quentin", "Rachel", "Sam", "Terry", "Umberto", "Vera", "Will", "Xavier",
            "Yasmin", "Zack"
        };

        private static readonly List<string> lastNames = new List<string>() {
            "A.", "B.", "C.", "D.", "E.", "F.", "G.", "H.",
            "I.", "J.", "K.", "L.", "M.", "N.", "O.", "P.",
            "Q.", "R.", "S.", "T.", "U.", "V.", "W.", "X.",
            "Y.", "Z."
        };

        public static string RandomName()
        {
            return string.Format("{0} {1}",
                firstNames[(int)UnityEngine.Random.Range(0,firstNames.Count)],
                lastNames[(int)UnityEngine.Random.Range(0,lastNames.Count)] );
        }

        public static Team RandomTeam()
        {
            return Team.teamData[(int)UnityEngine.Random.Range(0,Team.teamData.Count)];
        }
        
    }
}
