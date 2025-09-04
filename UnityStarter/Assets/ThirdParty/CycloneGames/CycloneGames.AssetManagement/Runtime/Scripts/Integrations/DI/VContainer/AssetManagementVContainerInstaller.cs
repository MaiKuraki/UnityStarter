#if VCONTAINER_PRESENT && YOOASSET_PRESENT
using VContainer;
using VContainer.Unity;
using YooAsset;
using CycloneGames.Logger;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Integrations.DI.VContainer
{
	public class AssetManagementVContainerInstaller : LifetimeScope
	{
		[UnityEngine.Header("Asset Package Config")]
		public string PackageName = "Default";
		public AssetPlayMode PlayMode = AssetPlayMode.Host;
		public int BundleConcurrency = 8;

		/// <summary>
		/// Creates provider-specific parameters for YooAsset based on <see cref="PlayMode"/>.
		/// Override to supply custom parameters from scene context.
		/// </summary>
		protected virtual object CreateProviderParameters()
		{
			switch (PlayMode)
			{
				case AssetPlayMode.Host:
					return new HostPlayModeParameters
					{
						BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
						CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices: null)
					};
				case AssetPlayMode.Offline:
					return new OfflinePlayModeParameters
					{
						BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters()
					};
				case AssetPlayMode.EditorSimulate:
					return new EditorSimulateModeParameters
					{
						EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot: null)
					};
				case AssetPlayMode.Web:
					return new WebPlayModeParameters
					{
						WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters(),
						WebRemoteFileSystemParameters = FileSystemParameters.CreateDefaultWebRemoteFileSystemParameters(remoteServices: null)
					};
				case AssetPlayMode.Custom:
					return new CustomPlayModeParameters();
				default:
					return null;
			}
		}

		protected override void Configure(IContainerBuilder builder)
		{
			builder.Register<IAssetModule, YooAssetModule>(Lifetime.Singleton);
			builder.RegisterBuildCallback(resolver =>
			{
				_ = InitializeAsync(resolver);
				async System.Threading.Tasks.Task InitializeAsync(IObjectResolver r)
				{
					try
					{
						var module = r.Resolve<IAssetModule>();
						module.Initialize(new AssetModuleOptions(16, int.MaxValue, null, true));
						var pkg = module.CreatePackage(PackageName);
						var providerParams = CreateProviderParameters();
						var opts = new AssetPackageInitOptions(PlayMode, providerParams, BundleConcurrency);
						await pkg.InitializeAsync(opts);
					}
					catch (System.Exception ex)
					{
						CLogger.LogError($"[AssetManagementVContainerInstaller] Init failed: {ex}");
					}
				}
			});
		}
	}
}
#endif