using System;
using System.Collections.Generic;

using CycloneGames.Utility.Editor;
using CycloneGames.Utility.Runtime;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Tests.Editor
{
    public sealed class PropertyGroupInspectorTests
    {
        [Test]
        public void LayoutCursor_UsesContinuousSegmentsAndEndBeforeField()
        {
            PropertyGroupTypeMetadata metadata = PropertyGroupMetadataCache.Get(typeof(PropertyGroupTestComponent));
            PropertyGroupLayoutCursor cursor = default;

            PropertyGroupLayoutInstruction baseStart = MoveNext(ref cursor, metadata, "BaseStart");
            Assert.That(baseStart.BeginGroup, Is.True);
            Assert.That(baseStart.DrawInsideGroup, Is.True);
            Assert.That(baseStart.CloseGroupAfterProperty, Is.False);
            Assert.That(baseStart.Group.GroupName, Is.EqualTo("Base"));

            PropertyGroupLayoutInstruction baseContinuation = MoveNext(ref cursor, metadata, "BaseContinuation");
            Assert.That(baseContinuation.BeginGroup, Is.False);
            Assert.That(baseContinuation.DrawInsideGroup, Is.True);

            PropertyGroupLayoutInstruction baseEnd = MoveNext(ref cursor, metadata, "BaseEnd");
            Assert.That(baseEnd.EndPreviousGroup, Is.True);
            Assert.That(baseEnd.DrawInsideGroup, Is.False);

            PropertyGroupLayoutInstruction solo = MoveNext(ref cursor, metadata, "Solo");
            Assert.That(solo.BeginGroup, Is.True);
            Assert.That(solo.DrawInsideGroup, Is.True);
            Assert.That(solo.CloseGroupAfterProperty, Is.True);

            PropertyGroupLayoutInstruction ungrouped = MoveNext(ref cursor, metadata, "Ungrouped");
            Assert.That(ungrouped.EndPreviousGroup, Is.False);
            Assert.That(ungrouped.DrawInsideGroup, Is.False);
        }

        [Test]
        public void LayoutCursor_DoesNotMergeNonContiguousGroupsWithTheSameName()
        {
            PropertyGroupTypeMetadata metadata = PropertyGroupMetadataCache.Get(typeof(PropertyGroupTestComponent));
            PropertyGroupLayoutCursor cursor = default;

            PropertyGroupLayoutInstruction first = MoveNext(ref cursor, metadata, "RepeatedStart");
            PropertyGroupLayoutInstruction firstEnd = MoveNext(ref cursor, metadata, "RepeatedEnd");
            PropertyGroupLayoutInstruction second = MoveNext(ref cursor, metadata, "RepeatedAgain");

            Assert.That(first.BeginGroup, Is.True);
            Assert.That(first.Group.GroupName, Is.EqualTo("Repeated"));
            Assert.That(firstEnd.EndPreviousGroup, Is.True);
            Assert.That(second.BeginGroup, Is.True);
            Assert.That(second.Group.GroupName, Is.EqualTo("Repeated"));
            Assert.That(first.Group, Is.Not.SameAs(second.Group));
        }

        [Test]
        public void InvalidMetadata_ClosesPreviousGroupAndNeverHidesTheField()
        {
            PropertyGroupTypeMetadata metadata = PropertyGroupMetadataCache.Get(typeof(PropertyGroupTestComponent));
            PropertyGroupLayoutCursor cursor = default;

            MoveNext(ref cursor, metadata, "RepeatedStart");
            PropertyGroupLayoutInstruction emptyName = MoveNext(ref cursor, metadata, "EmptyName");
            Assert.That(emptyName.EndPreviousGroup, Is.True);
            Assert.That(emptyName.BeginGroup, Is.False);
            Assert.That(emptyName.DrawInsideGroup, Is.False);
            Assert.That(emptyName.ValidationMessage, Is.Not.Empty);

            PropertyGroupLayoutInstruction conflicting = MoveNext(ref cursor, metadata, "ConflictingAttributes");
            Assert.That(conflicting.BeginGroup, Is.False);
            Assert.That(conflicting.DrawInsideGroup, Is.False);
            Assert.That(conflicting.ValidationMessage, Is.Not.Empty);
        }

        [Test]
        public void Metadata_RecognizesInheritedAndWholeNestedPropertiesAndIsCached()
        {
            PropertyGroupTypeMetadata first = PropertyGroupMetadataCache.Get(typeof(PropertyGroupTestComponent));
            PropertyGroupTypeMetadata second = PropertyGroupMetadataCache.Get(typeof(PropertyGroupTestComponent));

            Assert.That(second, Is.SameAs(first));
            Assert.That(first.TryGetField("BaseStart", out _), Is.True);
            Assert.That(first.TryGetField("NestedSettings", out PropertyGroupFieldMetadata nested), Is.True);
            Assert.That(nested.Group.GroupName, Is.EqualTo("Nested"));
            Assert.That(first.TryGetField("SettingsList", out PropertyGroupFieldMetadata list), Is.True);
            Assert.That(list.Group.GroupName, Is.EqualTo("List"));
            Assert.That(first.TryGetField("NestedGroupedValue", out _), Is.False);
        }

        [Test]
        public void ExplicitEditors_SupportComponentAssetAndMultiObjectTargetsWithoutGlobalFallback()
        {
            GameObject firstOwner = new GameObject("Property Group First");
            GameObject secondOwner = new GameObject("Property Group Second");
            PropertyGroupTestAsset asset = ScriptableObject.CreateInstance<PropertyGroupTestAsset>();
            UnityEditor.Editor componentEditor = null;
            UnityEditor.Editor assetEditor = null;
            UnityEditor.Editor unrelatedEditor = null;

            try
            {
                PropertyGroupTestComponent first = firstOwner.AddComponent<PropertyGroupTestComponent>();
                PropertyGroupTestComponent second = secondOwner.AddComponent<PropertyGroupTestComponent>();
                UnrelatedPropertyGroupTestComponent unrelated = firstOwner.AddComponent<UnrelatedPropertyGroupTestComponent>();

                componentEditor = UnityEditor.Editor.CreateEditor(new UnityEngine.Object[] { first, second });
                assetEditor = UnityEditor.Editor.CreateEditor(asset);
                unrelatedEditor = UnityEditor.Editor.CreateEditor(unrelated);

                Assert.That(componentEditor, Is.TypeOf<PropertyGroupTestComponentEditor>());
                Assert.That(assetEditor, Is.TypeOf<PropertyGroupTestAssetEditor>());
                Assert.That(unrelatedEditor, Is.Not.InstanceOf<PropertyGroupInspectorDrawer>());
            }
            finally
            {
                if (componentEditor != null)
                {
                    UnityEngine.Object.DestroyImmediate(componentEditor);
                }

                if (assetEditor != null)
                {
                    UnityEngine.Object.DestroyImmediate(assetEditor);
                }

                if (unrelatedEditor != null)
                {
                    UnityEngine.Object.DestroyImmediate(unrelatedEditor);
                }

                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(firstOwner);
                UnityEngine.Object.DestroyImmediate(secondOwner);
            }
        }

        private static PropertyGroupLayoutInstruction MoveNext(
            ref PropertyGroupLayoutCursor cursor,
            PropertyGroupTypeMetadata metadata,
            string propertyName)
        {
            metadata.TryGetField(propertyName, out PropertyGroupFieldMetadata fieldMetadata);
            return cursor.MoveNext(fieldMetadata);
        }
    }

    [Serializable]
    public sealed class PropertyGroupNestedTestSettings
    {
        [PropertyGroup("Ignored Nested Group")]
        public int NestedGroupedValue;
    }

    public class PropertyGroupTestBaseComponent : MonoBehaviour
    {
        [PropertyGroup("Base", true, 24, true)]
        [SerializeField] private int BaseStart;
        [SerializeField] private int BaseContinuation;
        [EndPropertyGroup]
        [SerializeField] private int BaseEnd;
    }

    [AddComponentMenu("")]
    public sealed class PropertyGroupTestComponent : PropertyGroupTestBaseComponent
    {
        [PropertyGroup("Solo")]
        [SerializeField] private int Solo;
        [SerializeField] private int Ungrouped;

        [PropertyGroup("Repeated", true)]
        [SerializeField] private int RepeatedStart;
        [EndPropertyGroup]
        [SerializeField] private int RepeatedEnd;
        [PropertyGroup("Repeated")]
        [SerializeField] private int RepeatedAgain;

        [PropertyGroup(null, true)]
        [SerializeField] private int EmptyName;
        [PropertyGroup("Invalid")]
        [EndPropertyGroup]
        [SerializeField] private int ConflictingAttributes;

        [PropertyGroup("Nested")]
        [SerializeField] private PropertyGroupNestedTestSettings NestedSettings = new PropertyGroupNestedTestSettings();
        [PropertyGroup("List")]
        [SerializeField] private List<PropertyGroupNestedTestSettings> SettingsList =
            new List<PropertyGroupNestedTestSettings>();
    }

    [AddComponentMenu("")]
    public sealed class UnrelatedPropertyGroupTestComponent : MonoBehaviour
    {
        [SerializeField] private int Value;
    }

    public sealed class PropertyGroupTestAsset : ScriptableObject
    {
        [PropertyGroup("Asset", true)]
        [SerializeField] private int First;
        [SerializeField] private int Second;
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(PropertyGroupTestComponent))]
    public sealed class PropertyGroupTestComponentEditor : PropertyGroupInspectorDrawer
    {
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(PropertyGroupTestAsset))]
    public sealed class PropertyGroupTestAssetEditor : PropertyGroupInspectorDrawer
    {
    }
}
