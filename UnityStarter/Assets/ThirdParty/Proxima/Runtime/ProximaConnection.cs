using System.IO;
using System;

namespace Proxima
{
    internal interface ProximaConnection
    {
        bool Open { get; }
        void SendMessage(MemoryStream data);
    }
}