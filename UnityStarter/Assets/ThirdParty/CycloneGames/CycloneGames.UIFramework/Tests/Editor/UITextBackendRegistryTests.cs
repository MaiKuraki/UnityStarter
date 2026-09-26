using System;
using CycloneGames.UIFramework.Editor;
using NUnit.Framework;
#if CYCLONEGAMES_HAS_TEXTMESHPRO
using TMPro;
#endif

namespace CycloneGames.UIFramework.Tests.Editor
{
    /// <summary>
    /// Covers the text backend registry, which is the seam that lets a text component package other
    /// than TextMeshPro take part in editor tooling. Tests match against a local type hierarchy, so
    /// the seam is exercised on a project where TextMeshPro is absent; the one test that needs the
    /// real component is symbol gated.
    /// </summary>
    public sealed class UITextBackendRegistryTests
    {
        private class FakeTextBase
        {
        }

        private class FakeTextDerived : FakeTextBase
        {
        }

        private const string PrimaryId = "Tests.Primary";
        private const string SecondaryId = "Tests.Secondary";
        private const string OverrideId = "Tests.Override";

        [TearDown]
        public void TearDown()
        {
            // The registry is process-wide static state. Each test removes the ids it registered so
            // the next one starts from the built-in set alone.
            UITextBackendRegistry.Unregister(PrimaryId);
            UITextBackendRegistry.Unregister(SecondaryId);
            UITextBackendRegistry.Unregister(OverrideId);
        }

        private static UITextBackend Fake(string id, int priority, string baseTypeName = null)
        {
            return new UITextBackend(
                id: id,
                displayName: id,
                baseTypeName: baseTypeName ?? typeof(FakeTextBase).FullName,
                textFieldName: "m_text",
                priority: priority);
        }

        [Test]
        public void TextMeshPro_IsRegisteredAsABuiltInBackend()
        {
            Assert.That(UITextBackendRegistry.TextMeshPro.BaseTypeName, Is.EqualTo("TMPro.TMP_Text"));
            Assert.That(UITextBackendRegistry.TextMeshPro.TextFieldName, Is.EqualTo("m_text"));
            Assert.That(UITextBackendRegistry.TextMeshPro.TitleObjectNames, Contains.Item("Text (TMP)"));
            Assert.That(
                UITextBackendRegistry.TextMeshPro.Priority,
                Is.EqualTo(UITextBackendRegistry.DefaultPriority));
            Assert.That(UITextBackendRegistry.Backends, Contains.Item(UITextBackendRegistry.TextMeshPro));
        }

        [Test]
        public void Matching_WalksInheritanceChain()
        {
            UITextBackend backend = Fake(PrimaryId, 0);
            Assert.That(backend.Matches(typeof(FakeTextDerived)), Is.True);
            Assert.That(backend.Matches(typeof(FakeTextBase)), Is.True);
            Assert.That(backend.Matches(typeof(UITextBackendRegistryTests)), Is.False);
            Assert.That(backend.Matches(null), Is.False);
        }

        [Test]
        public void TryMatch_FindsBackendRegisteredForTheBaseType()
        {
            UITextBackendRegistry.Register(Fake(PrimaryId, 0));

            Assert.That(
                UITextBackendRegistry.TryMatch(typeof(FakeTextDerived), out UITextBackend matched),
                Is.True);
            Assert.That(matched.Id, Is.EqualTo(PrimaryId));
        }

        [Test]
        public void TryMatch_ReturnsFalseForAnUnownedType()
        {
            UITextBackendRegistry.Register(Fake(PrimaryId, 0));

            Assert.That(
                UITextBackendRegistry.TryMatch(typeof(UITextBackendRegistryTests), out UITextBackend matched),
                Is.False);
            Assert.That(matched, Is.Null);
            Assert.That(UITextBackendRegistry.TryMatch(null, out _), Is.False);
        }

        [Test]
        public void HigherPriorityBackend_WinsOverLowerPriorityRegardlessOfRegistrationOrder()
        {
            // Registration order across assemblies is not deterministic, so priority must decide.
            UITextBackendRegistry.Register(Fake(SecondaryId, -5));
            UITextBackendRegistry.Register(Fake(PrimaryId, 10));

            Assert.That(UITextBackendRegistry.Backends[0].Id, Is.EqualTo(PrimaryId));
            Assert.That(
                UITextBackendRegistry.TryMatch(typeof(FakeTextDerived), out UITextBackend matched),
                Is.True);
            Assert.That(matched.Id, Is.EqualTo(PrimaryId));
        }

        [Test]
        public void RegisteringSameId_ReplacesInsteadOfGrowing()
        {
            UITextBackendRegistry.Register(Fake(PrimaryId, 0));
            int countAfterFirst = UITextBackendRegistry.Backends.Count;

            UITextBackend replacement = new UITextBackend(
                id: PrimaryId,
                displayName: "Replacement",
                baseTypeName: typeof(FakeTextBase).FullName,
                textFieldName: "m_otherText",
                priority: 0);
            UITextBackendRegistry.Register(replacement);

            Assert.That(UITextBackendRegistry.Backends.Count, Is.EqualTo(countAfterFirst));
            Assert.That(
                UITextBackendRegistry.TryMatch(typeof(FakeTextDerived), out UITextBackend matched),
                Is.True);
            Assert.That(matched.TextFieldName, Is.EqualTo("m_otherText"));
        }

        [Test]
        public void RegisteringWithADifferentPriority_ReordersTheBackend()
        {
            UITextBackendRegistry.Register(Fake(PrimaryId, -10));
            UITextBackendRegistry.Register(Fake(PrimaryId, 10));

            Assert.That(UITextBackendRegistry.Backends[0].Id, Is.EqualTo(PrimaryId));
        }

        [Test]
        public void Unregister_RemovesTheBackend()
        {
            UITextBackendRegistry.Register(Fake(OverrideId, 10));
            Assert.That(
                UITextBackendRegistry.TryMatch(typeof(FakeTextDerived), out UITextBackend matched),
                Is.True);
            Assert.That(matched.Id, Is.EqualTo(OverrideId));

            Assert.That(UITextBackendRegistry.Unregister(OverrideId), Is.True);
            Assert.That(
                UITextBackendRegistry.TryMatch(typeof(FakeTextDerived), out _),
                Is.False);

            Assert.That(UITextBackendRegistry.Unregister(OverrideId), Is.False);
            Assert.That(UITextBackendRegistry.Unregister(null), Is.False);
            Assert.That(UITextBackendRegistry.Unregister(string.Empty), Is.False);
        }

        [Test]
        public void Register_InvalidatesCachedMisses()
        {
            // A cached negative result must not shadow a backend registered later for that type.
            Assert.That(UITextBackendRegistry.TryMatch(typeof(int), out _), Is.False);

            UITextBackendRegistry.Register(Fake(PrimaryId, 0, baseTypeName: typeof(int).FullName));

            Assert.That(
                UITextBackendRegistry.TryMatch(typeof(int), out UITextBackend matched),
                Is.True);
            Assert.That(matched.Id, Is.EqualTo(PrimaryId));
        }

        [Test]
        public void Version_AdvancesWhenTheRegisteredSetChanges()
        {
            long before = UITextBackendRegistry.Version;
            UITextBackendRegistry.Register(Fake(PrimaryId, 0));
            long afterRegister = UITextBackendRegistry.Version;
            UITextBackendRegistry.Unregister(PrimaryId);
            long afterUnregister = UITextBackendRegistry.Version;

            Assert.That(afterRegister, Is.GreaterThan(before));
            Assert.That(afterUnregister, Is.GreaterThan(afterRegister));
        }

        [Test]
        public void BackendsSnapshot_IsImmutableWhileVersionTracksChange()
        {
            System.Collections.Generic.IReadOnlyList<UITextBackend> snapshot = UITextBackendRegistry.Backends;
            int snapshotCount = snapshot.Count;
            long snapshotVersion = UITextBackendRegistry.Version;

            UITextBackendRegistry.Register(Fake(PrimaryId, 0));

            // The snapshot a caller already holds stays valid to iterate; Version is how it learns
            // that a newer set exists.
            Assert.That(snapshot.Count, Is.EqualTo(snapshotCount));
            Assert.That(UITextBackendRegistry.Backends.Count, Is.EqualTo(snapshotCount + 1));
            Assert.That(UITextBackendRegistry.Version, Is.GreaterThan(snapshotVersion));
        }

        [Test]
        public void Register_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => UITextBackendRegistry.Register(null));
        }

        [Test]
        public void Backend_RejectsMissingRequiredFields()
        {
            Assert.Throws<ArgumentException>(() => new UITextBackend(null, "n", "b", "f"));
            Assert.Throws<ArgumentException>(() => new UITextBackend("id", "n", " ", "f"));
            Assert.Throws<ArgumentException>(() => new UITextBackend("id", "n", "b", ""));
        }

        [Test]
        public void TryResolveBackendType_FindsATypeInTheLoadedAssemblies()
        {
            UITextBackend backend = Fake(PrimaryId, 0);

            Assert.That(UITextBackendRegistry.TryResolveBackendType(backend, out Type resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(typeof(FakeTextBase)));
        }

        [Test]
        public void TryResolveBackendType_ReportsAnUnresolvableName()
        {
            UITextBackend backend = Fake(PrimaryId, 0, baseTypeName: "No.Such.Namespace.NoSuchText");

            Assert.That(UITextBackendRegistry.TryResolveBackendType(backend, out Type resolved), Is.False);
            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void Matching_DoesNotDependOnTypeResolution()
        {
            // Matching is by reflected name, so a backend still matches while its type cannot be
            // resolved by the diagnostic, and resolution never becomes a match precondition.
            Assert.That(Fake(PrimaryId, 0).Matches(typeof(FakeTextDerived)), Is.True);
            Assert.That(
                Fake(SecondaryId, 0, baseTypeName: "No.Such.Namespace.NoSuchText")
                    .Matches(typeof(FakeTextDerived)),
                Is.False);
        }

        [Test]
        public void TryResolveBackendType_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(
                () => UITextBackendRegistry.TryResolveBackendType(null, out _));
        }

#if CYCLONEGAMES_HAS_TEXTMESHPRO
        [Test]
        public void RealTextMeshProComponent_MatchesTheBuiltInBackend()
        {
            Assert.That(
                UITextBackendRegistry.TryMatch(typeof(TextMeshProUGUI), out UITextBackend matched),
                Is.True);
            Assert.That(matched.Id, Is.EqualTo(UITextBackendRegistry.TextMeshPro.Id));
        }
#endif
    }
}
