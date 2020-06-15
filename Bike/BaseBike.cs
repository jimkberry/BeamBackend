using System.Collections.Generic;
using Newtonsoft.Json;
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

        public BaseBike(BeamGameInstance gi, string _id, string _peerId, string _name, Team _team, string ctrl, Vector2 initialPos, Heading head)
        {
            gameInst = gi;
            bikeId = _id;
            peerId = _peerId;
            name = _name;
            team = _team;
            position = initialPos;
            heading = head;
            ctrlType = ctrl;
            score = kStartScore;
            logger = UniLogger.GetLogger("BaseBike");
        }


        public class SerialArgs
        {
            public Dictionary<string,int> peerIdxDict;
            public long cpTimeStamp;
            public SerialArgs(Dictionary<string,int> pid, long ts) {peerIdxDict=pid; cpTimeStamp=ts;}
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

            // First, though - propagate the bikes position back to the checkpoint timestamp
            // just in case it has just barely passed through a point (command might still be pending)
            float secsSinceCp = (gameInst.FrameApianTime - sArgs.cpTimeStamp) * .001f;
            Vector2 cpPos = position - GameConstants.UnitOffset2ForHeading(heading) * speed * secsSinceCp;

            // Find what THEN was the last point visited andw when we were there
            Vector2 point = speed == 0 ? cpPos : UpcomingGridPoint(cpPos, GameConstants.ReciprocalHeading(heading));
            float timeToPoint = speed == 0 ? 0 : Vector2.Distance(cpPos, point) / speed;
            long timeAtPoint = speed == 0 ? 0 : sArgs.cpTimeStamp - (long)(timeToPoint * 1000f);

            // round to nearest 100 ms...
            // TODO: I'm not real happy about this stuff. It just kinda smells funny to me.
            timeAtPoint = _RoundToNearest(100, timeAtPoint);

            // // WHile I'm debugging. These are in grid-index space (tomake it easier to compare withthe places list)
            // float indexX =   (position.x - Ground.minX) / Ground.gridSize;
            // float indexZ =   (position.y - Ground.minZ) / Ground.gridSize;

            return  JsonConvert.SerializeObject(new object[]{
                    bikeId,
                    sArgs.peerIdxDict[peerId],
                    name,
                    team.TeamID,
                    ctrlType,
                    (long)(point.x * 1000f), // integer mm
                    (long)(point.y * 1000f),
                    timeAtPoint,
                    heading,
                    speed,
                    score
                 });
        }

        // Commands from outside

        public void Loop(float secs)
        {
            //logger.Debug($"Loop(). Bike: {bikeId} Speed: {speed})");
            _updatePosition(secs);
        }

        public void AddScore(int val) => score += val;

        public void ApplyTurn(TurnDir dir, Vector2 nextPt, float commandDelaySecs, BeamMessage.BikeState reportedState)
        {
            // TODO: &&&& reported state really should not be there.
            if (commandDelaySecs != 0)
                logger.Verbose($"ApplyTurn(): rolling back {commandDelaySecs} to turn {bikeId} {dir}");

            float rollbackSecs = _rollbackTime(commandDelaySecs); // Move the bike backwards to the reported time

            Vector2 testPt = UpcomingGridPoint();
            if (!testPt.Equals(nextPt))
            {
                logger.Warn($"ApplyTurn(): {(nextPt.ToString())} is the wrong upcoming point for bike: {bikeId}");
                logger.Warn($"We think it should be {(testPt.ToString())}");
                logger.Warn($"Reported State:\n{JsonConvert.SerializeObject(reportedState)}");
                logger.Warn($"Actual State:\n{JsonConvert.SerializeObject(new BeamMessage.BikeState(this)) }");
                logger.Warn($"Stuffing-in reported state data.");
                // Just shove the reported data in
                score = reportedState.score;
                speed = reportedState.speed;
                heading = reportedState.heading;
                position = new Vector2(reportedState.xPos, reportedState.yPos);

            }
            pendingTurn = dir;
            _updatePosition(rollbackSecs);
        }

        public void ApplyCommand(BikeCommand cmd, Vector2 nextPt, float commandDelaySecs)
        {
            // Check to see that the reported upcoming point is what we think it is, too
            // In real life this'll get checked by Apian/consensus code to decide if the command
            // is valid before it even makes it here. Or... we might have to "fix things up"
            if (commandDelaySecs != 0)
                logger.Verbose($"ApplyCommand(): rolling back {commandDelaySecs} to apply {cmd} to {bikeId}");

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


        private void _updatePosition(float secs)
        {
            if (secs == 0 || speed == 0)
                return;

            Vector2 upcomingPoint = UpcomingGridPoint();
            float timeToPoint = Vector2.Distance(position, upcomingPoint) / speed;

            Vector2 newPos = position;
            Heading newHead = heading;

            if (secs >= timeToPoint)
            {
                logger.Debug($"_updatePosition() Bike: {bikeId} MsToPoint: {(long)(timeToPoint*1000)}");
                secs -= timeToPoint;
                newPos =  upcomingPoint;
                newHead = GameConstants.NewHeadForTurn(heading, pendingTurn);
                pendingTurn = TurnDir.kUnset;
                DoAtGridPoint(upcomingPoint, heading, newHead, gameInst.FrameApianTime);// + (long)(timeToPoint*1000)); // Using the offset makes odd things happen on the server
                heading = newHead;
            }

            newPos += GameConstants.UnitOffset2ForHeading(heading) * secs * speed;

            position = newPos;
        }

        private float _rollbackTime(float secs)
        {
            // Propagate the bike backwards in time by "secs" or almost the length of time that
            // takes it backwards to the previous point - whichever is shorter
            // This is to try to minimize message delays.
            // If, for instance, a bike command is received that we know happened .08 secs ago,
            // then the code handling the command can roll the bike back, apply the ecommand, and then
            // call bike.update(rolledBackTime) to have effectively back-applied the command.
            // it's not really safe to go backwards across a gridpoint, so that's as far as we'll go back.
            // It returns the amount of time rolled back as a positive float.
            if (speed == 0 || secs <= 0)
                return 0;
            Vector2 upcomingPoint = UpcomingGridPoint();
            float timeToNextPoint = Vector2.Distance(position, upcomingPoint) / speed;
            float timeSinceLastPoint = Mathf.Max(0,((Ground.gridSize * .8f) / speed) - timeToNextPoint); // Note QUITE all the way back
            secs = Mathf.Min(secs, timeSinceLastPoint);
            position -= GameConstants.UnitOffset2ForHeading(heading) * secs * speed;
            return secs;
        }

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
            BeamGameState gData = gameInst.GameData;
            BeamPlace p = gData.GetPlace(pos);
            logger.Verbose($"DoAtGridPoint({pos.ToString()}) Bike: {bikeId} Time: {apianTime} Dt: {apianTime - gameInst.FrameApianTime}");
            if (p == null)
            {
                int xIdx, zIdx;
                (xIdx, zIdx) = Ground.NearestGridIndices(pos);
                // is it on the map?
                if (gData.Ground.IndicesAreOnMap(xIdx, zIdx))
                {
                    // Yes. Since it's empty send a claim report
                    // Doesn't matter if the bike is local or not - THIS peer thinks there's a claim
                    gameInst.apian.SendPlaceClaimObs(apianTime, this, xIdx, zIdx, entryHead, exitHead);
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
                    gameInst.apian.SendPlaceClaimObs(apianTime, this, xIdx, zIdx, entryHead, exitHead);
                }
            } else {
                // Hit a marker. Report it.
                gameInst.apian.SendPlaceHitObs(apianTime, this, p.xIdx, p.zIdx, entryHead, exitHead);
            }
        }

        //
        // Static tools. Potentially useful publicly
        //
        public static Vector2 NearestGridPoint(Vector2 pos)
        {
            // TODO: duplicate of Ground method
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
            if (Vector2.Dot(GameConstants.UnitOffset2ForHeading(head), point - pos) <= 0)
            {
                point += GameConstants.UnitOffset2ForHeading(head) * Ground.gridSize;
            }
            return point;
        }

        public Vector2 UpcomingGridPoint( )
        {
            return UpcomingGridPoint(position, heading);
        }

        public void UpdatePosFromCommand(long timeStamp, Vector2 posFromCmd, Heading cmdHead)
        {
            // Given an authoritative position from a command (claim or hit)
            // Compute where the bike should be now according to the command
            float deltaSecs = Mathf.Max((gameInst.FrameApianTime - timeStamp) *.001f, .000001f);

            Vector2 cmdPos = posFromCmd + GameConstants.UnitOffset2ForHeading(cmdHead) * deltaSecs * speed;
            logger.Verbose($"UpdatePosFromCmd():  Bike: {bikeId}, CurTime: {gameInst.FrameApianTime}, CmdTime: {timeStamp} DeltaSecs: {deltaSecs}");
            logger.Verbose($"CurPos: {position.ToString()}, CurCmdPos: {cmdPos.ToString()}");

            position = cmdPos;

            // Roll back the current pos to the timestamp, average, and then
            // roll back forwards
            //logger.Info($"UpdatePosFromCmd(): Cur time: {gameInst.FrameApianTime}  Pos: {position.ToString()} Bike: {bikeId}");
            //logger.Info($"                    Cmd time: {timeStamp}  Pos: {posFromCmd.ToString()}");

            //Vector2 testPos = position - GameConstants.UnitOffset2ForHeading(heading) * deltaSecs * speed;
            //logger.Info($"             Rolled-back Pos: {testPos.ToString()}");
            //Vector2 avgTsPos = (posFromCmd + testPos) * .5f;
            //logger.Info($"                     Avg Pos: {avgTsPos.ToString()}");
            //position = avgTsPos + GameConstants.UnitOffset2ForHeading(heading) * deltaSecs * speed;
            //logger.Info($"                     New Pos: {position.ToString()}");
        }

        public Vector2 PosAtTime(long testTime) // assumes NOW is FrameApianTime
        {
            float deltaSecs = (gameInst.FrameApianTime - testTime) * .001f;
            Vector2 testPos = position - GameConstants.UnitOffset2ForHeading(heading) * deltaSecs * speed;
            return position;
        }

    }
}
