using System.Runtime.Serialization;
using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UniLog;

namespace BeamBackend
{
    public static class UserSettingsMgr
    {
        public const string currentVersion = "100";
        public const string subFolder = ".beam";
        public const string defaultBaseName= "beamsettings";
        public static string fileBaseName;
        public static string path;

        static UserSettingsMgr()
        {
            path = GetPath(subFolder);
        }

        public static BeamUserSettings Load(string baseName = defaultBaseName)
        {
            fileBaseName = baseName;
            BeamUserSettings settings;
            string filePath = path + Path.DirectorySeparatorChar + fileBaseName + ".json";
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
            string filePath = path + Path.DirectorySeparatorChar + fileBaseName + ".json";
            BeamUserSettings saveSettings = new BeamUserSettings(settings);
            saveSettings.tempSettings = new Dictionary<string, string>(); // Don't persist temp settings
            File.WriteAllText(filePath, JsonConvert.SerializeObject(saveSettings, Formatting.Indented));
        }

        public static string GetPath(string leafFolder)
        {
#if UNITY_2019_1_OR_NEWER
            string homePath =  Application.persistentDataPath;

#else
            string homePath = (Environment.OSVersion.Platform == PlatformID.Unix ||
                        Environment.OSVersion.Platform == PlatformID.MacOSX)
                        ? Environment.GetEnvironmentVariable("HOME")
                        : Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
#endif
            UniLogger.GetLogger("UserSettings").Info($"User settings path: {homePath + Path.DirectorySeparatorChar + leafFolder}");
            return homePath + Path.DirectorySeparatorChar + leafFolder;
        }

    }


    public class BeamUserSettings
    {
        public string version = UserSettingsMgr.currentVersion;
        public int startMode;
        public string screenName;
        public string p2pConnectionString;
        public string ethNodeUrl;
        public string ethAcct;
        public string localPlayerCtrlType;
        public int aiBikeCount; // in addition to localPLayerBike, spawn this many AIs (and respawn to keep the number up)
        public bool regenerateAiBikes; // create new ones when old ones get blown up

        public string defaultLogLevel;

        public Dictionary<string, string> logLevels;
        public Dictionary<string, string> tempSettings; // dict of cli-set, non-peristent values

        public BeamUserSettings()
        {
            logLevels = new Dictionary<string, string>();
            tempSettings = new Dictionary<string, string>();
        }

        public BeamUserSettings(BeamUserSettings source)
        {
            if (version != source.version)
                throw( new Exception($"Invalid settings version: {source.version}"));
            startMode = source.startMode;
            screenName = source.screenName;
            p2pConnectionString = source.p2pConnectionString;
            ethNodeUrl = source.ethNodeUrl;
            ethAcct = source.ethAcct;
            localPlayerCtrlType = source.localPlayerCtrlType;
            aiBikeCount = source.aiBikeCount;
            regenerateAiBikes = source.regenerateAiBikes;
            defaultLogLevel = source.defaultLogLevel;
            logLevels = source.logLevels ?? new Dictionary<string, string>();
            tempSettings = source.tempSettings ?? new Dictionary<string, string>();
        }

        public static BeamUserSettings CreateDefault()
        {
            return new BeamUserSettings() {
                version = UserSettingsMgr.currentVersion,
                startMode = BeamModeFactory.kPlay,
                screenName = "Fred Sanford",
                p2pConnectionString = "p2predis::newsweasel.com,password=O98nfRVWYYHg7rXpygBCBZWl+znRATaRXTC469SafZU",
                //p2pConnectionString = "p2predis::192.168.1.195,password=sparky-redis79",
                ethNodeUrl = "https://rinkeby.infura.io/v3/7653fb1ed226443c98ce85d402299735",
                ethAcct = "0x2b42eBD222B5a1134e85D78613078740eE3Cc93D",
                localPlayerCtrlType = BikeFactory.AiCtrl,
                aiBikeCount = 2,
                regenerateAiBikes = false,
                defaultLogLevel = "Warn",
                logLevels = new Dictionary<string, string>() {
                    {"UserSettings", UniLogger.LevelNames[UniLogger.Level.Info]},
                    {"P2pNet", UniLogger.LevelNames[UniLogger.Level.Warn]},
                    {"GameNet", UniLogger.LevelNames[UniLogger.Level.Warn]},
                    {"GameInstance", UniLogger.LevelNames[UniLogger.Level.Warn]},
                    {"BeamMode", UniLogger.LevelNames[UniLogger.Level.Warn]},
                },
                tempSettings = new Dictionary<string, string>()
            };
        }
    }
}