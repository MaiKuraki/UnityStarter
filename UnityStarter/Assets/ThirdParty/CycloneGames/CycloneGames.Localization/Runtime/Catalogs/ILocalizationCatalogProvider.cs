using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Localization.Runtime
{
    public interface ILocalizationCatalogProvider
    {
        UniTask<LocalizationCatalog> LoadCatalogAsync(CancellationToken cancellationToken = default);
    }
}
