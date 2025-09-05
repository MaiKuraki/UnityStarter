namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// A simple service locator for providing a default, globally accessible IAssetPackage.
    /// This is useful for systems that need to access asset loading without direct dependency injection.
    /// </summary>
    public static class AssetManagementLocator
    {
        /// <summary>
        /// Gets or sets the default asset package for the application.
        /// This should be set during your application's initialization sequence.
        /// </summary>
        public static IAssetPackage DefaultPackage { get; set; }
    }
}