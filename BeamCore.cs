using GameModeMgr;
using UniLog;
namespace BeamBackend
{
    public class BeamCore : IGameInstance // NOT a BeamGameInstance
    {

        public ModeManager modeMgr {get; private set;}
        public  IBeamGameNet gameNet {get; private set;}
        public IBeamFrontend frontend {get; private set;}
        public UniLogger logger;
        public BeamGameInstance mainGameInst {get; private set;}

        public BeamCore(BeamGameNet bgn, IBeamFrontend fe)
        {
            gameNet = bgn;
            frontend = fe;
            logger = UniLogger.GetLogger("BeamBackendInstance");
            modeMgr = new ModeManager(new BeamModeFactory(), this);
        }

        public void SetGameInstance(BeamGameInstance gi)
        {
            mainGameInst = gi;
            frontend.SetGameInstance(gi);
        }

        //
        // IGameInstance
        //
        public void Start(int initialMode)
        {
            modeMgr.Start(initialMode);
        }

        public bool Loop(float frameSecs)
        {
            mainGameInst?.Loop(frameSecs);
            return modeMgr.Loop(frameSecs);
        }


    }
}