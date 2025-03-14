namespace CycloneGames.Core
{
    /// <summary>
    /// Note:
    ///     This interface is designed to facilitate the integration of Unity with dependency injection (DI) frameworks,
    ///     such as VContainer or Zenject, by managing the registration of spawned objects within the DI framework.
    ///     
    ///     When using this interface with a DI framework, please note that the injected classes and services are valid 
    ///     starting from the MonoBehaviour's 'Start' method; they will be null in the 'Awake' method.
    /// </summary>
    public interface IObjectSpawner
    {
        UnityEngine.Object SpawnObject(UnityEngine.Object original);
        UnityEngine.Object SpawnObject<T>(T original) where T : UnityEngine.Object;
        UnityEngine.GameObject SpawnObjectOnNewGameObject<TComponent>(string name = "") where TComponent : UnityEngine.Component;
    }
}