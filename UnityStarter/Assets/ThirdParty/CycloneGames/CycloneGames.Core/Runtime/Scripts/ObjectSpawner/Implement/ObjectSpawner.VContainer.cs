#if VCONTAINER  // If you are using VContainer, please copy the contents of this file into your own project, 
                // remove the #if VCONTAINER directive, and recompile the script.

using VContainer;
namespace CycloneGames.Core
{
    public class VContainerObjectSpawner : IObjectSpawner
    {
        [Inject] IObjectResolver objectResolver;
        public UnityEngine.Object SpawnObject(UnityEngine.Object original)
        {
            var obj = UnityEngine.Object.Instantiate(original);
            objectResolver.Inject(obj);
            return obj;
        }

        public UnityEngine.Object SpawnObject<T>(T original) where T : UnityEngine.Object
        {
            var obj = UnityEngine.Object.Instantiate(original);
            objectResolver.Inject(obj);
            return obj;
        }

        public UnityEngine.GameObject SpawnObjectOnNewGameObject<TComponent>(string name = "") where TComponent : UnityEngine.Component
        {
            var go = new UnityEngine.GameObject(name);
            var component = go.AddComponent<TComponent>();
            objectResolver.Inject(component);
            return go;
        }
    }
}

#endif  // If you are using VContainer, please copy the contents of this file into your own project, 
// remove the #if VCONTAINER directive, and recompile the script.