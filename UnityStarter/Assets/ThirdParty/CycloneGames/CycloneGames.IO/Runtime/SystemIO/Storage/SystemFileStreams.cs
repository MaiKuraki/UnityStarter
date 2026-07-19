using System.IO;

namespace CycloneGames.IO
{
    internal static class SystemFileStreams
    {
        private const int FILE_STREAM_BUFFER_SIZE = 4096;

        internal static FileStream OpenRead(string path, bool asynchronous)
        {
            FileOptions options = FileOptions.SequentialScan;
            if (asynchronous)
            {
                options |= FileOptions.Asynchronous;
            }

            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FILE_STREAM_BUFFER_SIZE,
                options);
        }

        internal static FileStream CreateWrite(string path, bool asynchronous)
        {
            EnsureParentDirectory(path);
            FileOptions options = FileOptions.SequentialScan;
            if (asynchronous)
            {
                options |= FileOptions.Asynchronous;
            }

            return new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                FILE_STREAM_BUFFER_SIZE,
                options);
        }

        internal static FileStream OpenAppend(string path)
        {
            EnsureParentDirectory(path);
            return new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                FILE_STREAM_BUFFER_SIZE,
                FileOptions.SequentialScan);
        }

        private static void EnsureParentDirectory(string path)
        {
            string directoryPath = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
    }
}
