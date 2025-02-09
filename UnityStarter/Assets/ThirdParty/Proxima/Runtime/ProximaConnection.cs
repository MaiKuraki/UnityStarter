using System.IO;

namespace Proxima
{
    internal interface ProximaConnection
    {
        bool Open { get; }
        void SendMessage(MemoryStream data);
    }
}