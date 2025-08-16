#if NAVIGATHENA_PRESENT && NAVIGATHENA_YOOASSET
using MackySoft.Navigathena.SceneManagement;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Integrations.Navigathena
{
	/// <summary>
	/// Factory to create Navigathena scene identifiers backed by CycloneGames.AssetManagement (YooAsset).
	/// Configure <see cref="DefaultPackage"/> at startup.
	/// </summary>
	public static class NavigathenaYooSceneFactory
	{
		public static IAssetPackage DefaultPackage { get; set; }

		public static ISceneIdentifier Create(string location, LoadSceneMode mode = LoadSceneMode.Additive, bool activateOnLoad = true, int priority = 100)
		{
			if (DefaultPackage == null || string.IsNullOrEmpty(location)) return null;
			return new YooAssetSceneIdentifier(DefaultPackage, location, mode, activateOnLoad, priority);
		}
	}
}
#endif