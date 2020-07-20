using System.Runtime.ConstrainedExecution;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UniLog;
using Apian;


namespace BeamBackend
{
    public class BaseBike : IBike, IApianCoreData
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
        public Vector2 basePosition {get; private set;} = Vector2.zero; // always on the grid
        public long timeAtPosition {get; private set;}

        // NOTE: 2D position: x => east, y => north (in 3-space z is north and y is up)
        public Heading heading { get; private set;} = Heading.kNorth;
        public float speed { get; private set;} = 0;
        public BeamCoreState gameData = null;

        public UniLogger logger;

        public TurnDir pendingTurn { get; private set;} = TurnDir.kUnset; // set and turn will start at next grid point


        public BaseBike( BeamCoreState gData, string _id, string _peerId, string _name, Team _team, string ctrl, long initialTime, Vector2 initialPos, Heading head)
        {
            gameData = gData;
            bikeId = _id;
            peerId = _peerId;
            name = _name;
            team = _team;
            basePosition = initialPos;
            timeAtPosition = initialTime;
            heading = head;
            ctrlType = ctrl;
            score = kStartScore;
            logger = UniLogger.GetLogger("BaseBike");
        }


        public class SerialArgs
        {
            public Dictionary<string,int> peerIdxDict;
            public long curApianTime;
            public long cpTimeStamp;
            public SerialArgs(Dictionary<string,int> pid, long at, long ts) {peerIdxDict=pid; curApianTime=at; cpTimeStamp=ts;}
        };

        private long _RoundToNearest(long interval, long inVal)
        {
            return ((inVal+interval/2) / interval) * interval;
        }

        public string ApianSerialized(object args)
        {
            SerialArgs sArgs = args as SerialArgs;

            // args.peerIdxDict is a dictionary to map peerIds to array indices in the Json for the peers
            // It makes this Json a lot smaller

            // We can deal (mostly) with time differences from one machine to another by
            // replacing the bike's position with the position of the last gridpoint crossed
            // and the apianTime when it was there (assuming current speed.) Or it's
            //  actual position and 0 if the bike's not moving.


            // // WHile I'm debugging. These are in grid-index space (tomake it easier to compare withthe places list)
            // float indexX =   (position.x - Ground.minX) / Ground.gridSize;
            // float indexZ =   (position.y - Ground.minZ) / Ground.gridSize;

            return  JsonConvert.SerializeObject(new object[]{
                    bikeId,
                    sArgs.peerIdxDict[peerId],
                    name,
                    team.TeamID,
                    ctrlType,
                    (long)(basePosition.x * 1000f), // integer mm
                    (long)(basePosition.y * 1000f),
                    timeAtPosition, // Do I need to round this? Shouldn't have to, but: _RoundToNearest(100, timeAtPoint);
                    heading,
                    speed,
                    score,
                    pendingTurn
                 });
        }

        public static BaseBike FromApianJson(string jsonData, BeamCoreState gData, List<string> peerIdList, long timeStamp)
        {
            object[] data = JsonConvert.DeserializeObject<object[]>(jsonData);

            Vector2 lastGridPos = new Vector2((long)data[5]*.001f, (long)data[6]*.001f);
            long lastGridTime = (long)data[7];
            Heading head = (Heading)(long)data[8];

            float speed = (float)(double)data[9];
            int score = (int)(long)data[10];
            TurnDir pendingTurn = (TurnDir)(long)data[11];

            float secsSinceGrid = (timeStamp - lastGridTime ) * .001f;

            gData.Logger.Info($"BaseBike FromApianJson() - Id: {(string)data[0]} curTime: {timeStamp} timeAtPos: {lastGridTime} lastGridPos: {lastGridPos.ToString()}");

            BaseBike bb = new BaseBike(
                gData,
                (string)data[0], // bikeId
                peerIdList[(int)(long)data[1]], // peerId
                (string)data[2], // _name
                Team.teamData[(int)(long)data[3]], // Team
                (string)data[4],  // ctrl,
                lastGridTime, // timeAtPosition
                lastGridPos, // current pos
                head); //  head)

            bb.speed = speed;
            bb.score = score;
            bb.pendingTurn = pendingTurn;

            return bb;
        }


        // Commands from outside

        // public void Loop(float secs, long frameTimeMs)
        // {
        //     //logger.Debug($"Loop(). Bike: {bikeId} Speed: {speed})");
        //     _updatePosition(secs, frameTimeMs);
        // }

        public Vector2 Position(long curMs)
        {
            float deltaSecs = (curMs - timeAtPosition) * .001f;
            return basePosition +  GameConstants.UnitOffset2ForHeading(heading) * (deltaSecs * speed);
        }

        public void Loop(long apianTime)
        {
            _checkPosition(apianTime);
        }

        public void AddScore(int val) => score += val;

        public void ApplyTurn(TurnDir dir, Heading entryHeading, Vector2 nextPt,  long cmdTime, BeamMessage.BikeState reportedState)
        {
            // TODO: &&&& reported state really should not be there.

            Vector2 testPt = UpcomingGridPoint(basePosition); // use the last logged position
            if (!testPt.Equals(nextPt))
            {
                logger.Warn($"ApplyTurn(): {(nextPt.ToString())} is the wrong upcoming point for bike: {bikeId}");
                logger.Warn($"We think it should be {(testPt.ToString())}");
                logger.Warn($"Reported State:\n{JsonConvert.SerializeObject(reportedState)}");
                logger.Warn($"Actual State:\n{JsonConvert.SerializeObject(new BeamMessage.BikeState(this)) }");

            }
            pendingTurn = dir;
        }

        public void ApplyCommand(BikeCommand cmd, Vector2 nextPt, long cmdTime)
        {
            if (!UpcomingGridPoint(basePosition).Equals(nextPt))
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

        private void _checkPosition(long apianTime)
        {
            if (timeAtPosition == 0) // TODO: get rid of this one the changeover is complete
                logger.Error($"_checkPosition() Bike: {bikeId} TimeAtPosition UNITITIALIZED!");

            float secs = (apianTime - timeAtPosition) * .001f;

            if (secs <= 0 || speed == 0) // nothing can have happened
                return;

            Vector2 upcomingPoint = UpcomingGridPoint(basePosition); // using the last logged position
            float timeToPoint = Vector2.Distance(basePosition, upcomingPoint) / speed;

            Vector2 newPos = basePosition;
            Heading newHead = heading;

            // Note that this assumes that secs < timeToCrossAGrid
            // TODO: should it handle longer times?
            if (secs >= timeToPoint)
            {
                //logger.Verbose($"_checkPosition() Bike: {bikeId} MsToPoint: {(long)(timeToPoint*1000)}");
                secs -= timeToPoint;
                newPos =  upcomingPoint;
                newHead = GameConstants.NewHeadForTurn(heading, pendingTurn);
                DoAtGridPoint(upcomingPoint, heading, newHead, apianTime);

                // pre-entively update the next grid position
                // There will be an observation reported arriving withte same data
                // but we're better off assuming it'll match and fixing it later than
                // waiting for it
                _updatePosition(newPos, newHead, apianTime);
            }
        }

        private void _updatePosition(Vector2 pos, Heading head,  long apianTime)
        {
            pendingTurn = TurnDir.kUnset;
            heading = head;
            basePosition = pos;
            timeAtPosition = apianTime;
            logger.Verbose($"_updatePosition() Bike: {bikeId}, Pos: {pos.ToString()} Head: {heading.ToString()}");
        }

        // &&&&&&&&&&&&& remove
        // private void _updatePosition(float secs, long frameApianTime)
        // {
        //     if (secs == 0 || speed == 0)
        //         return;

        //     Vector2 upcomingPoint = UpcomingGridPoint();
        //     float timeToPoint = Vector2.Distance(position, upcomingPoint) / speed;

        //     Vector2 newPos = position;
        //     Heading newHead = heading;

        //     if (secs >= timeToPoint)
        //     {
        //         logger.Debug($"_updatePosition() Bike: {bikeId} MsToPoint: {(long)(timeToPoint*1000)}");
        //         secs -= timeToPoint;
        //         newPos =  upcomingPoint;
        //         newHead = GameConstants.NewHeadForTurn(heading, pendingTurn);
        //         pendingTurn = TurnDir.kUnset;
        //         DoAtGridPoint(upcomingPoint, heading, newHead, frameApianTime);// + (long)(timeToPoint*1000)); // Using the offset makes odd things happen on the server
        //         heading = newHead;
        //     }

        //     newPos += GameConstants.UnitOffset2ForHeading(heading) * secs * speed;

        //     position = newPos;
        // }

        // private float _rollbackTime(float secs)
        // {
        //     // Propagate the bike backwards in time by "secs" or almost the length of time that
        //     // takes it backwards to the previous point - whichever is shorter
        //     // This is to try to minimize message delays.
        //     // If, for instance, a bike command is received that we know happened .08 secs ago,
        //     // then the code handling the command can roll the bike back, apply the ecommand, and then
        //     // call bike.update(rolledBackTime) to have effectively back-applied the command.
        //     // it's not really safe to go backwards across a gridpoint, so that's as far as we'll go back.
        //     // It returns the amount of time rolled back as a positive float.
        //     if (speed == 0 || secs <= 0)
        //         return 0;
        //     Vector2 upcomingPoint = UpcomingGridPoint();
        //     float timeToNextPoint = Vector2.Distance(position, upcomingPoint) / speed;
        //     float timeSinceLastPoint = Mathf.Max(0,((Ground.gridSize * .8f) / speed) - timeToNextPoint); // Note QUITE all the way back
        //     secs = Mathf.Min(secs, timeSinceLastPoint);
        //     position -= GameConstants.UnitOffset2ForHeading(heading) * secs * speed;
        //     return secs;
        // }

        // private float _rollbackTime(float secs)
        // {
        //     // Propagate the bike backwards in time by "secs" to apply a command that should have happened in the past
        //     // (See above for a version that tries to avoid crossing gridpoints - this one does not anymore - the cure
        //     //  was worse than the disease)
        //     position -= GameConstants.UnitOffset2ForHeading(heading) * secs * speed;
        //     return secs;
        // }

        protected virtual void DoAtGridPoint(Vector2 pos, Heading entryHead, Heading exitHead, long apianTime)
        {
            BeamPlace p = gameData.GetPlace(pos);
            logger.Verbose($"DoAtGridPoint({pos.ToString()}) Bike: {bikeId} Time: {apianTime}");
            if (p == null)
            {
                int xIdx, zIdx;
                (xIdx, zIdx) = Ground.NearestGridIndices(pos);
                // is it on the map?
                if (gameData.Ground.IndicesAreOnMap(xIdx, zIdx))
                {
                    // Yes. Since it's empty send a claim report
                    // Doesn't matter if the bike is local or not - THIS peer thinks there's a claim
                    gameData.ReportPlaceClaimed(this, xIdx, zIdx, entryHead, exitHead);
                } else {
                    // Nope. Blow it up.
                    // TODO: should going off the map be a consensus event?
                    // Current thinking: yeah. But not now.
                    // A thought: Could just skip the on-map check and call it a place claim and report it
                    //   GameNet can grant/not grant it depending on the consensus rules, and if inst
                    //   gets the claim it can just blow it up then.

                    // This is stupid and temporary (rather than just getting rid of the test)
                    // TODO: FIX THIS!!!  &&&&&&&
                    gameData.ReportPlaceClaimed(this, xIdx, zIdx, entryHead, exitHead);
                    //gameInst.apian.SendPlaceClaimObs(apianTime, this, xIdx, zIdx, entryHead, exitHead);
                }
            } else {
                // Hit a marker. Report it.
                gameData.ReportPlaceHit(this, p.xIdx, p.zIdx, entryHead, exitHead);
            }
        }

        //
        // Static tools. Potentially useful publicly
        //
        public static Vector2 NearestGridPoint(Vector2 curPos)
        {
            // TODO: duplicate of Ground method
            float invGridSize = 1.0f / Ground.gridSize;
            return new Vector2(Mathf.Round(curPos.x * invGridSize) * Ground.gridSize, Mathf.Round(curPos.y * invGridSize) * Ground.gridSize);
        }

        public bool CloseToGridPoint(Vector2 curPos)
        {
            float dist = Vector2.Distance(curPos, NearestGridPoint(curPos));
            return (dist < length);
        }

        public static Vector2 UpcomingGridPoint(Vector2 pos, Heading head)
        {
            // it's either the current closest point (if direction to it is the same as heading)
            // or is the closest point + gridSize*unitOffsetForHeading[curHead] if closest point is behind us
            Vector2 point = NearestGridPoint( pos);
            if (Vector2.Dot(GameConstants.UnitOffset2ForHeading(head), point - pos) <= 0)
            {
                point += GameConstants.UnitOffset2ForHeading(head) * Ground.gridSize;
            }
            return point;
        }

        public Vector2 UpcomingGridPoint(Vector2 basePos)
        {
            return UpcomingGridPoint(basePos, heading);
        }

        public void UpdatePosFromCommand(long timeStamp, long curTime, Vector2 posFromCmd, Heading cmdHead)
        {

        //     // Given an authoritative position from a command (claim or hit)
        //     // Compute where the bike should be now according to the command
        //     float deltaSecs = Mathf.Max((curTime - timeStamp) *.001f, .000001f);

       //      logger.Verbose($"UpdatePosFromCmd():  Bike: {bikeId}, CurTime: {curTime}, CmdTime: {timeStamp} DeltaSecs: {deltaSecs}");

            if ( !basePosition.Equals(posFromCmd))
                logger.Info($"UpdatePosFromCmd(): *Conflict* Bike: {bikeId}, CurPos: {basePosition.ToString()}, CmdPos: {posFromCmd.ToString()}");

            basePosition = posFromCmd;
            timeAtPosition = timeStamp;
            heading = cmdHead;

        //     position = cmdPos;

        //     // Roll back the current pos to the timestamp, average, and then
        //     // roll back forwards
        //     //logger.Info($"UpdatePosFromCmd(): Cur time: {gameInst.FrameApianTime}  Pos: {position.ToString()} Bike: {bikeId}");
        //     //logger.Info($"                    Cmd time: {timeStamp}  Pos: {posFromCmd.ToString()}");

        //     //Vector2 testPos = position - GameConstants.UnitOffset2ForHeading(heading) * deltaSecs * speed;
        //     //logger.Info($"             Rolled-back Pos: {testPos.ToString()}");
        //     //Vector2 avgTsPos = (posFromCmd + testPos) * .5f;
        //     //logger.Info($"                     Avg Pos: {avgTsPos.ToString()}");
        //     //position = avgTsPos + GameConstants.UnitOffset2ForHeading(heading) * deltaSecs * speed;
        //     //logger.Info($"                     New Pos: {position.ToString()}");
        }

        // public Vector2 PosAtTime(long testTime, long curTime)
        // {
        //     float deltaSecs = (curTime - testTime) * .001f;
        //     Vector2 testPos = position - GameConstants.UnitOffset2ForHeading(heading) * deltaSecs * speed;
        //     return position;
        // }

    }
}
