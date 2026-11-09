using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests
{
    public sealed class AnimationClipReferenceTests : LoggedTestBase
    {
        /// <summary>
        /// Demonstrates the "custom path from a data table" case: a resolver that maps an arbitrary key to a clip
        /// without the runtime knowing anything about paths, Resources or asset bundles.
        /// </summary>
        private sealed class TablePathResolver : IAnimationClipResolver
        {
            private readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>(4);

            public void Add(string location, AnimationClip clip)
            {
                _clips[location] = clip;
            }

            public bool TryResolve(in AnimationClipReference reference, out AnimationClip clip)
            {
                return _clips.TryGetValue(reference.Location, out clip);
            }
        }

        [Test]
        public void Default_IsNotValid()
        {
            AnimationClipReference reference = default;

            Assert.IsFalse(reference.IsValid);
        }

        [Test]
        public void EmbeddedClip_IsValidAndResolvable()
        {
            var clip = new AnimationClip();
            try
            {
                var reference = AnimationClipReference.FromClip(clip);

                Assert.IsTrue(reference.IsValid);
                Assert.AreEqual(AnimationClipReferenceMode.Embedded, reference.EffectiveMode);
                Assert.IsTrue(reference.TryGetEmbeddedClip(out AnimationClip resolved));
                Assert.AreSame(clip, resolved);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void Location_IsValidButHasNoEmbeddedClip()
        {
            var reference = AnimationClipReference.FromLocation("anim/attack_heavy_02", "guid-123");

            Assert.IsTrue(reference.IsValid);
            Assert.AreEqual(AnimationClipReferenceMode.Location, reference.EffectiveMode);
            Assert.IsFalse(reference.TryGetEmbeddedClip(out _));
            Assert.AreEqual("guid-123", reference.EditorGuid);
        }

        [Test]
        public void EmbeddedModeWithMissingClip_FallsBackToLocation()
        {
            var reference = new AnimationClipReference(AnimationClipReferenceMode.Embedded, null, "anim/moved");

            Assert.AreEqual(AnimationClipReferenceMode.Location, reference.EffectiveMode);
            Assert.IsTrue(reference.IsValid);
        }

        [Test]
        public void EmbeddedModeWithNothing_IsInvalid()
        {
            var reference = new AnimationClipReference(AnimationClipReferenceMode.Embedded, null, null);

            Assert.AreEqual(AnimationClipReferenceMode.Embedded, reference.EffectiveMode);
            Assert.IsFalse(reference.IsValid);
        }

        [Test]
        public void Equality_UsesEffectiveModeAndPayload()
        {
            var a = AnimationClipReference.FromLocation("anim/a");
            var b = AnimationClipReference.FromLocation("anim/a");
            var c = AnimationClipReference.FromLocation("anim/b");

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(a, c);
        }

        [Test]
        public void EmbeddedResolver_RejectsLocationReferences()
        {
            var resolver = EmbeddedAnimationClipResolver.Instance;
            var location = AnimationClipReference.FromLocation("anim/a");

            Assert.IsFalse(resolver.TryResolve(location, out AnimationClip resolved));
            Assert.IsNull(resolved);
        }

        [Test]
        public void EmbeddedResolver_AcceptsEmbeddedClips()
        {
            var clip = new AnimationClip();
            try
            {
                var resolver = EmbeddedAnimationClipResolver.Instance;
                var reference = AnimationClipReference.FromClip(clip);

                Assert.IsTrue(resolver.TryResolve(reference, out AnimationClip resolved));
                Assert.AreSame(clip, resolved);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void CustomPathResolver_ResolvesDataTableKeys()
        {
            var clip = new AnimationClip();
            try
            {
                var resolver = new TablePathResolver();
                resolver.Add("Animations/Attack/Heavy_02", clip);

                var reference = AnimationClipReference.FromLocation("Animations/Attack/Heavy_02");

                Assert.IsTrue(resolver.TryResolve(reference, out AnimationClip resolved));
                Assert.AreSame(clip, resolved);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void CustomPathResolver_Miss_ReturnsFalse()
        {
            var resolver = new TablePathResolver();
            var reference = AnimationClipReference.FromLocation("Animations/Attack/Missing");

            Assert.IsFalse(resolver.TryResolve(reference, out AnimationClip resolved));
            Assert.IsNull(resolved);
        }
    }
}
