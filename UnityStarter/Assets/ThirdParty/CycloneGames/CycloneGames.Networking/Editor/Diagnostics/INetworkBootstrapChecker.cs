namespace CycloneGames.Networking.Editor.Diagnostics
{
    internal interface INetworkBootstrapChecker
    {
        void Run(NetworkBootstrapContext context, NetworkBootstrapReport report);
    }
}
