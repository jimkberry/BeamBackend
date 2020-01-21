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
        bool isActive { get;}         
        int ctrlType {get;}        
        Vector2 position {get;}   
        Heading heading { get;} 
        float speed { get; }
        int score {get;}          
        void Loop(float secs);  
        TurnDir pendingTurn { get;}        

    }

}
