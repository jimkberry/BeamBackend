using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace BeamBackend
{
    public interface IBike 
    {
        string bikeId {get;}   
        Player player {get;}   
        int ctrlType {get;}        
        Vector2 position {get;}   
        Heading heading { get;} 
        int score {get;}          
        void Loop(float secs);  

        // Temporary ctrl stuff
        TurnDir pendingTurn { get;}
    }

}
