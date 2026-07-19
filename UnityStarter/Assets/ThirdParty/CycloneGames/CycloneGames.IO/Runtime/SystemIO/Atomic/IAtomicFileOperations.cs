namespace CycloneGames.IO
{
    internal interface IAtomicFileOperations
    {
        bool Exists(string path);

        void Move(string sourcePath, string destinationPath);

        void Replace(string sourcePath, string destinationPath);

        void Delete(string path);
    }
}
