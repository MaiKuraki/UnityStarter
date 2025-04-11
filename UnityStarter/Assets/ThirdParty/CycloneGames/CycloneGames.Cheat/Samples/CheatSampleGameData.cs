using System;

namespace CycloneGames.Cheat.Sample
{
    [Serializable]
    public struct GameData
    {
        public UnityEngine.Vector3 position { get; private set; }
        public UnityEngine.Vector3 rotation { get; private set; }
        public GameData(UnityEngine.Vector3 position, UnityEngine.Vector3 rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }
    }
}
