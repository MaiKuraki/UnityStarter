namespace CycloneGames.AssetManagement.Integrations.Common
{
	/// <summary>
	/// Minimal service locator for cases where DI is not available.
	/// Assign once during boot and other integrations can consume the default package.
	/// </summary>
	public static class AssetManagementLocator
	{
		public static IAssetPackage DefaultPackage { get; set; }
	}
}