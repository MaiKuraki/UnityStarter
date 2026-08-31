using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Guards the cached IMGUI resources in <see cref="AtlasPipelineUi"/>.
    /// The window's zero-allocation refactor moved every GUIContent into static readonly fields
    /// that are built by static methods. C# runs static field initializers in textual order, so a
    /// field that is built from another field must be declared after it. When that order is
    /// broken the class initializer throws, Unity swallows the exception inside OnGUI and the
    /// window renders an empty panel — a failure the compiler cannot see. These tests force the
    /// class to initialize and assert every cached entry is actually populated.
    /// </summary>
    [TestFixture]
    public sealed class AtlasPipelineUiCacheTests
    {
        /// <summary>
        /// Category for assertions that need a real editor GUI context. Exclude it when running
        /// the suite outside the Unity editor.
        /// </summary>
        public const string RequiresEditorGui = "RequiresEditorGui";

        [Test]
        public void ClassInitializer_LeavesNoNullEntries()
        {
            // A dependency declared below its consumer throws here as TypeInitializationException,
            // which is exactly the regression this test exists to prevent.
            RuntimeHelpers.RunClassConstructor(typeof(AtlasPipelineUi).TypeHandle);

            FieldInfo[] fields = typeof(AtlasPipelineUi).GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            int checkedEntries = 0;
            foreach (FieldInfo field in fields)
            {
                if (field.FieldType == typeof(GUIContent))
                {
                    var content = (GUIContent)field.GetValue(null);
                    Assert.NotNull(content, $"AtlasPipelineUi.{field.Name} was never built.");
                    checkedEntries++;
                }
                else if (field.FieldType == typeof(GUIContent[]))
                {
                    var contents = (GUIContent[])field.GetValue(null);
                    Assert.NotNull(contents, $"AtlasPipelineUi.{field.Name} was never built.");
                    Assert.IsNotEmpty(contents, $"AtlasPipelineUi.{field.Name} is empty.");
                    for (int i = 0; i < contents.Length; i++)
                    {
                        Assert.NotNull(
                            contents[i],
                            $"AtlasPipelineUi.{field.Name}[{i}] is null.");
                    }

                    checkedEntries++;
                }
            }

            Assert.Greater(
                checkedEntries,
                0,
                "No GUIContent fields were found — the reflection filter no longer matches the "
                + "cache layout, so this test silently stopped guarding anything.");
        }

        [Test]
        public void SizePopupOptions_InheritEntryPrecedesSharedSizes()
        {
            Assert.AreEqual(
                AtlasPipelineUi.SizeInheritOption,
                AtlasPipelineUi.SizePopupOptions[0],
                "Index 0 must be the Inherit entry: the window maps index 0 to 'no override'.");

            Assert.AreEqual(
                AtlasPipelineUi.SizeOptions.Length + 1,
                AtlasPipelineUi.SizePopupOptions.Length,
                "The popup is Inherit plus one entry per shared size.");

            for (int i = 0; i < AtlasPipelineUi.SizeOptions.Length; i++)
            {
                Assert.AreEqual(
                    AtlasPipelineUi.SizeOptions[i],
                    AtlasPipelineUi.SizePopupOptions[i + 1],
                    "Popup entries must mirror SizeOptions one slot after Inherit.");
                Assert.AreEqual(
                    AtlasPipelineUi.SizeValues[i].ToString(),
                    AtlasPipelineUi.SizeOptions[i].text,
                    "SizeOptions must stay index-aligned with SizeValues; the window converts "
                    + "popup index back to a size through SizeValues.");
            }
        }

        [Test]
        public void GetFormatOptions_CoversEveryPlatform()
        {
            foreach (AtlasPlatform platform in System.Enum.GetValues(typeof(AtlasPlatform)))
            {
                GUIContent[] options = AtlasPipelineUi.GetFormatOptions(platform);
                Assert.NotNull(options, $"No format options for {platform}.");
                Assert.IsNotEmpty(options, $"Empty format options for {platform}.");
                foreach (GUIContent option in options)
                {
                    Assert.NotNull(option, $"Null format entry for {platform}.");
                    Assert.IsNotEmpty(
                        option.text,
                        $"Blank format entry for {platform} — the popup would show an empty row.");
                }
            }
        }

        /// <summary>
        /// Tagged because it reads <c>EditorStyles.helpBox</c>, which needs a live editor GUI
        /// context. It runs in the Unity Test Runner and is excluded from headless harnesses.
        /// </summary>
        [Test]
        [Category(RequiresEditorGui)]
        public void RichHelpBoxStyle_EnablesRichText()
        {
            // EditorStyles.helpBox ships with richText = false, which renders <b> markup literally.
            Assert.IsTrue(
                AtlasPipelineUi.RichHelpBoxStyle.richText,
                "Guidance blocks use <b> markup; the style must render it.");
            Assert.IsTrue(
                AtlasPipelineUi.RichHelpBoxStyle.wordWrap,
                "Guidance text is several lines long and must wrap inside the panel width.");
        }
    }
}
