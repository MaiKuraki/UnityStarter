using System;
using System.Reflection;
using System.Runtime.InteropServices;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Engine-free contract evidence: struct layout, value-type clamps and the authoring-to-runtime projection.
    /// Every assertion here is deterministic and independent of frame timing.
    /// </summary>
    public sealed class EvidenceContractTests : EvidenceTestBase
    {
        // ---------------------------------------------------------------- H01

        [Test]
        public void H01_PlaybackHandle_IsTwelveBytesAsDocumented()
        {
            int handleSize = LayoutSize(typeof(AnimationPlaybackHandle), out string layout);
            int marshalled = Marshal.SizeOf(typeof(AnimationPlaybackHandle));

            NoteEvidence(
                "H01",
                "AnimationPlaybackHandle managed size " + handleSize + " B, marshalled size " + marshalled
                + " B, layout " + layout + ", AnimationRuntimeGraph.PlaybackStructSize "
                + AnimationRuntimeGraph.PlaybackStructSize + " B");

            Assert.AreEqual(12, handleSize, "int(4) + byte(1) + byte(1) + uint(4), alignment 4");
            Assert.AreEqual(handleSize, marshalled, "all fields are blittable, so both measurements agree");
        }

        [Test]
        public void H01b_PlaybackStruct_IsExactlyOneCacheLine_AndAppliedTimeWouldSpillTo72()
        {
            int measuredPlaybackSize = AnimationRuntimeGraph.PlaybackStructSize;

            Type playbackType = typeof(AnimationRuntimeGraph).GetNestedType("Playback", BindingFlags.NonPublic);
            Assert.IsNotNull(playbackType, "AnimationRuntimeGraph.Playback moved; update this test");

            int computedPlaybackSize = LayoutSize(playbackType, out string playbackLayout);
            Assert.AreEqual(measuredPlaybackSize, computedPlaybackSize,
                "the layout calculator must reproduce the size the module measures itself, otherwise its "
                + "prediction for the variant is not trustworthy");

            int computedWithAppliedTime = LayoutSize(typeof(PlaybackWithAppliedTime), out string variantLayout);

            NoteEvidence(
                "H01b",
                "Playback measured by the module = " + measuredPlaybackSize + " B, recomputed = " + computedPlaybackSize
                + " B [" + playbackLayout + "]; same layout plus one double AppliedTime = "
                + computedWithAppliedTime + " B [" + variantLayout + "]; cache line = 64 B");

            Assert.AreEqual(64, measuredPlaybackSize, "the playback struct is exactly one cache line today");
            Assert.AreEqual(72, computedWithAppliedTime,
                "the extra double is 8-byte aligned, so it costs 8 bytes plus 8 bytes of tail padding");
            Assert.Greater(computedWithAppliedTime, 64,
                "the applied-time field the code comments refuse to add would cross the cache line boundary");
        }

        /// <summary>
        /// Managed size of a sequential struct, computed from its field list with the alignment rules the CLR
        /// uses. Needed because unsafe code is disabled and test assemblies do not reference
        /// <c>System.Runtime.CompilerServices.Unsafe</c>; the calculator is validated against
        /// <see cref="AnimationRuntimeGraph.PlaybackStructSize"/>, which the module measures with Unsafe itself.
        /// </summary>
        private static int LayoutSize(Type type, out string layout)
        {
            int offset = 0;
            int maxAlignment = 1;
            var builder = new System.Text.StringBuilder();

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                int size = ManagedSize(field.FieldType);
                int alignment = size < IntPtr.Size ? size : IntPtr.Size;

                offset = (offset + alignment - 1) / alignment * alignment;
                builder.Append(field.Name).Append('@').Append(offset).Append(' ');
                offset += size;

                if (alignment > maxAlignment)
                {
                    maxAlignment = alignment;
                }
            }

            offset = (offset + maxAlignment - 1) / maxAlignment * maxAlignment;
            layout = builder.ToString().TrimEnd();
            return offset;
        }

        private static int ManagedSize(Type type)
        {
            if (type.IsClass || type.IsInterface)
            {
                return IntPtr.Size;
            }

            if (type.IsEnum)
            {
                return ManagedSize(Enum.GetUnderlyingType(type));
            }

            if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte))
            {
                return 1;
            }

            if (type == typeof(short) || type == typeof(ushort) || type == typeof(char))
            {
                return 2;
            }

            if (type == typeof(int) || type == typeof(uint) || type == typeof(float))
            {
                return 4;
            }

            if (type == typeof(long) || type == typeof(ulong) || type == typeof(double))
            {
                return 8;
            }

            // A nested value type: Playable is an IntPtr plus a version field, so recurse.
            return LayoutSize(type, out _);
        }

        /// <summary>Layout mirror of the private playback struct with an applied-time field.</summary>
        private struct PlaybackWithAppliedTime
        {
            public Playable Playable;
            public AnimationClip Clip;
            public AnimationClip BoundClip;
            public int InstanceId;
            public int PlaybackChannel;
            public double Time;
            public double AppliedTime;
            public float Weight;
            public float AppliedWeight;
            public float Length;
            public byte Flags;
            public byte ClipChannel;
        }

        // ---------------------------------------------------------------- H09

        [Test]
        public void H09_ProfileAndDescriptor_NormaliseCapacityTheSameWay()
        {
            var profile = ScriptableObject.CreateInstance<AnimationLayerProfile>();
            try
            {
                var layers = new AnimationLayerProfile.Layer[2];
                layers[0] = new AnimationLayerProfile.Layer { Capacity = 0, Weight = 0.5f };
                layers[1] = new AnimationLayerProfile.Layer { Capacity = 99, Weight = 1f };
                SetPrivateField(profile, "_layers", layers);

                MethodInfo onValidate = typeof(AnimationLayerProfile).GetMethod(
                    "OnValidate",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(onValidate, "AnimationLayerProfile.OnValidate moved; update this test");
                onValidate.Invoke(profile, null);

                Assert.IsTrue(profile.TryGetLayer(0, out AnimationLayerProfile.Layer repaired));
                AnimationLayerDescriptor[] descriptors = profile.ToDescriptors();

                NoteEvidence(
                    "H09",
                    "authored capacity 0 -> OnValidate writes " + repaired.Capacity
                    + ", ToDescriptors capacity " + (int)descriptors[0].Capacity
                    + ", authored capacity 99 -> " + (int)descriptors[1].Capacity);

                Assert.AreEqual(
                    (byte)AnimationRuntimeLimits.MinLayerCapacity,
                    repaired.Capacity,
                    "both normalisation paths clamp an invalid capacity to the same floor");
                Assert.AreEqual(
                    (byte)AnimationRuntimeLimits.MinLayerCapacity,
                    descriptors[0].Capacity);
                Assert.AreEqual(
                    (byte)AnimationRuntimeLimits.MaxLayerCapacity,
                    descriptors[1].Capacity,
                    "the upper bound is clamped in both paths");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void H09b_ProfileProjection_TreatsZeroWeightAsUnset()
        {
            var profile = ScriptableObject.CreateInstance<AnimationLayerProfile>();
            try
            {
                var layers = new AnimationLayerProfile.Layer[1];
                layers[0] = new AnimationLayerProfile.Layer { Capacity = 4, Weight = 0f };
                SetPrivateField(profile, "_layers", layers);

                AnimationLayerDescriptor[] descriptors = profile.ToDescriptors();

                NoteEvidence(
                    "H09b",
                    "authored layer weight 0 -> projected descriptor weight " + descriptors[0].Weight);

                Assert.AreEqual(0f, layers[0].Weight);
                Assert.AreEqual(1f, descriptors[0].Weight,
                    "0 means 'unset' for authoring; SetLayerWeight(0) is the explicit runtime mute");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        // ---------------------------------------------------------------- H10

        [Test]
        public void H10_TickDelta_SanitisesNonFiniteAndNegativeValues()
        {
            var nanTick = new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, float.NaN);
            var infiniteTick = new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, float.PositiveInfinity);
            var negativeTick = new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, -1f);

            NoteEvidence(
                "H10",
                "TransitionDelta: NaN -> " + nanTick.TransitionDelta
                + ", +Inf -> " + infiniteTick.TransitionDelta
                + ", -1 -> " + negativeTick.TransitionDelta);

            Assert.AreEqual(0f, nanTick.TransitionDelta);
            Assert.AreEqual(0f, infiniteTick.TransitionDelta);
            Assert.AreEqual(0f, negativeTick.TransitionDelta);

            Assert.AreEqual(0d, new AnimationTick(double.NaN, 0L, AnimationTimeSource.Update).ClipTime);
        }

        // ---------------------------------------------------------------- H06, H08, H20 (static only)

        [Test]
        public void H06_RigMismatchWarningLatch_ExcludesHumanoidRuntimeEvidence()
        {
            // A humanoid clip cannot be authored at runtime, so this remains source-level evidence. The latch
            // is now cleared in BuildGraph, which means a rebind can report a new mismatch instead of staying
            // silent for the rest of the process lifetime.
            Assert.IsTrue(
                FieldExists(typeof(AnimationRuntimeGraph), "_warnedRigMismatch"),
                "the one-shot warning latch documented in the report is gone; revisit the finding");
            NoteEvidence("H06", "static only: latch exists and is reset per graph build; Humanoid rig not constructible at runtime");
            Assert.Pass("static evidence only; see report section on H06");
        }

        [Test]
        public void H20_GraphDeclaresNoFinalizer_SoAForgottenDisposeIsSilent()
        {
            MethodInfo declaredFinalizer = typeof(AnimationRuntimeGraph).GetMethod(
                "Finalize",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            NoteEvidence("H20", "declared finalizer on AnimationRuntimeGraph: " + (declaredFinalizer != null));

            Assert.IsNull(declaredFinalizer,
                "the type holds a native PlayableGraph and implements IDisposable without a finalizer, so a "
                + "missed Dispose leaks native memory with no diagnostic");
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(typeof(AnimationRuntimeGraph)));
        }

        private static bool FieldExists(Type type, string name)
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) != null
                || type.GetField(name, BindingFlags.Instance | BindingFlags.Public) != null;
        }
    }
}
