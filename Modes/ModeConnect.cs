using GameModeMgr;

namespace BeamBackend
{
    public class ModeConnect : BaseGameMode
    {
        public class ModeConnectParam
        {
            public string netGameId;
        }
        protected long preJoinWaitMs = 5000;
        public BeamGameInstance game = null;
        public ModeConnectParam parms;

		public override void Start(object param = null)
        {
            UnityEngine.Debug.Log("Connecting to net game");
            base.Start();
            parms = (ModeConnectParam)param;
            game = (BeamGameInstance)gameInst;
            game.ClearPlayers();
            game.ClearBikes();
            game.ClearPlaces();

            game.frontend.ModeHelper()
                .OnStartMode(BeamModeFactory.kConnect, null );
        }
        public override void Loop(float frameSecs) {
            preJoinWaitMs -= (long)frameSecs * 1000;
            if (preJoinWaitMs <= 0)
                DoConnected();
            else
            {

            }

        }

        protected void DoConnected()
        {
            UnityEngine.Debug.Log("Connected.");

        }
    }

}