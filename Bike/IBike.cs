using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace BeamBackend
{
    public interface IBike 
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
        void Loop(float secs);             
        void AddScore(int val);

    }

}
