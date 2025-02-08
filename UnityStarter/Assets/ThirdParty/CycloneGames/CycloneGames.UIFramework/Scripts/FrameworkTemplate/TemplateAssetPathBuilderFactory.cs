#if UNITY_EDITOR
using CycloneGames.Service;

namespace CycloneGames.UIFramework
{
    /// <summary>
    /// This just only a example, you must define your path builder factory for your own project
    /// </summary>
    public class TemplateAssetPathBuilderFactory : IAssetPathBuilderFactory
    {
        public IAssetPathBuilder Create(string type)
        {
            switch (type)
            {
                case "UI":
                    return new TemplateUIPathBuilder();
            }
            return null;
        }
    }
}
#endif