using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    public enum TeamID {
        kSharks = 0,
        kCatfish = 1,
        kWhales = 2,
        kOrcas = 3
    }

    public class Team
    {
        public static readonly List<Team> teamData = new List<Team>() {
            new Team("Sharks", "0xffff00"), // yellow
            new Team("Catfish", "0xff0000"), // red
            new Team("Whales", "0x00ffff"), // cyan
            new Team("Orcas", "0x0000ff")  // blue                       
        };

        public string Name;
        public string Color;

        public Team(string name, string color) 
        {
            Name = name;
            Color = color;
        }
    }
}