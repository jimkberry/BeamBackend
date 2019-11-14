using System;
using System.Collections.Generic;
using GameModeMgr;

namespace BeamBackend
{
    public class BeamModeFactory : ModeFactory
    {
        public static int kStartup = 0;
        public const int kSplash = 1;        
        public const int kPlay = 2;

        public BeamModeFactory()
        {      
            modeFactories =  new Dictionary<int, Func<IGameMode>>  {  
                { kStartup, ()=> new ModeStartup() },  
                { kSplash, ()=> new ModeSplash() },                 
                { kPlay, ()=> new ModePlay() },                      
            }; 
        }       
    }
}