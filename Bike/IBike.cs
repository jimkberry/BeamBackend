using System.Collections;
using System.Collections.Generic;
using Apian;
using UnityEngine;


namespace BeamBackend
{
    public interface IBike : IApianCoreData
    {
        string bikeId {get;}
        string peerId {get;}
        string name {get;}
        Team team { get;}
        string ctrlType {get;}
        Vector2 position {get;}
        Heading heading { get;}
        float speed { get; }
        int score {get;}
        TurnDir pendingTurn { get;}
        void Loop(float secs, long frameApianMs);
        void AddScore(int val);

    }

}
