// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Audio.Tests
{
    /// <summary>
    /// Reports the per-operation cost of the spatial primitives so optimisation decisions are based on
    /// measurements instead of assumptions. Numbers vary by machine; the ratios drive the decision.
    /// </summary>
    public sealed class AudioSpatialCostTests
    {
        private const int Iterations = 200000;
        private const int Rounds = 5;

        private static float floatSink;
        private static int intSink;

        [Test]
        public void Report_CurveEvaluateVersusLut()
        {
            AnimationCurve curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.2f, 0.55f),
                new Keyframe(0.45f, 0.22f),
                new Keyframe(0.7f, 0.06f),
                new Keyframe(0.85f, 0.01f),
                new Keyframe(1f, 0f));

            float[] lut = BakeLut(curve, 256);

            double evaluateNs = MeasureBest(() => MeasureCurve(curve));
            double lutNs = MeasureBest(() => MeasureLut(lut));

            TestContext.WriteLine($"curve.evaluate: {evaluateNs:F2} ns/op");
            TestContext.WriteLine($"lut.sample:    {lutNs:F2} ns/op");
            TestContext.WriteLine($"ratio:         {evaluateNs / lutNs:F2}x");
            TestContext.WriteLine($"100 events/frame: evaluate={evaluateNs * 100 / 1000f:F1} us, lut={lutNs * 100 / 1000f:F1} us");

            Assert.Greater(evaluateNs, 0d);
            Assert.Greater(lutNs, 0d);
        }

        [Test]
        public void Report_LinecastSingleVersusMultipleRays()
        {
            var origin = new Vector3(0f, 0f, 0f);
            var target = new Vector3(0f, 0f, 12f);
            int layerMask = ~0;

            double emptyNs = MeasureBest(() => MeasureLinecast(origin, target, layerMask));
            double threeRaysNs = MeasureBest(() => MeasureThreeRays(origin, target, layerMask));

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 0f, 6f);
            wall.transform.localScale = new Vector3(4f, 4f, 0.2f);
            Physics.SyncTransforms();

            try
            {
                double blockedNs = MeasureBest(() => MeasureLinecast(origin, target, layerMask));

                TestContext.WriteLine($"linecast empty scene: {emptyNs:F2} ns/op");
                TestContext.WriteLine($"linecast with wall:   {blockedNs:F2} ns/op");
                TestContext.WriteLine($"linecast 3 rays:      {threeRaysNs:F2} ns/op ({threeRaysNs / emptyNs:F2}x single)");
                TestContext.WriteLine(
                    $"amortised (0.25s reuse, 100 events): threeRays={threeRaysNs * 100 / 0.25f / 1e6f:F3} ms/s");

                Assert.Greater(emptyNs, 0d);
                Assert.Greater(blockedNs, 0d);
                Assert.Greater(threeRaysNs, 0d);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wall);
            }
        }

        private static float[] BakeLut(AnimationCurve curve, int resolution)
        {
            var samples = new float[resolution];
            for (int i = 0; i < resolution; i++)
            {
                samples[i] = curve.Evaluate(i / (float)(resolution - 1));
            }

            return samples;
        }

        private static float SampleLut(float[] samples, float t)
        {
            float clamped = Mathf.Clamp01(t);
            float scaled = clamped * (samples.Length - 1);
            int index = (int)scaled;
            if (index >= samples.Length - 1) return samples[samples.Length - 1];

            float fraction = scaled - index;
            return samples[index] + (samples[index + 1] - samples[index]) * fraction;
        }

        private static double MeasureBest(Func<long> measurement)
        {
            double best = double.MaxValue;
            for (int round = 0; round < Rounds; round++)
            {
                double ns = measurement() * (1000000000d / Stopwatch.Frequency) / Iterations;
                if (ns < best) best = ns;
            }

            return best;
        }

        private static long MeasureCurve(AnimationCurve curve)
        {
            var stopwatch = Stopwatch.StartNew();
            float sum = 0f;
            for (int i = 0; i < Iterations; i++)
            {
                sum += curve.Evaluate((i & 1023) / 1023f);
            }

            stopwatch.Stop();
            floatSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureLut(float[] lut)
        {
            var stopwatch = Stopwatch.StartNew();
            float sum = 0f;
            for (int i = 0; i < Iterations; i++)
            {
                sum += SampleLut(lut, (i & 1023) / 1023f);
            }

            stopwatch.Stop();
            floatSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureLinecast(Vector3 origin, Vector3 target, int layerMask)
        {
            var stopwatch = Stopwatch.StartNew();
            int hits = 0;
            for (int i = 0; i < Iterations; i++)
            {
                if (Physics.Linecast(origin, target, layerMask)) hits++;
            }

            stopwatch.Stop();
            intSink = hits;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureThreeRays(Vector3 origin, Vector3 target, int layerMask)
        {
            var side = new Vector3(0.35f, 0f, 0f);
            var stopwatch = Stopwatch.StartNew();
            int hits = 0;
            for (int i = 0; i < Iterations; i++)
            {
                if (Physics.Linecast(origin, target, layerMask)) hits++;
                if (Physics.Linecast(origin + side, target + side, layerMask)) hits++;
                if (Physics.Linecast(origin - side, target - side, layerMask)) hits++;
            }

            stopwatch.Stop();
            intSink = hits;
            return stopwatch.ElapsedTicks;
        }
    }
}
