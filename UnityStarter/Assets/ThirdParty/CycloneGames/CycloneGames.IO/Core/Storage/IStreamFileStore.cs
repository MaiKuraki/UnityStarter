using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    public interface IStreamFileStore
    {
        Stream OpenRead(string path);

        Stream CreateWrite(string path);

        Stream OpenAppend(string path);

        Task<long> WriteStreamAsync(
            string path,
            Stream source,
            CancellationToken cancellationToken = default);

        Task<long> ReadStreamAsync(
            string path,
            Stream destination,
            CancellationToken cancellationToken = default);
    }
}
