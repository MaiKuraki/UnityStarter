using System;
using System.Numerics;
using System.Reflection;

using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPInt64Tests
    {
        private const int RANDOM_ORACLE_CASES = 512;

        [Test]
        public void Q32Constants_HaveExpectedRawLayout()
        {
            Assert.That(FPInt64.FractionalBits, Is.EqualTo(32));
            Assert.That(FPInt64.RAW_ONE, Is.EqualTo(1L << 32));
            Assert.That(FPInt64.RAW_HALF, Is.EqualTo(1L << 31));
            Assert.That(FPInt64.One.RawValue, Is.EqualTo(FPInt64.RAW_ONE));
            Assert.That(FPInt64.Half.RawValue, Is.EqualTo(FPInt64.RAW_HALF));
            Assert.That(FPInt64.MinValue.RawValue, Is.EqualTo(long.MinValue));
            Assert.That(FPInt64.MaxValue.RawValue, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void PublicSurface_UsesExplicitFactoriesAndValueConstants()
        {
            Type type = typeof(FPInt64);

            Assert.That(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance), Is.Empty);
            Assert.That(
                type.GetMethod(nameof(FPInt64.Parse), BindingFlags.Public | BindingFlags.Static)?.ReturnType,
                Is.EqualTo(typeof(FPInt64)));
            Assert.That(type.GetField(nameof(FPInt64.RAW_ONE))?.FieldType, Is.EqualTo(typeof(long)));
            Assert.That(type.GetField(nameof(FPInt64.RAW_HALF))?.FieldType, Is.EqualTo(typeof(long)));
            Assert.That(type.GetField(nameof(FPInt64.One))?.FieldType, Is.EqualTo(typeof(FPInt64)));
            Assert.That(type.GetField(nameof(FPInt64.Half))?.FieldType, Is.EqualTo(typeof(FPInt64)));
            Assert.That(FPInt64.FromRaw(123L).RawValue, Is.EqualTo(123L));
        }

        [Test]
        public void ToInt_TruncatesTowardZero()
        {
            FPInt64 positive = FPInt64.FromDouble(1.75);
            FPInt64 negative = FPInt64.FromDouble(-1.75);

            Assert.That(positive.ToInt(), Is.EqualTo(1));
            Assert.That(negative.ToInt(), Is.EqualTo(-1));
        }

        [Test]
        public void SafeFloatingConversions_AcceptFiniteRepresentableValues()
        {
            Assert.That(FPInt64.TryFromFloat(-12.5f, out FPInt64 fromFloat), Is.True);
            Assert.That(fromFloat.RawValue, Is.EqualTo(-12L * FPInt64.RAW_ONE - FPInt64.RAW_HALF));
            Assert.That(FPInt64.TryFromDouble(2147483647.5d, out FPInt64 nearMaximum), Is.True);
            Assert.That(nearMaximum.RawValue, Is.EqualTo(long.MaxValue - FPInt64.RAW_HALF + 1L));
            Assert.That(FPInt64.TryFromDouble(-2147483648d, out FPInt64 minimum), Is.True);
            Assert.That(minimum, Is.EqualTo(FPInt64.MinValue));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(2147483648d)]
        [TestCase(-2147483649d)]
        public void TryFromDouble_InvalidValue_FailsWithoutResult(double value)
        {
            bool success = FPInt64.TryFromDouble(value, out FPInt64 result);

            Assert.That(success, Is.False);
            Assert.That(result, Is.EqualTo(FPInt64.Zero));
            Assert.That(() => FPInt64.FromDouble(value), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void TryFromFloat_NonFiniteValuesFail()
        {
            Assert.That(FPInt64.TryFromFloat(float.NaN, out _), Is.False);
            Assert.That(FPInt64.TryFromFloat(float.PositiveInfinity, out _), Is.False);
            Assert.That(FPInt64.TryFromFloat(float.NegativeInfinity, out _), Is.False);
            Assert.That(() => FPInt64.FromFloat(float.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ArithmeticOperators_UseExplicitUncheckedWrapping()
        {
            FPInt64 oneRaw = FPInt64.FromRaw(1L);

            Assert.That((FPInt64.MaxValue + oneRaw).RawValue, Is.EqualTo(long.MinValue));
            Assert.That((FPInt64.MinValue - oneRaw).RawValue, Is.EqualTo(long.MaxValue));
            Assert.That((-FPInt64.MinValue).RawValue, Is.EqualTo(long.MinValue));
        }

        [Test]
        public void TryAddSubtractAndNegate_ReportRawOverflow()
        {
            FPInt64 oneRaw = FPInt64.FromRaw(1L);

            Assert.That(FPInt64.TryAdd(FPInt64.MaxValue, oneRaw, out _), Is.False);
            Assert.That(FPInt64.TrySubtract(FPInt64.MinValue, oneRaw, out _), Is.False);
            Assert.That(FPInt64.TryNegate(FPInt64.MinValue, out _), Is.False);
            Assert.That(FPInt64.TryAdd(1, 2, out FPInt64 sum), Is.True);
            Assert.That(sum, Is.EqualTo(FPInt64.FromInt(3)));
            Assert.That(FPInt64.TrySubtract(1, 2, out FPInt64 difference), Is.True);
            Assert.That(difference, Is.EqualTo(FPInt64.FromInt(-1)));
            Assert.That(FPInt64.TryNegate(2, out FPInt64 negative), Is.True);
            Assert.That(negative, Is.EqualTo(FPInt64.FromInt(-2)));
        }

        [Test]
        public void Multiplication_AgreesWithBigIntegerOracle()
        {
            var random = new Random(0x6D617468);

            for (int i = 0; i < RANDOM_ORACLE_CASES; i++)
            {
                long aRaw = NextInt64(random);
                long bRaw = NextInt64(random);
                BigInteger exact = MultiplyOracle(aRaw, bRaw);
                long wrapped = WrapToInt64(exact);

                FPInt64 actual = FPInt64.FromRaw(aRaw) * FPInt64.FromRaw(bRaw);
                bool success = FPInt64.TryMultiply(
                    FPInt64.FromRaw(aRaw),
                    FPInt64.FromRaw(bRaw),
                    out FPInt64 checkedResult);
                bool representable = IsInt64(exact);

                Assert.That(actual.RawValue, Is.EqualTo(wrapped), $"Multiply case {i}.");
                Assert.That(success, Is.EqualTo(representable), $"TryMultiply case {i}.");
                if (representable)
                {
                    Assert.That(checkedResult.RawValue, Is.EqualTo((long)exact), $"TryMultiply result {i}.");
                }
            }
        }

        [Test]
        public void Division_AgreesWithBigIntegerOracle()
        {
            var random = new Random(0x64697669);

            for (int i = 0; i < RANDOM_ORACLE_CASES; i++)
            {
                long aRaw = NextInt64(random);
                long bRaw;
                do
                {
                    bRaw = NextInt64(random);
                }
                while (bRaw == 0);

                BigInteger exact = (new BigInteger(aRaw) << FPInt64.FractionalBits) / bRaw;
                long wrapped = WrapToInt64(exact);

                FPInt64 actual = FPInt64.FromRaw(aRaw) / FPInt64.FromRaw(bRaw);
                bool success = FPInt64.TryDivide(
                    FPInt64.FromRaw(aRaw),
                    FPInt64.FromRaw(bRaw),
                    out FPInt64 checkedResult);
                bool representable = IsInt64(exact);

                Assert.That(actual.RawValue, Is.EqualTo(wrapped), $"Divide case {i}.");
                Assert.That(success, Is.EqualTo(representable), $"TryDivide case {i}.");
                if (representable)
                {
                    Assert.That(checkedResult.RawValue, Is.EqualTo((long)exact), $"TryDivide result {i}.");
                }
            }
        }

        [Test]
        public void MultiplyDivide_AgreesWithFullPrecisionBigIntegerOracle()
        {
            var random = new Random(0x66757365);

            for (int i = 0; i < RANDOM_ORACLE_CASES; i++)
            {
                long aRaw = NextInt64(random);
                long bRaw = NextInt64(random);
                long divisorRaw;
                do
                {
                    divisorRaw = NextInt64(random);
                }
                while (divisorRaw == 0);

                BigInteger exact = (new BigInteger(aRaw) * bRaw) / divisorRaw;
                bool representable = IsInt64(exact);
                bool success = FPInt64.TryMultiplyDivide(
                    FPInt64.FromRaw(aRaw),
                    FPInt64.FromRaw(bRaw),
                    FPInt64.FromRaw(divisorRaw),
                    out FPInt64 result);

                Assert.That(success, Is.EqualTo(representable), $"TryMultiplyDivide case {i}.");
                if (representable)
                {
                    Assert.That(result.RawValue, Is.EqualTo((long)exact), $"TryMultiplyDivide result {i}.");
                }
            }
        }

        [Test]
        public void MultiplyDivide_ZeroDivisorFailsWithoutResult()
        {
            bool success = FPInt64.TryMultiplyDivide(
                FPInt64.One,
                FPInt64.One,
                FPInt64.Zero,
                out FPInt64 result);

            Assert.That(success, Is.False);
            Assert.That(result, Is.EqualTo(FPInt64.Zero));
        }

        [Test]
        public void Division_TruncatesNegativeSubRawResultTowardZero()
        {
            FPInt64 numerator = FPInt64.FromRaw(-1L);
            FPInt64 denominator = FPInt64.FromInt(2);

            Assert.That((numerator / denominator).RawValue, Is.EqualTo(0L));
        }

        [Test]
        public void DivisionByZero_ThrowsWhileTryDivideFails()
        {
            Assert.That(() => _ = FPInt64.One / FPInt64.Zero, Throws.TypeOf<DivideByZeroException>());
            Assert.That(FPInt64.TryDivide(FPInt64.One, FPInt64.Zero, out FPInt64 result), Is.False);
            Assert.That(result, Is.EqualTo(FPInt64.Zero));
        }

        [Test]
        public void TryMultiplyAndTryDivide_ReportUnrepresentableResults()
        {
            Assert.That(FPInt64.TryMultiply(FPInt64.MaxValue, 2, out _), Is.False);
            Assert.That(
                FPInt64.TryDivide(FPInt64.MaxValue, FPInt64.FromRaw(1L), out _),
                Is.False);
        }

        [Test]
        public void ExactDecimalText_RoundTripsRepresentativeRawValues()
        {
            long[] rawValues =
            {
                long.MinValue,
                long.MinValue + 1L,
                -FPInt64.RAW_ONE - 1L,
                -FPInt64.RAW_ONE,
                -1L,
                0L,
                1L,
                FPInt64.RAW_ONE,
                FPInt64.RAW_ONE + 1L,
                long.MaxValue - 1L,
                long.MaxValue,
            };

            foreach (long rawValue in rawValues)
            {
                FPInt64 original = FPInt64.FromRaw(rawValue);
                string text = original.ToString();
                bool success = FPInt64.TryParse(text, out FPInt64 parsed);

                Assert.That(success, Is.True, text);
                Assert.That(parsed.RawValue, Is.EqualTo(rawValue), text);
            }
        }

        [Test]
        public void ExactDecimalText_RoundTripsRandomRawValues()
        {
            var random = new Random(0x72617773);

            for (int i = 0; i < RANDOM_ORACLE_CASES; i++)
            {
                long rawValue = NextInt64(random);
                string text = FPInt64.FromRaw(rawValue).ToString();

                Assert.That(FPInt64.TryParse(text, out FPInt64 parsed), Is.True, $"Text case {i}: {text}");
                Assert.That(parsed.RawValue, Is.EqualTo(rawValue), $"Text case {i}: {text}");
            }
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("+")]
        [TestCase("-")]
        [TestCase("1,5")]
        [TestCase("1.2.3")]
        [TestCase("2147483648")]
        [TestCase("-2147483648.00000000023283064365386962890625")]
        [TestCase("0.000000000000000000000000000000001")]
        public void TryParse_RejectsInvalidOrOutOfRangeText(string text)
        {
            Assert.That(FPInt64.TryParse(text, out _), Is.False);
            Assert.That(() => FPInt64.Parse(text), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_AcceptsExactRangeEndpoints()
        {
            Assert.That(FPInt64.Parse("-2147483648"), Is.EqualTo(FPInt64.MinValue));
            Assert.That(
                FPInt64.Parse("2147483647.99999999976716935634613037109375"),
                Is.EqualTo(FPInt64.MaxValue));
        }

        [Test]
        public void AbsCeilRoundAndSqrt_ExposeInvalidNonTryOperations()
        {
            Assert.That(FPInt64.TryAbs(FPInt64.MinValue, out _), Is.False);
            Assert.That(() => FPInt64.Abs(FPInt64.MinValue), Throws.TypeOf<OverflowException>());
            Assert.That(FPInt64.TryCeil(FPInt64.MaxValue, out _), Is.False);
            Assert.That(() => FPInt64.Ceil(FPInt64.MaxValue), Throws.TypeOf<OverflowException>());
            Assert.That(FPInt64.TryRound(FPInt64.MaxValue, out _), Is.False);
            Assert.That(() => FPInt64.Round(FPInt64.MaxValue), Throws.TypeOf<OverflowException>());
            Assert.That(FPInt64.TrySqrt(FPInt64.MinusOne, out _), Is.False);
            Assert.That(() => FPInt64.Sqrt(FPInt64.MinusOne), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void FloorCeilAndRound_UseDocumentedNegativeSemantics()
        {
            FPInt64 minusOnePointSeven = FPInt64.FromDouble(-1.7);
            FPInt64 minusOnePointFive = FPInt64.FromDouble(-1.5);
            FPInt64 plusOnePointFive = FPInt64.FromDouble(1.5);

            Assert.That(FPInt64.Floor(minusOnePointSeven), Is.EqualTo(FPInt64.FromInt(-2)));
            Assert.That(FPInt64.Ceil(minusOnePointSeven), Is.EqualTo(FPInt64.FromInt(-1)));
            Assert.That(FPInt64.Round(minusOnePointFive), Is.EqualTo(FPInt64.FromInt(-2)));
            Assert.That(FPInt64.Round(plusOnePointFive), Is.EqualTo(FPInt64.FromInt(2)));
        }

        [Test]
        public void Sqrt_ReturnsFloorOfExactQ32Root()
        {
            long[] sourceRawValues =
            {
                0L,
                1L,
                FPInt64.RAW_ONE,
                FPInt64.FromInt(2).RawValue,
                FPInt64.FromInt(4).RawValue,
                long.MaxValue,
            };

            foreach (long sourceRaw in sourceRawValues)
            {
                FPInt64 root = FPInt64.Sqrt(FPInt64.FromRaw(sourceRaw));
                BigInteger target = new BigInteger(sourceRaw) << FPInt64.FractionalBits;
                BigInteger rootSquared = new BigInteger(root.RawValue) * root.RawValue;
                BigInteger nextSquared = new BigInteger(root.RawValue + 1L) * (root.RawValue + 1L);

                Assert.That(rootSquared <= target, Is.True, $"Source raw {sourceRaw} lower bound.");
                Assert.That(nextSquared > target, Is.True, $"Source raw {sourceRaw} upper bound.");
            }
        }

        [Test]
        public void Lerp_ClampsWhileLerpUnclampedExtrapolates()
        {
            FPInt64 start = FPInt64.FromInt(10);
            FPInt64 end = FPInt64.FromInt(20);

            Assert.That(FPInt64.Lerp(start, end, FPInt64.FromInt(-1)), Is.EqualTo(start));
            Assert.That(FPInt64.Lerp(start, end, FPInt64.FromInt(2)), Is.EqualTo(end));
            Assert.That(
                FPInt64.LerpUnclamped(start, end, FPInt64.FromInt(2)),
                Is.EqualTo(FPInt64.FromInt(30)));
        }

        private static BigInteger MultiplyOracle(long aRaw, long bRaw)
        {
            bool negative = (aRaw < 0) ^ (bRaw < 0);
            BigInteger magnitude = (BigInteger.Abs(new BigInteger(aRaw)) * BigInteger.Abs(new BigInteger(bRaw))) >>
                                   FPInt64.FractionalBits;
            return negative ? -magnitude : magnitude;
        }

        private static bool IsInt64(BigInteger value)
        {
            return value >= long.MinValue && value <= long.MaxValue;
        }

        private static long WrapToInt64(BigInteger value)
        {
            BigInteger modulus = BigInteger.One << 64;
            BigInteger wrapped = value % modulus;
            if (wrapped.Sign < 0)
            {
                wrapped += modulus;
            }

            return unchecked((long)(ulong)wrapped);
        }

        private static long NextInt64(Random random)
        {
            var bytes = new byte[sizeof(long)];
            random.NextBytes(bytes);
            return BitConverter.ToInt64(bytes, 0);
        }
    }
}
