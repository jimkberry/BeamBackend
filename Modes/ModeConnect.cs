using GameModeMgr;

namespace BeamBackend
{
    public class ModeConnect : BaseGameMode
    {
        public BeamGameInstance game = null; 
        public BeamUserSettings settings = null;     

		public override void Start(object param = null)	
        {
            UnityEngine.Debug.Log("Starting Connect");
            base.Start();
            game = (BeamGameInstance)gameInst;
            settings = game.frontend.GetUserSettings();

            game.ClearPlayers();
            game.ClearBikes();    
            game.ClearPlaces();     

            game.frontend.ModeHelper()
                .OnStartMode(BeamModeFactory.kConnect, null );             
        }


    }
}