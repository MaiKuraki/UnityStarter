namespace CycloneGames.Factory
{
    public interface IFactory { }
    public interface IFactory<out TValue> : IFactory { TValue Create(); }
    public interface IFactory<in TArg, out TValue> : IFactory { TValue Create(TArg arg); }
}