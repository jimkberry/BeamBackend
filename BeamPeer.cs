using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace BeamBackend
{
    public class BeamPeer
    {
        public string PeerId { get; private set;}    
        public string Name { get; private set;}

        public BeamPeer(string peerId, string name)
        { 
            PeerId = peerId;
            Name = name;
        }

        public static BeamPeer FromDataString(string jsonData)
        {
            object[] data = JsonConvert.DeserializeObject<object[]>(jsonData);
            return new BeamPeer(
                data[0] as string, 
                data[1] as string);
        }

        public string ApianSerialized()
        {
            return  JsonConvert.SerializeObject(new object[]{
                PeerId,
                Name });
        }

    }
}
