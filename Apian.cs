
using System;
using System.Linq;
using System.Collections.Generic;
using UniLog;
using GameNet;

namespace Apian
{
    public class ApianMessage
    {
        // These should be opaque to anything other than Apian
        public string Payload { get; private set; }
    }

    public interface IApian : IGameNetClient {}
    // The IApian interface IS an IGameNetClient
    // It's be nicer if the method names had "obs" and "req" in them to signify
    // they are observations and requests, but it really IS at this level IGameNet.

    public interface IApianClient : IGameNetClient {}
    // But the Apian client (the Business Logic/State instance) is *also* an IGamenetClient. 
    // That's kinda the point: Apian currently looks kinda like a passthru

  
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
            VoteData vd;

            Cleanup();

            if (voteDict.TryGetValue(candidate, out vd))
            {
                vd.peerIds.Add(observerPeer);
                logger.Debug($"Vote.Add: +1 for: {candidate.ToString()}, Votes: {vd.peerIds.Count}");                    
            } else {
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
                return true;
            }
            return false;

        }
    }    


}