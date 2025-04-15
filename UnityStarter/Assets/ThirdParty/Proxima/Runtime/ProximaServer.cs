namespace Proxima
{
    internal interface ProximaServer
    {
        void Start(string displayName, string password);
        void Stop();
        bool TryGetMessage(out (ProximaConnection, ProximaRequest) message);
    }
}