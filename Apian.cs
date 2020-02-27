
using System;
using System.Linq;
using System.Collections.Generic;
using UniLog;

namespace Apian
{
    public abstract class ApianMessage
    {   
        public string msgType;
        public ApianMessage(string t) => msgType = t;
    }

    public abstract class ApianAssertion 
    {
        public long sequenceNumber;
        public ApianMessage message;

        public ApianAssertion(ApianMessage msg, long seq)
        {
            message = msg;
            sequenceNumber = seq;
        }
    }

    public interface IApianClient 
    {
        void OnApianAssertion(ApianAssertion aa);
    }


    public abstract class ApianBase
    {

    }
  
    public class ApianVoteMachine<T>
    {
        public static long NowMs => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;            

        protected struct VoteData
        {
            public const long timeoutMs = 300;
            public int neededVotes;
            public long expireTs;
            public bool voteDone;
            public List<string> peerIds;
          
            public VoteData(int voteCnt, long now)
            {
                neededVotes = voteCnt;
                expireTs = now + timeoutMs;
                peerIds = new List<string>();
                voteDone = false;
            }
        }

        protected Dictionary<T, VoteData> voteDict;
        public UniLogger logger;

        public ApianVoteMachine(UniLogger _logger) 
        { 
            logger = _logger;
            voteDict = new Dictionary<T, VoteData>();
        }

        public void Cleanup()
        {
            List<T> delKeys = voteDict.Keys.Where(k => voteDict[k].expireTs < NowMs).ToList();
            foreach (T k in delKeys)
            {
                logger.Debug($"Vote.Cleanup(): removing: {k.ToString()}, {voteDict[k].peerIds.Count} votes.");
                voteDict.Remove(k);
            }
        }

        public bool AddVote(T candidate, string observerPeer, int totalPeers)
        {
            Cleanup();
            VoteData vd;
            try {
                vd = voteDict[candidate];
                vd.peerIds.Add(observerPeer);
                voteDict[candidate] = vd; // VoteData is a struct (value) so must be re-added
                logger.Debug($"Vote.Add: +1 for: {candidate.ToString()}, Votes: {vd.peerIds.Count}");                
            } catch (KeyNotFoundException) {
                int majorityCnt = totalPeers / 2 + 1;                    
                vd = new VoteData(majorityCnt, NowMs);
                vd.peerIds.Add(observerPeer);
                voteDict[candidate] = vd;
                logger.Debug($"Vote.Add: New: {candidate.ToString()}, Majority: {majorityCnt}"); 
            }

            if ((vd.peerIds.Count >= vd.neededVotes) && (vd.voteDone == false))
            {
                // only call this once
                vd.voteDone = true;
                voteDict[candidate] = vd; 
                return true;
            }
            return false;

        }
    }    


}