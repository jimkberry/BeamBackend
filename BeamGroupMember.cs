using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Apian;
using UnityEngine;

namespace BeamBackend
{
    public class BeamGroupMember : ApianClientMemberData
    {
        public string Name { get; private set;}

        public BeamGroupMember(string peerId, string name) : base(peerId)
        {
            Name = name;
        }

        public static BeamGroupMember FromApianSerialized(string jsonData)
        {
            object[] data = JsonConvert.DeserializeObject<object[]>(jsonData);
            return new BeamGroupMember(
                data[0] as string,
                data[1] as string);
        }

        public override string ApianSerialized()
        {
            return  JsonConvert.SerializeObject(new object[]{
                PeerId,
                Name });
        }

    }
}
