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
        public bool isActive {get; private set;} // Set when bike is fully ready. TYpically first Update()?
        public int score {get; set;}        
        public int ctrlType {get; private set;}
        public Vector2 position {get; private set;} = Vector2.zero; // always on the grid
        // NOTE: 2D position: x => east, y => north (in 3-space z is north and y is up)
        public Heading heading { get; private set;} = Heading.kNorth;
        public float speed { get; private set;} = 0;
        public BeamGameInstance gameInst = null;
        //protected Ground ground { get =>gameInst.gameData.Ground;}

        public UniLogger logger;

        public TurnDir pendingTurn { get; private set;} = TurnDir.kUnset; // set and turn will start at next grid point

        public BaseBike(BeamGameInstance gi, string _id, string _peerId, string _name, Team _team, int ctrl, Vector2 initialPos, Heading head, float _speed)
        { 
            isActive = true; // remote bikes will be set NOT active when added. Activated on first udpate
            gameInst = gi;
            bikeId = _id;
            peerId = _peerId;
            name = _name;
            team = _team;
            position = initialPos;
            speed = _speed;
            heading = head;
            ctrlType = ctrl;  
            score = kStartScore;  
            logger = UniLogger.GetLogger("BaseBike");
        }

        // Commands from outside
        public void SetActive(bool isIt) => isActive = isIt;
        //
  
        public void Loop(float secs)
        {
            //logger.Debug($"Loop(). Bike: {bikeId} Speed: {speed})");            
            _updatePosition(secs);
        }

        public void AddScore(int val) => score += val;

        public void ApplyTurn(TurnDir dir, Vector2 nextPt)
        {
            // Check to see that the reported upcoming point is what we think it is, too
            // In real life this'll get checked by Apian/consensus code to decide if the command 
            // is valid before it even makes it here. Or... we might have to "fix things up"
            if (!isActive)
                return;

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
        }

        public void ApplyCommand(BikeCommand cmd, Vector2 nextPt)
        {
            // Check to see that the reported upcoming point is what we think it is, too
            // In real life this'll get checked by Apian/consensus code to decide if the command 
            // is valid before it even makes it here. Or... we might have to "fix things up"
            if (!isActive)
                return;

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
        }        

        public void ApplyUpdate(Vector2 newPos, float newSpeed, Heading newHeading, int newScore, long lagMs)
        {
            // This happens even for an inactive bike. Sets it active, in fact.

            // STOOOPID 1st cut - just dump the data in there... no attempt at smoothing
            
            speed = newSpeed;
            heading = newHeading;

            // LagMs is how old, in ms, this information is. We need to project it to now.
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
            isActive = true;
        }

        private void _updatePosition(float secs)
        {
            if (!isActive)
                return;

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
                    gameInst.gameNet.SendPlaceClaimObs(bikeId, xIdx, zIdx);
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
                    gameInst.gameNet.SendPlaceClaimObs(bikeId, xIdx, zIdx);               
                }
            } else {
                // Hit a marker. Report it.
                gameInst.gameNet.SendPlaceHitObs(bikeId, p.xIdx, p.zIdx);
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
