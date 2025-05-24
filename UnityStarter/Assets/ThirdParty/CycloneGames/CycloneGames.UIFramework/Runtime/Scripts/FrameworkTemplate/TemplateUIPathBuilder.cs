#if UNITY_EDITOR
using CycloneGames.Service;

namespace CycloneGames.UIFramework.Editor // Or a runtime namespace
{
    /// <summary>
    /// This is an example UI path builder. Define path construction logic specific to your project's
    /// Addressables setup (e.g., using labels, specific group structures, or naming conventions).
    /// </summary>
    public class TemplateUIPathBuilder : IAssetPathBuilder
    {
        // Example: "Assets/Path/To/UI_WindowConfigs/{windowName}_Config.asset"
        // It's crucial that this matches how your UIWindowConfiguration assets are named and addressed.
        private const string UI_CONFIG_PATH_FORMAT = "Assets/_DEVELOPER/ScriptableObject/UI/Window/{0}.asset"; // Original format

        public string GetAssetPath(string windowName)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                UnityEngine.Debug.LogWarning("[TemplateUIPathBuilder] windowName is null or empty. Cannot generate asset path.");
                return string.Empty;
            }
            // Using string.Format for clarity, though interpolation is also fine here.
            return string.Format(UI_CONFIG_PATH_FORMAT, windowName);
        }
    }
}
#endif