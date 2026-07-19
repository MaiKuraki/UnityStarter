using System;

using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class DeterministicRandomTests
    {
        private static readonly ulong[] SeedZeroGoldenSequence =
        {
            0x99EC5F36CB75F2B4UL,
            0xBF6E1F784956452AUL,
            0x1A5F849D4933E6E0UL,
            0x6AA594F1262D2D2CUL,
            0xBBA5AD4A1F842E59UL,
            0xFFEF8375D9EBCACAUL,
            0x6C160DEED2F54C98UL,
            0x8920AD648FC30A3FUL,
        };

        [Test]
        public void AlgorithmIdentity_IsExplicitlyVersioned()
        {
            Assert.That(DeterministicRandom.ALGORITHM_ID, Is.EqualTo("xoshiro256**"));
            Assert.That(DeterministicRandom.ALGORITHM_VERSION, Is.EqualTo(1));
        }

        [Test]
        public void SeedZero_ProducesCanonicalSplitMixState()
        {
            DeterministicRandom random = DeterministicRandom.Create(0UL);

            DeterministicRandomState state = random.SaveState();

            Assert.That(state.S0, Is.EqualTo(0xE220A8397B1DCDAFUL));
            Assert.That(state.S1, Is.EqualTo(0x6E789E6AA1B965F4UL));
            Assert.That(state.S2, Is.EqualTo(0x06C45D188009454FUL));
            Assert.That(state.S3, Is.EqualTo(0xF88BB8A8724C81ECUL));
            Assert.That(state.IsValid, Is.True);
            Assert.That(random.IsInitialized, Is.True);
        }

        [Test]
        public void SeedZero_ProducesCanonicalXoshiroGoldenSequence()
        {
            DeterministicRandom random = DeterministicRandom.Create(0UL);

            for (int i = 0; i < SeedZeroGoldenSequence.Length; i++)
            {
                Assert.That(random.NextULong(), Is.EqualTo(SeedZeroGoldenSequence[i]), $"Output {i}.");
            }
        }

        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            DeterministicRandom a = DeterministicRandom.Create(12345UL);
            DeterministicRandom b = DeterministicRandom.Create(12345UL);

            for (int i = 0; i < 128; i++)
            {
                Assert.That(a.NextULong(), Is.EqualTo(b.NextULong()), $"Output {i}.");
            }
        }

        [Test]
        public void DefaultGenerator_IsInvalidAndAllSamplingFailsFast()
        {
            DeterministicRandom random = default;

            Assert.That(random.IsInitialized, Is.False);
            Assert.That(default(DeterministicRandomState).IsValid, Is.False);
            Assert.That(() => random.NextULong(), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => random.NextInt(10), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => random.NextInt(-10, 10), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => random.NextFP(), Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => random.NextFP(FPInt64.Zero, FPInt64.One),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => random.SaveState(), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void InvalidTrySampling_DoesNotAdvanceState()
        {
            DeterministicRandom random = DeterministicRandom.Create(9UL);
            DeterministicRandomState before = random.SaveState();

            Assert.That(random.TryNextInt(0, out _), Is.False);
            Assert.That(random.TryNextInt(5, 5, out _), Is.False);
            Assert.That(random.TryNextFP(FPInt64.One, FPInt64.One, out _), Is.False);

            Assert.That(random.SaveState(), Is.EqualTo(before));
        }

        [Test]
        public void InvalidNonTryRanges_ThrowArgumentOutOfRange()
        {
            DeterministicRandom random = DeterministicRandom.Create(10UL);

            Assert.That(() => random.NextInt(0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => random.NextInt(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => random.NextInt(5, 5), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => random.NextInt(6, 5), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => random.NextFP(FPInt64.One, FPInt64.One),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void BoundedIntSequence_UsesCanonicalUnbiasedMapping()
        {
            int[] expected = { 0, 2, 8, 2, 7, 8, 4, 3 };
            DeterministicRandom random = DeterministicRandom.Create(0UL);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(random.NextInt(10), Is.EqualTo(expected[i]), $"Output {i}.");
            }
        }

        [Test]
        public void FullSignedIntRange_DoesNotOverflowRangeCalculation()
        {
            DeterministicRandom random = DeterministicRandom.Create(0x12345678UL);

            for (int i = 0; i < 1024; i++)
            {
                int value = random.NextInt(int.MinValue, int.MaxValue);
                Assert.That(value, Is.GreaterThanOrEqualTo(int.MinValue));
                Assert.That(value, Is.LessThan(int.MaxValue));
            }
        }

        [Test]
        public void UnitIntRange_AlwaysReturnsZero()
        {
            DeterministicRandom random = DeterministicRandom.Create(17UL);

            for (int i = 0; i < 32; i++)
            {
                Assert.That(random.NextInt(1), Is.EqualTo(0));
            }
        }

        [Test]
        public void NextFp_StaysInHalfOpenUnitInterval()
        {
            DeterministicRandom random = DeterministicRandom.Create(23UL);

            for (int i = 0; i < 1024; i++)
            {
                FPInt64 value = random.NextFP();
                Assert.That(value.RawValue, Is.GreaterThanOrEqualTo(0L));
                Assert.That(value.RawValue, Is.LessThan(FPInt64.RAW_ONE));
            }
        }

        [Test]
        public void NextFp_WideSignedRange_DoesNotOverflow()
        {
            DeterministicRandom random = DeterministicRandom.Create(29UL);
            FPInt64 min = FPInt64.FromInt(-2_000_000_000);
            FPInt64 max = FPInt64.FromInt(2_000_000_000);

            for (int i = 0; i < 1024; i++)
            {
                FPInt64 value = random.NextFP(min, max);
                Assert.That(value.RawValue, Is.GreaterThanOrEqualTo(min.RawValue));
                Assert.That(value.RawValue, Is.LessThan(max.RawValue));
            }
        }

        [Test]
        public void SaveAndRestore_ResumesExactSequence()
        {
            DeterministicRandom random = DeterministicRandom.Create(99UL);
            random.NextULong();
            DeterministicRandomState state = random.SaveState();
            ulong expected = random.NextULong();
            random.NextULong();

            random.RestoreState(state);

            Assert.That(random.NextULong(), Is.EqualTo(expected));
        }

        [Test]
        public void AllZeroRestore_IsRejectedWithoutChangingCurrentState()
        {
            DeterministicRandom random = DeterministicRandom.Create(101UL);
            DeterministicRandomState before = random.SaveState();
            DeterministicRandomState invalid = default;

            Assert.That(random.TryRestoreState(invalid), Is.False);
            Assert.That(() => random.RestoreState(invalid), Throws.TypeOf<ArgumentException>());

            Assert.That(random.SaveState(), Is.EqualTo(before));
        }

        [Test]
        public void TryRestoreState_InitializesDefaultGenerator()
        {
            DeterministicRandom source = DeterministicRandom.Create(77UL);
            DeterministicRandomState state = source.SaveState();
            DeterministicRandom restored = default;

            bool success = restored.TryRestoreState(state);

            Assert.That(success, Is.True);
            Assert.That(restored.IsInitialized, Is.True);
            Assert.That(restored.NextULong(), Is.EqualTo(source.NextULong()));
        }
    }
}
