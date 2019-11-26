using System;
using System.Collections.Generic;
using GameModeMgr;

namespace BeamBackend
{
    public class BeamModeFactory : ModeFactory
    {
        public static int kStartup = 0;
        public static int kConnect = 1;
        public const int kSplash = 2;
        public const int kPlay = 3;

        public BeamModeFactory()
        {
            modeFactories =  new Dictionary<int, Func<IGameMode>>  {
                { kStartup, ()=> new ModeStartup() },
                { kConnect, ()=> new ModeConnect() },
                { kSplash, ()=> new ModeSplash() },
                { kPlay, ()=> new ModePlay() },
            };
        }
    }
}