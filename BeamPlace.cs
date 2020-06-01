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

        public string ApianSerialized()
        {
            return  JsonConvert.SerializeObject(new object[]{
                bike.bikeId,
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
