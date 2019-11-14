using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace BeamBackend
{
    public class BaseBike : IBike
    {
        public const int kStartScore = 2000;        
        public static readonly float length = 2.0f;
        public static readonly float speed =  15.0f;   

        public string bikeId {get; private set;} 
        public int score {get; set;}        
        public Player player {get; private set;}
        public int ctrlType {get; private set;}
        public Vector2 position {get; private set;} = Vector2.zero; // always on the grid
        // NOTE: 2D position: x => east, y => north (in 3-space z is north and y is up)
        public Heading heading { get; private set;} = Heading.kNorth;
        public BeamGameInstance gameInst = null;

        //
        // Temporary stuff for refactoring
        //
        public TurnDir pendingTurn { get; private set;} = TurnDir.kUnset; // set and turn will start at next grid point

        public void TempSetPendingTurn(TurnDir d) => pendingTurn = d;

        public void TempSetHeading(Heading h) => heading = h;

        public BaseBike(BeamGameInstance gi, string ID, Player p, int ctrl, Vector2 initialPos, Heading head)
        { 
            gameInst = gi;
            bikeId = ID;
            position = initialPos;
            heading = head;
            player = p;    
            ctrlType = ctrl;  
            score = kStartScore;  
            player.bikeId = ID;
        }

        // Commands from outside
        public void PostPendingTurn(TurnDir t) => pendingTurn = t;

        //
  
        public void Loop(float secs)
        {
            //UnityEngine.Debug.Log("** BaseBike.DoUpdate()");            
            _updatePosition(secs);
        }

        private void _updatePosition(float secs)
        {
            Vector2 upcomingPoint = UpcomingGridPoint(this, Ground.gridSize);
            float timeToPoint = Vector2.Distance(position, upcomingPoint) / speed;

            Vector2 newPos = position;
            Heading newHead = heading;

            if (secs >= timeToPoint) 
            {
                secs -= timeToPoint;
                newPos =  upcomingPoint;
                newHead = GameConstants.NewHeadForTurn(heading, pendingTurn);
                pendingTurn = TurnDir.kUnset;
                DoAtGridPoint(upcomingPoint, heading);    
                heading = newHead;                    
            }

            newPos += GameConstants.UnitOffset2ForHeading(heading) * secs * speed;

            position = newPos;
        }

        protected virtual void DoAtGridPoint(Vector2 pos, Heading head)
        {
            Ground g = gameInst.gameData.Ground;
            Ground.Place p = g.GetPlace(pos);
            bool justClaimed = false;
            if (p == null)
            {
                p = g.ClaimPlace(this, pos); 
                if ( p == null)
                {
                    // Off map
                    gameInst.OnScoreEvent(this, ScoreEvent.kOffMap, null);                    
                } else {
                    justClaimed = true;
                    gameInst.OnScoreEvent(this, ScoreEvent.kClaimPlace, null); 
                }
            } else {
                // Hit a marker. Do score thing,
                gameInst.OnScoreEvent(this, p.bike.player.Team == player.Team ? ScoreEvent.kHitFriendPlace : ScoreEvent.kHitEnemyPlace, p);
            }            
            
            gameInst.frontend?.OnBikeAtPlace(bikeId, p, justClaimed); 
            
        }

        //
        // Static tools. Potentially useful publicly
        // 
        public static Vector2 NearestGridPoint(BaseBike bb, float gridSize)
        {
            float invGridSize = 1.0f / gridSize;
            return new Vector2(Mathf.Round(bb.position.x * invGridSize) * gridSize, Mathf.Round(bb.position.y * invGridSize) * gridSize);
        }

        public static Vector2 UpcomingGridPoint(BaseBike bb, float gridSize)
        {
            // it's either the current closest point (if direction to it is the same as heading)
            // or is the closest point + gridSize*unitOffsetForHeading[curHead] if closest point is behind us
            Vector2 point = NearestGridPoint(bb, gridSize);
            if (Vector2.Dot(GameConstants.UnitOffset2ForHeading(bb.heading), point - bb.position) < 0)
            {
                point += GameConstants.UnitOffset2ForHeading(bb.heading) * gridSize;
            }            
            return point;
        }    


    }
}
