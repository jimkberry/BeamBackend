using GameModeMgr;
using UniLog;
namespace BeamBackend
{
    public class BeamCore : IGameInstance
    {

        public ModeManager modeMgr {get; private set;}
        public  IBeamGameNet gameNet {get; private set;}
        public UniLogger logger;
        public BeamGameInstance mainGameInst {get; private set;}

        public BeamCore(BeamGameNet bgn)
        {
            gameNet = bgn;
            logger = UniLogger.GetLogger("BeamBackendInstance");
            modeMgr = new ModeManager(new BeamModeFactory(), this);
        }

        public void SetGameInstance(BeamGameInstance gi)
        {
            mainGameInst = gi;
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
            return modeMgr.Loop(frameSecs); // TODO: I THINK this is OK. manager code can't change instance state
        }


    }
}