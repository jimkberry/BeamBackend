using System;
using System.Collections.Generic;
using GameModeMgr;
using UniLog;

namespace BeamBackend
{
    public class BeamGameMode : IGameMode
    {
		public ModeManager manager; 
		public IGameInstance gameInst;
		public UniLogger logger;

		public void Setup(ModeManager mgr, IGameInstance gInst = null)
		{
			// Called by manager before Start()
			// Not virtual
			// TODO: this should be the engine and not the modeMgr - but what IS an engine...
			manager = mgr;
			gameInst = gInst;
			logger = UniLogger.GetLogger("BeamMode");
        }

		protected Dictionary<string, dynamic> _cmdDispatch; 

		public virtual void Start( object param = null)	{
            _cmdDispatch = new Dictionary<string, dynamic>();            
        }

		public virtual void Loop(float frameSecs) {}

		public virtual void Pause() {}
		public virtual void Resume(string prevModeName, object prevModeResult) {}	
		public virtual object End() => null;
        public virtual string ModeName() => this.GetType().Name;        

        public bool HandleCmd(object cmd)
        {
			try {			
            	return _cmdDispatch[((BeamMessage)cmd).msgType](cmd);
			} catch (KeyNotFoundException) {
				logger.Warn($"{this.ModeName()}: Unhandled Command: {((BeamMessage)cmd).msgType} {cmd.GetType()}");
				return false;
			}
        }    
    }
}