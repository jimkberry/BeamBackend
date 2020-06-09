using System.Collections.Generic;
using Newtonsoft.Json;
using Apian;
using UnityEngine;

namespace BeamBackend
{
    public class BeamPlace : IApianStateData
    {
        public static long kLifeTimeMs = 15000; // TODO: Maybe should be per-bike and increase with time?

        public int xIdx; // x index into array.
        public int zIdx;
        public IBike bike;
        public long expirationTimeMs;

        public class SerialArgs
        {
            public Dictionary<string,int> bikeIdxDict;
            public SerialArgs(Dictionary<string,int> bid) {bikeIdxDict=bid;}
        };
        public string ApianSerialized(object args)
        {
            SerialArgs sArgs = args as SerialArgs;
            // args.bikeIdxDict is a dictionary to map bikeIds to array indices in the Json for the bikes
            // It makes this Json a lot smaller

            return  JsonConvert.SerializeObject(new object[]{
                sArgs.bikeIdxDict[bike.bikeId],
                xIdx,
                zIdx,
                expirationTimeMs
                });
        }

        public int PosHash { get => xIdx + zIdx * Ground.pointsPerAxis; }

        public static int MakePosHash(int xIdx, int zIdx) => xIdx + zIdx * Ground.pointsPerAxis;

        public Vector2 GetPos()
        {
            return PlacePos(xIdx,zIdx);
        }

        public static Vector2 PlacePos(int x, int z)
        {
            return new Vector2(x*Ground.gridSize+Ground.minX,z*Ground.gridSize+Ground.minZ);
        }
    }
}
