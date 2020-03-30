using UnityEngine;
using UniLog;


namespace BeamBackend
{
    public class BaseBike : IBike
    {
        public const int kStartScore = 2000;        
        public static readonly float length = 2.0f;
        public static readonly float defaultSpeed =  15.0f;   

        public string bikeId {get; private set;} 
        public string peerId {get; private set;}
        public string name {get; private set;}
        public Team team {get; private set;}
        public int score {get; set;}        
        public string ctrlType {get; private set;}
        public Vector2 position {get; private set;} = Vector2.zero; // always on the grid
        // NOTE: 2D position: x => east, y => north (in 3-space z is north and y is up)
        public Heading heading { get; private set;} = Heading.kNorth;
        public float speed { get; private set;} = 0;
        public BeamGameInstance gameInst = null;
        //protected Ground ground { get =>gameInst.gameData.Ground;}

        public UniLogger logger;

        public TurnDir pendingTurn { get; private set;} = TurnDir.kUnset; // set and turn will start at next grid point

        public BaseBike(BeamGameInstance gi, string _id, string _peerId, string _name, Team _team, string ctrl, Vector2 initialPos, Heading head, float _speed, TurnDir _pendingTurn=TurnDir.kUnset)
        { 
            gameInst = gi;
            bikeId = _id;
            peerId = _peerId;
            name = _name;
            team = _team;
            position = initialPos;
            speed = _speed;
            heading = head;
            pendingTurn = _pendingTurn;
            ctrlType = ctrl;  
            score = kStartScore;  
            logger = UniLogger.GetLogger("BaseBike");
        }

        // Commands from outside

  
        public void Loop(float secs)
        {
            //logger.Debug($"Loop(). Bike: {bikeId} Speed: {speed})");            
            _updatePosition(secs);
        }

        public void AddScore(int val) => score += val;

        public void ApplyTurn(TurnDir dir, Vector2 nextPt, float commandDelaySecs)
        {

            // Check to see that the reported upcoming point is what we think it is, too
            // In real life this'll get checked by Apian/consensus code to decide if the command 
            // is valid before it even makes it here. Or... we might have to "fix things up"
            float rollbackSecs = _rollbackTime(commandDelaySecs);

            Vector2 testPt = UpcomingGridPoint();
            if (!testPt.Equals(nextPt))
            {
                logger.Verbose($"ApplyTurn(): wrong upcoming point for bike: {bikeId}");
                // Fix it up...
                // Go back 1 grid space
                Vector2 p2 = position - GameConstants.UnitOffset2ForHeading(heading) * Ground.gridSize;
                testPt = UpcomingGridPoint(p2, heading);
                if (testPt.Equals(nextPt))
                {
                    // We can fix
                    Heading newHead = GameConstants.NewHeadForTurn(heading, dir);
                    Vector2 newPos = nextPt +  GameConstants.UnitOffset2ForHeading(newHead) * Vector2.Distance(nextPt, position);
                    heading = newHead;
                    logger.Verbose($"  Fixed.");                     
                } else {
                    logger.Verbose($"  Unable to fix.");                    
                }
            }
            pendingTurn = dir;
            _updatePosition(rollbackSecs);
        }

        public void ApplyCommand(BikeCommand cmd, Vector2 nextPt, float commandDelaySecs)
        {
            // Check to see that the reported upcoming point is what we think it is, too
            // In real life this'll get checked by Apian/consensus code to decide if the command 
            // is valid before it even makes it here. Or... we might have to "fix things up"
            float rollbackSecs = _rollbackTime(commandDelaySecs);

            if (!UpcomingGridPoint().Equals(nextPt))
                logger.Warn($"ApplyCommand(): wrong upcoming point for bike: {bikeId}");

            switch(cmd)
            {
            case BikeCommand.kStop:
                speed = 0;
                break;
            case BikeCommand.kGo:
                speed = defaultSpeed;
                break;
            default:
                logger.Warn($"ApplyCommand(): Unknown BikeCommand: {cmd}");
                break;
            }

            _updatePosition(rollbackSecs);
        }        

        public void ApplyUpdate(Vector2 newPos, float newSpeed, Heading newHeading, int newScore, long msgTime)
        {
            // STOOOPID 2nd cut - just dump the data in there... no attempt at smoothing
            
            speed = newSpeed;
            heading = newHeading;

            // project reported pos to now.
            long lagMs = gameInst.CurGameTime - msgTime;
            //logger.Info($"ApplyUpdate(): msgTime: {msgTime}");            
            logger.Debug($"ApplyUpdate(): lagMs: {lagMs}");

            newPos = newPos +  GameConstants.UnitOffset2ForHeading(heading) * (speed * lagMs / 1000.0f );
            score = newScore; // TODO: this might be problematic

            // Make sure the bike is on a grid line...     
            Vector2 ptPos = Ground.NearestGridPoint(newPos);   
            if (heading == Heading.kEast || heading == Heading.kWest)
            {
                newPos.y = ptPos.y;
            } else {
                newPos.x = ptPos.x;
            }
            position = newPos;
        }

        private void _updatePosition(float secs)
        {
            Vector2 upcomingPoint = UpcomingGridPoint();
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

        private float _rollbackTime(float secs)
        {
            // Propagate the bike backwards in time by "secs" or the length of time that 
            // takes it back to the next point - whichever is longer            
            // This is to try to minimize message delays. 
            // If, for instance, a bike command is received that we know happened .08 secs ago, 
            // then the code handling the command can roll the bike back, apply th ecommand, and then 
            // call bike.update(rolledBackTime) to have effectively back-applied the command.
            // it's not really safe to go backwards across a gridpoint, so that's as far as we'll go back.
            // It returns the amount of time rolled back as a positive float.
            Vector2 upcomingPoint = UpcomingGridPoint();
            float timeToNextPoint = Vector2.Distance(position, upcomingPoint) / speed;
            float timeSinceLastPoint = (Ground.gridSize / speed) - timeToNextPoint;
            secs = (secs > timeSinceLastPoint) ? timeSinceLastPoint : secs;

            position -= GameConstants.UnitOffset2ForHeading(heading) * secs * speed;

            return secs;
        }

        protected virtual void DoAtGridPoint(Vector2 pos, Heading head)
        {
            Ground g = gameInst.gameData.Ground;
            Ground.Place p = g.GetPlace(pos);
            logger.Debug($"DoAtGridPoint()");
            if (p == null)
            {
                int xIdx, zIdx;
                (xIdx, zIdx) = Ground.NearestGridIndices(pos);
                // is it on the map?
                if (g.IndicesAreOnMap(xIdx, zIdx))
                {
                    // Yes. Since it's empty send a claim report 
                    // Doesn't matter if the bike is local or not - THIS peer thinks there's a claim
                    gameInst.gameNet.SendPlaceClaimObs(this, xIdx, zIdx);
                } else {
                    // Nope. Blow it up.
                    // TODO: should going off the map be a consensus event?
                    // Current thinking: yeah. But not now.
                    // A thought: Could just skip the on-map check and call it a place claim and report it
                    //   GameNet can grant/not grant it depending on the consensus rules, and if inst
                    //   gets the claim it can just blow it up then. 

                    //gameInst.OnScoreEvent(this, ScoreEvent.kOffMap, null);     
                    // This is stupid and temporary (rather than just getting rid of the test)
                    // TODO: FIX THIS!!!  &&&&&&&
                    gameInst.gameNet.SendPlaceClaimObs(this, xIdx, zIdx);               
                }
            } else {
                // Hit a marker. Report it.
                gameInst.gameNet.SendPlaceHitObs(this, p.xIdx, p.zIdx);
            }            
        }

        //
        // Static tools. Potentially useful publicly
        // 
        public static Vector2 NearestGridPoint(Vector2 pos)
        {
            float invGridSize = 1.0f / Ground.gridSize;
            return new Vector2(Mathf.Round(pos.x * invGridSize) * Ground.gridSize, Mathf.Round(pos.y * invGridSize) * Ground.gridSize);
        }

        public bool CloseToGridPoint()
        {
            float dist = Vector2.Distance(position, NearestGridPoint(position));
            return (dist < length);
        }

        public static Vector2 UpcomingGridPoint(Vector2 pos, Heading head)
        {
            // it's either the current closest point (if direction to it is the same as heading)
            // or is the closest point + gridSize*unitOffsetForHeading[curHead] if closest point is behind us
            Vector2 point = NearestGridPoint( pos);
            if (Vector2.Dot(GameConstants.UnitOffset2ForHeading(head), point - pos) < 0)
            {
                point += GameConstants.UnitOffset2ForHeading(head) * Ground.gridSize;
            }            
            return point;
        }    

        public Vector2 UpcomingGridPoint( )
        {
            return UpcomingGridPoint(position, heading);
        }

    }
}
