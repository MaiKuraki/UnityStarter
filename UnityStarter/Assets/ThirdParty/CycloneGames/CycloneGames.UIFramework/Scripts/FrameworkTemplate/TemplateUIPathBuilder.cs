#if UNITY_EDITOR
using CycloneGames.Service;

namespace CycloneGames.UIFramework
{
    /// <summary>
    /// This just only a example, you must define your path builder for your own project
    /// </summary>
    public class TemplateUIPathBuilder : IAssetPathBuilder
    {
        public string GetAssetPath(string pageName)
            => $"Assets/_DEVELOPER/ScriptableObject/UI/Page/{pageName}.asset";
    }
}
#endif