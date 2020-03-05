
using System;
using System.Linq;
using System.Collections.Generic;
using GameNet;
using UniLog;

namespace Apian
{
    public class ApianMessage
    {   
        public const string kRequestGroups = "APrg";        
        public const string kGroupAnnounce = "APga";
        public const string kGroupJoinReq = "APgjr";        
        public const string kGroupJoinVote = "APgjv";         

        public string msgType;
        public ApianMessage(string t) => msgType = t;
    }

    public class RequestGroupsMsg : ApianMessage // Send on main channel
    {
        public RequestGroupsMsg() : base(kRequestGroups) {}  
    } 
    public class GroupAnnounceMsg : ApianMessage // Send on main channel
    {
        public string groupId;
        public int peerCnt;
        public GroupAnnounceMsg(string id, int cnt) : base(kGroupAnnounce) {groupId = id; peerCnt=cnt;}  
    }  

    public class GroupJoinRequestMsg : ApianMessage // Send on main channel
    {
        public string groupId;
        public string peerId;
        public GroupJoinRequestMsg(string id, string pid) : base(kGroupJoinReq) {groupId = id; peerId=pid;}  
    }    

    public class GroupJoinVoteMsg : ApianMessage // Send on main channel
    {
        public string groupId;
        public string peerId;
        public bool approve;
        public GroupJoinVoteMsg(string gid, string pid, bool doIt) : base(kGroupJoinVote) {groupId = gid; peerId=pid; approve=doIt;}  
    }

    public abstract class ApianAssertion 
    {
        // TODO: WHile it looked good written down, it may be that "ApianAssertion" is a really bad name,
        // given what "assertion" usually means in the world of programming.
        public long SequenceNumber {get; private set;}

        public ApianAssertion( long seq)
        {
            SequenceNumber = seq;
        }
    }

    public interface IApianClient 
    {
        void OnApianAssertion(ApianAssertion aa);
    }

    public abstract class ApianBase
    {
        protected Dictionary<string, Action<string, string, string, long>> ApMsgHandlers;
        public UniLogger logger; 

        protected IGameNet GameNet {get; private set;}
        protected long SysMs { get => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;}

        public ApianBase(IGameNet gn) {
            GameNet = gn;
            logger = UniLogger.GetLogger("Apian");             
            ApMsgHandlers = new Dictionary<string, Action<string, string, string, long>>(); 
            // Add any truly generic handlers here          
        }

        public abstract void Update();

        public abstract void SendApianMessage(string toChannel, ApianMessage msg);
        public  void AddGroupChannel(string channel) => GameNet.AddChannel(channel);
        public  void RemoveGroupChannel(string channel) => GameNet.RemoveChannel(channel);
        public abstract void OnApianMessage(string msgType, string msgJson, string fromId, string toId, long lagMs);   
    }
  
    public class ApianVoteMachine<T>
    {
        public static long SysMs => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;            

        protected struct VoteData
        {
            public const long kTimeoutMs = 300;
            public int NeededVotes {get; private set;}
            public long ExpireTs {get; private set;}
            public bool voteIsDone;
            public List<string> peerIds;
          
            public VoteData(int voteCnt, long now)
            {
                NeededVotes = voteCnt;
                ExpireTs = now + kTimeoutMs;
                peerIds = new List<string>();
                voteIsDone = false;
            }
        }

        protected virtual int NeededVotes(int peerCount) => peerCount / 2 + 1;
        protected Dictionary<T, VoteData> voteDict;
        public UniLogger logger;

        public ApianVoteMachine(UniLogger _logger) 
        { 
            logger = _logger;
            voteDict = new Dictionary<T, VoteData>();
        }

        public void Cleanup()
        {
            List<T> delKeys = voteDict.Keys.Where(k => voteDict[k].ExpireTs < SysMs).ToList();
            foreach (T k in delKeys)
            {
                logger.Debug($"Vote.Cleanup(): removing: {k.ToString()}, {voteDict[k].peerIds.Count} votes.");
                voteDict.Remove(k);
            }
        }

        public bool AddVote(T candidate, string votingPeer, int totalPeers)
        {
            Cleanup();
            VoteData vd;
            try {
                vd = voteDict[candidate];
                vd.peerIds.Add(votingPeer);
                voteDict[candidate] = vd; // VoteData is a struct (value) so must be re-added
                logger.Debug($"Vote.Add: +1 for: {candidate.ToString()}, Votes: {vd.peerIds.Count}");                
            } catch (KeyNotFoundException) {
                int majorityCnt = NeededVotes(totalPeers);                   
                vd = new VoteData(majorityCnt, SysMs);
                vd.peerIds.Add(votingPeer);
                voteDict[candidate] = vd;
                logger.Debug($"Vote.Add: New: {candidate.ToString()}, Majority: {majorityCnt}"); 
            }

            if ((vd.peerIds.Count >= vd.NeededVotes) && (vd.voteIsDone == false))
            {
                // only call this once
                vd.voteIsDone = true;
                voteDict[candidate] = vd; 
                return true;
            }
            return false;
        }

        public bool VoteWon(T candidate)
        {
            // Have to get to it before it expires - better to use the reult of AddVote()
            Cleanup();
            Boolean success = false;
            try {
                VoteData vd = voteDict[candidate];  
                success = vd.voteIsDone;              
            } catch (KeyNotFoundException) { }
            return success;
        }        

        public bool VoteIsGone(T candidate)
        {
            Cleanup();
            Boolean itsGone = true;
            try {
                VoteData vd = voteDict[candidate];  
                itsGone = false;              
            } catch (KeyNotFoundException) { }
            return itsGone;
        }          
    }    


}