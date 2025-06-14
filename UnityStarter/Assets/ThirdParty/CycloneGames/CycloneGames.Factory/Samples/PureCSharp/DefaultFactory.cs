using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.PureCSharp
{
    public class DefaultFactory<T> : IFactory<T> where T : new()
    {
        public T Create() => new T();
    }
}
