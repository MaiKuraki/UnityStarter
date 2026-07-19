using System.IO;

namespace CycloneGames.IO
{
    internal sealed class SystemAtomicFileOperations : IAtomicFileOperations
    {
        internal static readonly SystemAtomicFileOperations Instance = new SystemAtomicFileOperations();

        private SystemAtomicFileOperations()
        {
        }

        public bool Exists(string path)
        {
            return File.Exists(path);
        }

        public void Move(string sourcePath, string destinationPath)
        {
            File.Move(sourcePath, destinationPath);
        }

        public void Replace(string sourcePath, string destinationPath)
        {
            File.Replace(sourcePath, destinationPath, null);
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }
    }
}
