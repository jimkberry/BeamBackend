namespace BeamBackend
{
    public class BeamUserSettings
    {
        public string screenName;
        public string p2pConnectionString;
        public string ethNodeUrl;
        public string ethAcct;

        public BeamUserSettings() {}
        public BeamUserSettings(BeamUserSettings source)
        {
            screenName = source.screenName;
            p2pConnectionString = source.p2pConnectionString;
            ethNodeUrl = source.ethNodeUrl;
            ethAcct = source.ethAcct;         
        }

        public static BeamUserSettings CreateDefault()
        {
            return new BeamUserSettings() {
                screenName = "Fred Sanford",
                p2pConnectionString = "p2predis::sparkyx,password=sparky-redis79",
                ethNodeUrl = "https://rinkeby.infura.io",
                ethAcct = "0x2b42eBD222B5a1134e85D78613078740eE3Cc93D"
            };
        }



    }
}