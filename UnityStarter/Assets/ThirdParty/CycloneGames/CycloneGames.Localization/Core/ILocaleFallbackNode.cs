namespace CycloneGames.Localization.Core
{
    /// <summary>
    /// Pure fallback graph node contract used by locale adapters.
    /// </summary>
    /// <typeparam name="TNode">Concrete node type returned by fallback accessors.</typeparam>
    public interface ILocaleFallbackNode<out TNode> where TNode : class
    {
        LocaleId Id { get; }
        int FallbackCount { get; }
        TNode GetFallback(int index);
    }
}
