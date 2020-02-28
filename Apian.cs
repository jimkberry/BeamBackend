
using System;
using System.Linq;
using System.Collections.Generic;
using UniLog;

namespace Apian
{
    public abstract class ApianMessage
    {   
        public string MsgType {get; private set;}
        public ApianMessage(string t) => MsgType = t;
    }

    public abstract class ApianAssertion 
    {
        // TODO: WHil eit looked good written down, it may be that "ApianAssertion" is a really bad name,
        // given what "assertion" usually means in the world of programming.
        public long SequenceNumber {get; private set;}
        public ApianMessage Message {get; private set;}

        public ApianAssertion(ApianMessage msg, long seq)
        {
            Message = msg;
            SequenceNumber = seq;
        }
    }

    public interface IApianClient 
    {
        void OnApianAssertion(ApianAssertion aa);
    }

    public class ApianPeer
    {
        public enum Status
        {
            kNew,  // just created
            kJoining, // In the process of synching and getting up-to-date
            kActive, // part of the gang
            kMissing, // not currently present, but only newly so
        }

        public string P2pId {get; private set;}
        public Status status;

        public ApianPeer(string _p2pId)
        {
            status = Status.kNew;
            P2pId = _p2pId;
        }
    }


    public abstract class ApianBase
    {
        //public List<ApianPeer> apianPeers; // TODO - tease this out of implmentations back into this base one

        public ApianBase() 
        {
            //apianPeers = new List<ApianPeer>();
        }

    }
  
    public class ApianVoteMachine<T>
    {
        public static long NowMs => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;            

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

        protected Dictionary<T, VoteData> voteDict;
        public UniLogger logger;

        public ApianVoteMachine(UniLogger _logger) 
        { 
            logger = _logger;
            voteDict = new Dictionary<T, VoteData>();
        }

        public void Cleanup()
        {
            List<T> delKeys = voteDict.Keys.Where(k => voteDict[k].ExpireTs < NowMs).ToList();
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

            if ((vd.peerIds.Count >= vd.NeededVotes) && (vd.voteIsDone == false))
            {
                // only call this once
                vd.voteIsDone = true;
                voteDict[candidate] = vd; 
                return true;
            }
            return false;

        }
    }    


}