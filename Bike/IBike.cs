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
        long timeAtPosition {get;}
        Vector2 prevPosition {get;}
        Heading heading { get;}
        float speed { get; }
        int score {get;}
        TurnDir pendingTurn { get;}
        void Loop(long apianTime);
        void AddScore(int val);
        Vector2 Position(long curMs);

    }

}
