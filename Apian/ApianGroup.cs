using System;
using System.Linq;
using System.Collections.Generic;
using UniLog;

namespace Apian
{
    public class ApianMember
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

        public ApianMember(string _p2pId)
        {
            status = Status.kNew;
            P2pId = _p2pId;
        }
    }

    public interface IApianGroup
    {
        string GroupId {get;}
        Dictionary<string, ApianMember> Members {get;}
        void Update();
    }

    public class ApianBasicGroup : IApianGroup
    {
        protected abstract class LocalState
        {
            protected struct JoinVoteKey // for ApianVoteMachine
            {
                public string peerId;
                public JoinVoteKey(string _pid) => peerId=_pid;
            }

            public const int kGroupAnnouceTimeoutMs = 1000;
            public const int kGroupAnnouceSendTimeoutMs = 250;            
            public const int kGroupVoteTimeoutMs = 2000;
            protected ApianBasicGroup Group {get; private set;}

            protected int NeededJoinVotes(int peerCnt) => peerCnt/2 + 1;
            public abstract void Start();
            public abstract LocalState Update(); // returns isntance to make current and start. Null if done.
            public virtual void OnApianMsg(ApianMessage msg, string msgSrc, string msgChannel) {}           

            public LocalState(ApianBasicGroup group) { Group = group;}
         }

        protected class StateListeningForGroup : LocalState
        {
            long listenTimeoutMs;
            GroupAnnounceMsg heardMsg;

            public StateListeningForGroup(ApianBasicGroup group) : base(group) {}           

            public override void Start() 
            {
                Group.RequestGroups();
                // wait at least 1.5 timeouts, plus a random .5
                // being pretty loosy-goosey with Random bcause it really doesn't matter much
                listenTimeoutMs = Group.SysMs + 3*kGroupAnnouceTimeoutMs/2 + new Random().Next(kGroupAnnouceTimeoutMs/2);
                Group.logger.Info($"{this.GetType().Name} - Requested Groups. Listening");
            }
            public override LocalState Update()
            {
                LocalState retVal = this;
                if (heardMsg != null)
                    retVal = new StateJoiningGroup(Group, heardMsg.groupId, heardMsg.peerCnt);

                else if (Group.SysMs > listenTimeoutMs) // Bail
                {
                    Group.logger.Info($"{this.GetType().Name} - Listen timed out.");
                    retVal = new StateCreatingGroup(Group);
                }
                return retVal;
            }

            public override void OnApianMsg(ApianMessage msg, string msgSrc, string msgChannel)
            {
                switch (msg.msgType)
                {
                case ApianMessage.kGroupAnnounce:
                    heardMsg = msg as GroupAnnounceMsg;                
                    Group.logger.Info($"{this.GetType().Name} - Heard group announced: {heardMsg.groupId}. Joining.");
                    break;
                }
            }
        }

        protected class StateCreatingGroup : LocalState
        {
            long listenTimeoutMs;            
            string newGroupId;
            string otherGroupId; // group we heard announced. We should cancel.       
            
            public StateCreatingGroup(ApianBasicGroup group) : base(group) {}               
            public override void  Start() 
            {
                newGroupId = System.Guid.NewGuid().ToString(); // TODO: do this better (details are all hidden in here)
                Group.AnnounceGroup(newGroupId, 1);
                Group.RequestGroups();
                listenTimeoutMs = Group.SysMs + 3*kGroupAnnouceTimeoutMs/2 + new Random().Next(kGroupAnnouceTimeoutMs/2);
                Group.logger.Info($"{this.GetType().Name} - Announced new group: {newGroupId}. Waiting.");                
            }
            public override LocalState Update()
            {
                LocalState retVal = this;
                if (otherGroupId != null) // Cancel back to the  start
                    retVal = new StateListeningForGroup(Group);

                else if (Group.SysMs > listenTimeoutMs) // We're in! 
                {
                    Group.logger.Info($"{this.GetType().Name} - Wait timed out. Joining created group.");
                    retVal = new StateInGroup(Group, newGroupId);
                }
                return retVal;                
            }

            public override void OnApianMsg(ApianMessage msg, string msgSrc, string msgChannel)
            {
                switch (msg.msgType)
                {
                case ApianMessage.kGroupAnnounce:
                    string heardId = (msg as GroupAnnounceMsg).groupId;
                    if (heardId != null && (heardId != newGroupId)) // 
                    {
                        Group.logger.Info($"{this.GetType().Name} - Received an announcement for group: {heardId}. Bailing.");
                        otherGroupId = heardId; // An announcement from someone else
                    }
                    break;
                }
            }            
        }

        // protected class StateJoiningGroup : LocalState
        // {
        //     long voteTimeoutMs;
        //     string newGroupId;
        //     int peerCnt;
        //     int neededVotes;
        //     int votesRcvd;
        //     int yesVotes;

        //     public StateJoiningGroup(ApianBasicGroup group, string _groupId, int _peerCnt) : base(group) 
        //     {
        //         newGroupId = _groupId;
        //         peerCnt = _peerCnt;
        //         neededVotes = peerCnt/2 + 1;
        //         votesRcvd = 0;
        //         yesVotes = 0;
        //         voteTimeoutMs = Group.SysMs + kGroupVoteTimeoutMs;                
        //     }               
        //     public override void Start() 
        //     {
        //         Group.RequestToJoinGroup(newGroupId);
        //         voteTimeoutMs = Group.SysMs + kGroupVoteTimeoutMs;  
        //         Group.logger.Info($"{this.GetType().Name} - Requested join group: {newGroupId}. Waiting for votes.");              
        //     }
        //     public override LocalState Update()
        //     {
        //         LocalState retVal = this;

        //         int votesLeft = peerCnt - votesRcvd;
        //         if (yesVotes >= neededVotes)
        //         {
        //             Group.logger.Info($"{this.GetType().Name} - Got enough yes votes.");                    
        //             retVal = new StateInGroup(Group, newGroupId);
        //         }
        //         else if ( yesVotes + votesLeft < neededVotes)
        //         {
        //             Group.logger.Error("Error joining basic group: lost vote!");
        //             retVal = new StateListeningForGroup(Group);
        //         }
        //         else if (Group.SysMs > voteTimeoutMs) // Bail
        //         {
        //             Group.logger.Error("Error joining group: timeout!");
        //             retVal = new StateListeningForGroup(Group);                   
        //         }
        //         return retVal;
        //     }

        //     public override void OnApianMsg(ApianMessage msg, string msgSrc, string msgChannel)
        //     {
        //         switch (msg.msgType)
        //         {
        //         case ApianMessage.kGroupJoinVote:               
        //             GroupJoinVoteMsg vmsg = msg as GroupJoinVoteMsg;
        //             Group.logger.Info($"{this.GetType().Name} - Got a vote. Group: {vmsg.groupId}");                     
        //             if (vmsg.groupId == newGroupId && vmsg.peerId == Group.LocalP2pId)
        //             {
        //                 Group.logger.Info($"{this.GetType().Name} - Got a {(vmsg.approve ? "yes" : "no")} vote.");                         
        //                 votesRcvd++;
        //                 yesVotes += (vmsg.approve ? 1 : 0);
        //             }
        //             break;
        //         }
        //     }
        // }        

        protected class StateJoiningGroup : LocalState
        {
            protected ApianVoteMachine<JoinVoteKey> joinVoteMachine;            
            string newGroupId;
            int peerCnt;            
            bool voteWon = false;
            protected JoinVoteKey voteKey; // only care about one

            public StateJoiningGroup(ApianBasicGroup group, string _groupId, int _peerCnt) : base(group) 
            {
                newGroupId = _groupId;
                peerCnt = _peerCnt;
                joinVoteMachine = new ApianVoteMachine<JoinVoteKey>(Group.logger);
                voteKey =  new JoinVoteKey(Group.LocalP2pId);
            }               
            public override void Start() 
            {
                Group.RequestToJoinGroup(newGroupId);
                Group.logger.Info($"{this.GetType().Name} - Requested join group: {newGroupId}. Waiting for votes.");              
            }
            public override LocalState Update()
            {
                LocalState retVal = this;

                if (voteWon)
                {
                    Group.logger.Info($"{this.GetType().Name} - Got enough yes votes.");                    
                    retVal = new StateInGroup(Group, newGroupId);                    
                } else if (joinVoteMachine.VoteIsGone(voteKey)) {
                   // Group.logger.Error("Error joining basic group: lost vote!");
                    //retVal = new StateListeningForGroup(Group);
                }
                return retVal;
            }

            public override void OnApianMsg(ApianMessage msg, string msgSrc, string msgChannel)
            {
                switch (msg.msgType)
                {
                case ApianMessage.kGroupJoinVote: // our own is in here as well
                    GroupJoinVoteMsg gv = (msg as GroupJoinVoteMsg);
                    if (gv.groupId == newGroupId && gv.peerId == Group.LocalP2pId)
                    {
                        Group.logger.Info($"{this.GetType().Name} - Got a {(gv.approve ? "yes" : "no")} join vote.");                        
                        if (joinVoteMachine.AddVote(voteKey, msgSrc, peerCnt))
                            voteWon = true;
                    }
                    break;
                }
            }
        }    

        protected class StateInGroup : LocalState
        {
            protected ApianVoteMachine<JoinVoteKey> joinVoteMachine;
            protected string groupId; 
            protected long groupAnnounceTimeoutMs;
            public StateInGroup(ApianBasicGroup group, string newGroupId) : base(group) 
            {
                groupId = newGroupId;
                joinVoteMachine = new ApianVoteMachine<JoinVoteKey>(Group.logger);
            }               
            public override void Start() 
            {
                Group.GroupId = groupId;
                Group.Members.Clear();
                Group.Members[Group.LocalP2pId] = new ApianMember(Group.LocalP2pId);
                Group.ListenToGroupChannel(groupId);
                Group.logger.Info($"{this.GetType().Name} - Joined group: {groupId}"); 
                groupAnnounceTimeoutMs = 0; //              
            }

            private long SetGroupAnnounceTimeout() 
            {
                return Group.SysMs + kGroupAnnouceSendTimeoutMs - new Random().Next(kGroupAnnouceSendTimeoutMs/2);
            }

            public override LocalState Update()
            {
                if (groupAnnounceTimeoutMs > 0 && Group.SysMs > groupAnnounceTimeoutMs) 
                {
                    // Need to send a group announcement?
                    Group.logger.Info($"{this.GetType().Name} - Timeout: announcing group.");                    
                    Group.AnnounceGroup(groupId, Group.Members.Count);
                    groupAnnounceTimeoutMs = 0;                    
                }
                return this;
            }

            public override void OnApianMsg(ApianMessage msg, string msgSrc, string msgChannel)
            {
                switch (msg.msgType)
                {
                case ApianMessage.kRequestGroups:
                    Group.logger.Info($"{this.GetType().Name} - Received an group request.");                
                    groupAnnounceTimeoutMs = SetGroupAnnounceTimeout(); // reset the timer
                    break;
                case ApianMessage.kGroupAnnounce:
                    GroupAnnounceMsg ga = (msg as GroupAnnounceMsg);
                    if (ga.groupId != groupId)  
                    {
                        Group.logger.Info($"{this.GetType().Name} - Received an anouncement for this group.");
                        groupAnnounceTimeoutMs = 0; // cancel any send (someone else sent it)
                    }
                    break;
                case ApianMessage.kGroupJoinReq:
                    GroupJoinRequestMsg gr = (msg as GroupJoinRequestMsg);
                    Group.logger.Info($"{this.GetType().Name} - Gote a join req for gid: {gr.groupId}");
                    if (gr.groupId == groupId)  
                    {
                        Group.logger.Info($"{this.GetType().Name} - Received a request to join this group. Voting yes.");
                        Group.VoteOnJoinReq(groupId, gr.peerId, true);
                    }
                    break;  
                case ApianMessage.kGroupJoinVote: // our own is in here as well
                    GroupJoinVoteMsg gv = (msg as GroupJoinVoteMsg);
                    if (gv.groupId == groupId)
                    {
                        Group.logger.Info($"{this.GetType().Name} - Got a {(gv.approve ? "yes" : "no")} join vote for {gv.peerId}");                        
                        bool won = joinVoteMachine.AddVote(new JoinVoteKey(gv.peerId), msgSrc, Group.Members.Count);
                        if (won)
                        {
                            Group.Members[gv.peerId] = new ApianMember(gv.peerId);
                            Group.logger.Info($"{this.GetType().Name} - Added {gv.peerId} to group");                            
                        }
                    }
                    break;                  
                }
            }  

        }

        // - - - - - - - - - - - - -
        //

        public ApianBase ApianInst {get; private set;}
        public string GroupId {get; private set;}
        public string MainChannel {get; private set;}
        public string LocalP2pId {get; private set;} // need this? Or should we have a localMember reference?
        public Dictionary<string, ApianMember> Members {get; private set;}
        public UniLogger logger;

        protected LocalState currentState;

        protected long SysMs { get => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;}

        public ApianBasicGroup(ApianBase _apianInst, string _mainChannel, string _localP2pId)
        {
            logger = UniLogger.GetLogger("ApianGroup");            
            ApianInst = _apianInst;
            MainChannel = _mainChannel;
            LocalP2pId = _localP2pId;
            GroupId = null;
            Members = new Dictionary<string, ApianMember>();

         }

        protected void InitState()
        {
            currentState = new StateListeningForGroup(this);
            currentState.Start(); // dont want to xcall state methods in group ctor   
        }

        public void Update()
        {
            if (currentState == null)
                InitState();

            LocalState newState = currentState?.Update();
            if (newState != null && newState != currentState)
            {
                currentState = newState;
                currentState.Start();
            }
        }

        public void OnApianMsg(ApianMessage msg, string msgSrc, string msgChan) {
            // Dispatch to current state
            currentState?.OnApianMsg(msg, msgSrc, msgChan);
        }

        protected void RequestGroups() 
        {
            ApianInst.SendApianMessage(MainChannel, new RequestGroupsMsg());
        }

        protected void AnnounceGroup(string groupId, int memberCnt) 
        {
            ApianInst.SendApianMessage(MainChannel, new GroupAnnounceMsg(groupId, memberCnt));
        }
        protected void RequestToJoinGroup(string groupId) 
        {
            ApianInst.SendApianMessage(MainChannel, new GroupJoinRequestMsg(groupId, LocalP2pId));
        }        

        protected void VoteOnJoinReq(string groupId, string peerid, bool vote) 
        {
            ApianInst.SendApianMessage(MainChannel, new GroupJoinVoteMsg(groupId, peerid, vote));
        }  
        protected void ListenToGroupChannel(string groupChannel)
        {
            ApianInst.AddGroupChannel(groupChannel);
        }


    }
}