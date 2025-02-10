using CycloneGames.Core;

namespace CycloneGames.GameplayFramework.Example.PureUnity
{
    public class UnityExampleObjectSpawner : IObjectSpawner
    {
        public UnityEngine.Object SpawnObject(UnityEngine.Object original)
        {
            return UnityEngine.Object.Instantiate(original);
        }

        public UnityEngine.Object SpawnObject<T>(T original) where T : UnityEngine.Object
        {
            return UnityEngine.Object.Instantiate(original);
        }

        UnityEngine.GameObject IObjectSpawner.SpawnObjectOnNewGameObject<TComponent>(string name)
        {
            var go = new UnityEngine.GameObject(name);
            var component = go.AddComponent<TComponent>();
            return go;
        }
    }
}