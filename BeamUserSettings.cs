using System.Runtime.Serialization;
using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using UniLog;
namespace BeamBackend
{
    public static class UserSettingsMgr
    {
        public const string currentVersion = "100";
        public const string subFolder = ".beam";  
        public const string fileName = "beamsettings.json";      
        public static string path;

        static UserSettingsMgr()
        {
            path = GetPath(subFolder);
        }

        public static BeamUserSettings Load()
        {
            BeamUserSettings settings;
            string filePath = path + Path.DirectorySeparatorChar + fileName;
            try {
                settings = JsonConvert.DeserializeObject<BeamUserSettings>(File.ReadAllText(filePath));
            } catch(Exception) { 
                settings =  BeamUserSettings.CreateDefault();
            }        

            // TODO: in real life this should do at least 1 version's worth of updating.
            if (settings.version != currentVersion)
            //  settings =  BeamUserSettings.CreateDefault();
                throw( new Exception($"Invalid settings version: {settings.version}"));

            return settings;   
        }

        public static void Save(BeamUserSettings settings)
        {
            System.IO.Directory.CreateDirectory(path); 
            string filePath = path + Path.DirectorySeparatorChar + fileName;            
            File.WriteAllText(filePath, JsonConvert.SerializeObject(settings, Formatting.Indented));            
        }

        public static string GetPath(string leafFolder)
        {
            string homePath = (Environment.OSVersion.Platform == PlatformID.Unix || 
                        Environment.OSVersion.Platform == PlatformID.MacOSX)
                        ? Environment.GetEnvironmentVariable("HOME")
                        : Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");        

            return homePath + Path.DirectorySeparatorChar + leafFolder;
        }

    }


    public class BeamUserSettings
    {
        public string version = UserSettingsMgr.currentVersion;
        public string gameId; // if null, create a game and join, otherwise just join 
        public string screenName;
        public string p2pConnectionString;
        public string ethNodeUrl;
        public string ethAcct;
        public Dictionary<string, string> debugLevels;

        public BeamUserSettings() {}

        public BeamUserSettings(BeamUserSettings source)
        {
            if (version != source.version)
                throw( new Exception($"Invalid settings version: {source.version}"));
            gameId = source.gameId;
            screenName = source.screenName;
            p2pConnectionString = source.p2pConnectionString;
            ethNodeUrl = source.ethNodeUrl;
            ethAcct = source.ethAcct; 
            debugLevels = source.debugLevels;        
        }

        public static BeamUserSettings CreateDefault()
        {
            return new BeamUserSettings() {
                version = UserSettingsMgr.currentVersion,
                gameId = null,
                screenName = "Fred Sanford",
                p2pConnectionString = "p2predis::192.168.1.195,password=sparky-redis79",
                ethNodeUrl = "https://rinkeby.infura.io",
                ethAcct = "0x2b42eBD222B5a1134e85D78613078740eE3Cc93D",
                debugLevels = new Dictionary<string, string>() {
                    {"P2pNet", UniLogger.LevelNames[UniLogger.Level.Warn]},
                    {"GameNet", UniLogger.LevelNames[UniLogger.Level.Warn]},
                    {"GameInstance", UniLogger.LevelNames[UniLogger.Level.Warn]},
                    {"BeamMode", UniLogger.LevelNames[UniLogger.Level.Warn]},                                          
                }
            };
        }
    }
}