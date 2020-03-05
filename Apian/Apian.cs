
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
  
    public enum VoteStatus
        {
        kVoting,
        kWon,
        kLost,  // timed out
        kNoVotes  // Vote not found
    }

    public class ApianVoteMachine<T>
    {
        public const long kDefaultTimeoutMs = 300;        
        public static long SysMs => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;            

        protected struct VoteData
        {
            public int NeededVotes {get; private set;}
            public long ExpireTs {get; private set;}
            public VoteStatus Status {get; private set;}
            public List<string> peerIds;
          
            public void UpdateStatus(long nowMs) 
            { 
                if (Status == VoteStatus.kVoting)
                {
                    if (nowMs > ExpireTs)
                        Status = VoteStatus.kLost;
                    else if (peerIds.Count >= NeededVotes)
                        Status = VoteStatus.kWon;
                }
            }

            public VoteData(int voteCnt, long expireTimeMs)
            {
                NeededVotes = voteCnt;
                ExpireTs = expireTimeMs;
                Status = VoteStatus.kVoting;
                peerIds = new List<string>();   
            }
        }

        protected virtual int MajorityVotes(int peerCount) => peerCount / 2 + 1;
        protected Dictionary<T, VoteData> voteDict;
        protected long TimeoutMs {get; private set;}
        public UniLogger logger;

        public ApianVoteMachine(long timeoutMs=kDefaultTimeoutMs, UniLogger _logger=null) 
        { 
            TimeoutMs = timeoutMs;
            logger = _logger ?? UniLogger.GetLogger("ApianVoteMachine");
            voteDict = new Dictionary<T, VoteData>();
        }

        protected void UpdateAllStatus()
        {
            // if timed out set status to Lost
            foreach (VoteData vote in voteDict.Values)
                vote.UpdateStatus(SysMs);
        }

        public VoteStatus AddVote(T candidate, string votingPeer, int totalPeers, bool removeIfDone=false)
        {
            UpdateAllStatus();
            VoteData vd;
            try {
                vd = voteDict[candidate];
                if (vd.Status == VoteStatus.kVoting)
                {
                    vd.peerIds.Add(votingPeer);
                    vd.UpdateStatus(SysMs);
                    voteDict[candidate] = vd; // VoteData is a struct (value) so must be re-added
                    logger.Debug($"Vote.Add: +1 for: {candidate.ToString()}, Votes: {vd.peerIds.Count}");
                }
            } catch (KeyNotFoundException) {
                int majorityCnt = MajorityVotes(totalPeers);                   
                vd = new VoteData(majorityCnt, SysMs+TimeoutMs);
                vd.peerIds.Add(votingPeer);
                vd.UpdateStatus(SysMs);                
                voteDict[candidate] = vd;
                logger.Debug($"Vote.Add: New: {candidate.ToString()}, Majority: {majorityCnt}"); 
            }

            VoteStatus retVal = vd.Status;
            if (removeIfDone && retVal != VoteStatus.kVoting)
                DoneWithVote(candidate);
            return retVal;
        }

        public void DoneWithVote(T candidate)
        {
            try {
                voteDict.Remove(candidate);  
            } catch (KeyNotFoundException) {}                   
        }

        public VoteStatus GetStatus(T candidate, bool removeIfDone=false)
        {
            // Have to get to it before it expires - better to use the reult of AddVote()
            UpdateAllStatus();
            VoteStatus status = VoteStatus.kNoVotes;
            try {
                VoteData vd = voteDict[candidate];  
                status = vd.Status;        
                if (removeIfDone && status != VoteStatus.kVoting)
                    DoneWithVote(candidate); 
            } catch (KeyNotFoundException) 
            { 
                //logger.Warn($"GetStatus: Vote not found");
            }
            return status;
        }        
        
    }    


}