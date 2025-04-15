namespace CycloneGames.Factory
{
    public interface IFactory { }
    public interface IFactory<out TValue> : IFactory { TValue Create(); }
    public interface IFactory<in TArg, out TValue> : IFactory { TValue Create(TArg arg); }
    public interface IUnityObjectSpawner : IFactory { T Create<T>(T origin) where T : UnityEngine.Object; }
}