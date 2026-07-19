using System;
using System.IO;

namespace CycloneGames.IO.Tests.SystemIO
{
    internal sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CycloneGames.IO.SystemIO.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string GetPath(string relativePath)
        {
            return System.IO.Path.Combine(Path, relativePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
