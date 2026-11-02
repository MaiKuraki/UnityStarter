// Copyright (c) CycloneGames
// Licensed under the MIT License.

using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Audio.Tests
{
    /// <summary>
    /// Geometry scenarios for the occlusion probe: no cover, full cover, partial cover, and the
    /// legacy single-ray path.
    /// </summary>
    public sealed class AudioOcclusionProbeTests
    {
        private static readonly Vector3 Emitter = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 Listener = new Vector3(0f, 0f, 10f);
        private const int Everything = ~0;

        private GameObject wall;

        [TearDown]
        public void TearDown()
        {
            if (wall != null) Object.DestroyImmediate(wall);
            wall = null;
        }

        [Test]
        public void NoGeometry_ReportsClear()
        {
            Assert.AreEqual(0f, AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 1));
            Assert.AreEqual(0f, AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 3));
            Assert.AreEqual(0f, AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 5));
        }

        [Test]
        public void FullWall_ReportsFullyBlocked()
        {
            CreateWall(new Vector3(0f, 0f, 5f), new Vector3(4f, 4f, 0.2f));

            Assert.AreEqual(1f, AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 1));
            Assert.AreEqual(1f, AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 3));
            Assert.AreEqual(1f, AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 5));
        }

        [Test]
        public void HalfWall_ReportsPartialBlocking()
        {
            // Covers x in [0.1, 1.7]: the centre ray passes, the +0.35 m ray is blocked.
            CreateWall(new Vector3(0.9f, 0f, 5f), new Vector3(1.6f, 4f, 0.2f));

            float single = AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 1);
            float three = AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 3);

            Assert.AreEqual(0f, single, "the centre ray must pass beside the wall");
            Assert.Greater(three, 0f, "a sampled edge must be reported as partial cover");
            Assert.Less(three, 1f);
            Assert.AreEqual(1f / 3f, three, 0.001f);
        }

        [Test]
        public void PartiallyCovered_DecaysWithMoreRays()
        {
            // Narrow pillar on the path: fewer of the outer rays are blocked as the count grows.
            CreateWall(new Vector3(0.35f, 0f, 5f), new Vector3(0.6f, 4f, 0.2f));

            float three = AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 3);
            float five = AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 5);

            Assert.Greater(three, 0f);
            Assert.Greater(five, 0f);
            Assert.LessOrEqual(five, three + 0.001f, "more rays must not over-report cover");
        }

        [Test]
        public void RayCount_IsClampedToSupportedRange()
        {
            CreateWall(new Vector3(0.9f, 0f, 5f), new Vector3(1.6f, 4f, 0.2f));

            float zero = AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 0);
            Assert.AreEqual(0f, zero, "0 must behave as the legacy single centre ray");

            float oversized = AudioOcclusionProbe.Sample(Emitter, Listener, Everything, 99);
            float scaled = oversized * AudioOcclusionProbe.MaximumRayCount;
            Assert.AreEqual(Mathf.Round(scaled), scaled, 0.001f, "ray count must clamp to the supported maximum");
        }

        [Test]
        public void CoincidentEndpoints_ProduceBinaryResultWithoutNaN()
        {
            CreateWall(new Vector3(0f, 0f, 0f), new Vector3(4f, 4f, 0.2f));

            float result = AudioOcclusionProbe.Sample(Emitter, Emitter, Everything, 3);

            Assert.IsFalse(float.IsNaN(result), "a degenerate path must not produce NaN");
            Assert.That(result == 0f || result == 1f, $"expected a binary fallback, got {result}");
        }

        [Test]
        public void LayerMask_ExcludesIgnoredLayers()
        {
            CreateWall(new Vector3(0f, 0f, 5f), new Vector3(4f, 4f, 0.2f));
            wall.layer = 8;

            Assert.AreEqual(0f, AudioOcclusionProbe.Sample(Emitter, Listener, 1 << 0, 3), "layer 0 mask must ignore layer 8");
            Assert.AreEqual(1f, AudioOcclusionProbe.Sample(Emitter, Listener, 1 << 8, 3));
        }

        private void CreateWall(Vector3 position, Vector3 scale)
        {
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "OcclusionProbeWall";
            wall.transform.position = position;
            wall.transform.localScale = scale;
            Physics.SyncTransforms();
        }
    }
}
