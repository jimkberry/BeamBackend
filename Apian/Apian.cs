
using System.Xml.Linq;
using System.Reflection.Emit;
using System;
using System.Linq;
using System.Collections.Generic;
using GameNet;
using UniLog;

namespace Apian
{
    public interface IApianClient 
    {
        void OnApianAssertion(ApianAssertion aa);
    }

    public abstract class ApianBase
    {
        protected Dictionary<string, Action<string, string, string, long>> ApMsgHandlers;
        public UniLogger logger; 

        public IApianGroupManager ApianGroup  {get; protected set;}    
        public IApianClock ApianClock {get; protected set;}  
        protected IGameNet GameNet {get; private set;}      
        protected long SysMs { get => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;}

        public ApianBase(IGameNet gn) {
            GameNet = gn;          
            logger = UniLogger.GetLogger("Apian");             
            ApMsgHandlers = new Dictionary<string, Action<string, string, string, long>>(); 
            // Add any truly generic handlers here          
        }

        public abstract void Update();
        
        // Apian Messages
        public abstract void OnApianMessage(string msgType, string msgJson, string fromId, string toId, long lagMs);         
        public abstract void SendApianMessage(string toChannel, ApianMessage msg);

        // Group-related
        public void AddGroupChannel(string channel) => GameNet.AddChannel(channel); // IApianGroupManager uses this. Maybe it should use GameNet directly?
        public void RemoveGroupChannel(string channel) => GameNet.RemoveChannel(channel);
        public abstract void OnMemberJoinedGroup(string peerId); // Any peer, including local. On getting this check with ApianGroup for details.
  
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
        public const long kDefaultExpireMs = 300;        
        public const long kDefaultCleanupMs = 900;         
        public static long SysMs => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;            

        protected struct VoteData
        {
            public int NeededVotes {get; private set;}
            public long ExpireTs {get; private set;} // vote defaults to "no" after this
            public long CleanupTs {get; private set;} // VoteData gets removed after this
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

            public VoteData(int voteCnt, long expireTimeMs, long cleanupTimeMs)
            {
                NeededVotes = voteCnt;
                ExpireTs = expireTimeMs;
                CleanupTs = cleanupTimeMs;
                Status = VoteStatus.kVoting;
                peerIds = new List<string>();   
            }
        }

        protected virtual int MajorityVotes(int peerCount) => peerCount / 2 + 1;
        protected Dictionary<T, VoteData> voteDict;
        protected long TimeoutMs {get; private set;}
        protected long CleanupMs {get; private set;}        
        public UniLogger logger;

        public ApianVoteMachine(long timeoutMs, long cleanupMs, UniLogger _logger=null) 
        { 
            TimeoutMs = timeoutMs;
            CleanupMs = cleanupMs;
            logger = _logger ?? UniLogger.GetLogger("ApianVoteMachine");
            voteDict = new Dictionary<T, VoteData>();
        }

        protected void UpdateAllStatus()
        {
            // remove old and forgotten ones
            voteDict = voteDict.Where(pair => pair.Value.CleanupTs >= SysMs)
                                 .ToDictionary(pair => pair.Key, pair => pair.Value);          

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
                vd = new VoteData(majorityCnt, SysMs+TimeoutMs, SysMs+CleanupMs);
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