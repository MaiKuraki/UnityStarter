using NUnit.Framework;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.UIFramework.Runtime.Editor;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class TemplatePathBuilderTests
    {
        [Test]
        public void TemplateUIPathBuilder_BuildsStableWindowConfigPath()
        {
            var builder = new TemplateUIPathBuilder();

            string path = builder.GetAssetPath("UIWindow_Inventory_Config");

            Assert.AreEqual("Assets/_DEVELOPER/ScriptableObject/UI/Window/UIWindow_Inventory_Config.asset", path);
        }

        [Test]
        public void TemplateUIPathBuilder_ReturnsEmptyPathForInvalidWindowName()
        {
            var builder = new TemplateUIPathBuilder();

            Assert.AreEqual(string.Empty, builder.GetAssetPath(null));
            Assert.AreEqual(string.Empty, builder.GetAssetPath(string.Empty));
        }

        [Test]
        public void TemplateAssetPathBuilderFactory_ReturnsUiBuilderOnlyForKnownType()
        {
            var factory = new TemplateAssetPathBuilderFactory();

            IAssetPathBuilder builder = factory.Create("UI");

            Assert.IsInstanceOf<TemplateUIPathBuilder>(builder);
            Assert.IsNull(factory.Create("Audio"));
        }
    }
}
