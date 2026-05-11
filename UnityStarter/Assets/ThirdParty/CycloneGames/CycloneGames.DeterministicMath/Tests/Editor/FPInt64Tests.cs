using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPInt64Tests
    {
        private static readonly FPInt64 Epsilon = FPInt64.FromRaw(100); // ~2.3e-8

        [Test]
        public void Zero_Equals_Zero()
        {
            Assert.That(FPInt64.Zero.RawValue, Is.EqualTo(0));
        }

        [Test]
        public void FromInt_RoundTrips_Int()
        {
            Assert.That(FPInt64.FromInt(42).ToInt(), Is.EqualTo(42));
            Assert.That(FPInt64.FromInt(-7).ToInt(), Is.EqualTo(-7));
        }

        [Test]
        public void FromString_Parses_Invariant_Decimal()
        {
            var parsed = FPInt64.FromString("-12.5");
            var expected = FPInt64.FromInt(-12) - FPInt64.FromRaw(FPInt64.One >> 1);

            Assert.That(parsed.RawValue, Is.EqualTo(expected.RawValue));
        }

        [Test]
        public void TryParse_Rejects_Invalid_Input()
        {
            Assert.That(FPInt64.TryParse("1,5", out _), Is.False);
            Assert.That(FPInt64.TryParse("abc", out _), Is.False);
        }

        [Test]
        public void TryParse_Allows_Minimum_Integer()
        {
            Assert.That(FPInt64.TryParse("-2147483648", out var value), Is.True);
            Assert.That(value.RawValue, Is.EqualTo(long.MinValue));
        }

        [Test]
        public void OneValue_Multiplied_Equals_Identity()
        {
            var v = FPInt64.FromInt(5);
            Assert.That((v * FPInt64.OneValue).RawValue, Is.EqualTo(v.RawValue));
        }

        [Test]
        public void Addition_Is_Commutative()
        {
            var a = FPInt64.FromFloat(3.14f);
            var b = FPInt64.FromFloat(2.72f);
            Assert.That((a + b).RawValue, Is.EqualTo((b + a).RawValue));
        }

        [Test]
        public void Multiplication_Preserves_Sign()
        {
            var pos = FPInt64.FromInt(3);
            var neg = FPInt64.FromInt(-3);
            Assert.That((pos * neg).RawValue, Is.LessThan(0));
            Assert.That((neg * neg).RawValue, Is.GreaterThan(0));
        }

        [Test]
        public void Division_By_Zero_Throws()
        {
            Assert.That(() => FPInt64.OneValue / FPInt64.Zero, Throws.TypeOf<System.DivideByZeroException>());
        }

        [Test]
        public void Division_By_Int_PowerOfTwo_Is_Correct()
        {
            var value = FPInt64.FromInt(100);
            var result = value / FPInt64.FromInt(2);
            Assert.That(result.RawValue, Is.EqualTo(FPInt64.FromInt(50).RawValue));
        }

        [Test]
        public void Division_By_Fractional_PowerOfTwo_Is_Correct()
        {
            var value = FPInt64.FromInt(5);
            var half = FPInt64.FromRaw(FPInt64.One >> 1);
            var result = value / half;
            Assert.That(result.RawValue, Is.EqualTo(FPInt64.FromInt(10).RawValue));
        }

        [Test]
        public void Division_By_Negative_PowerOfTwo_Is_Correct()
        {
            var value = FPInt64.FromInt(100);
            var result = value / FPInt64.FromInt(-2);
            Assert.That(result.RawValue, Is.EqualTo(FPInt64.FromInt(-50).RawValue));
        }

        [Test]
        public void Sqrt_Four_Equals_Two()
        {
            var four = FPInt64.FromInt(4);
            var two = FPInt64.FromInt(2);
            var result = FPInt64.Sqrt(four);
            Assert.That(FPInt64.Abs(result - two).RawValue, Is.LessThan(Epsilon.RawValue));
        }

        [Test]
        public void Sqrt_Two_Approximately_Correct()
        {
            // sqrt(2) is approximately 1.41421356237 in Q32.32.
            var two = FPInt64.FromInt(2);
            var expected = FPInt64.FromRaw(6074000999L);
            var result = FPInt64.Sqrt(two);
            var diff = FPInt64.Abs(result - expected);
            Assert.That(diff.RawValue, Is.LessThan(Epsilon.RawValue * 10));
        }

        [Test]
        public void Floor_Ceil_Round_NegativeValues_Are_Correct()
        {
            var minusOnePointSeven = FPInt64.FromRaw(-FPInt64.One - (FPInt64.One * 7 / 10));
            var minusOnePointThree = FPInt64.FromRaw(-FPInt64.One - (FPInt64.One * 3 / 10));
            var minusOnePointFive = FPInt64.FromRaw(-FPInt64.One - FPInt64.Half);

            Assert.That(FPInt64.Floor(minusOnePointSeven).RawValue, Is.EqualTo(FPInt64.FromInt(-2).RawValue));
            Assert.That(FPInt64.Ceil(minusOnePointThree).RawValue, Is.EqualTo(FPInt64.FromInt(-1).RawValue));
            Assert.That(FPInt64.Round(minusOnePointFive).RawValue, Is.EqualTo(FPInt64.FromInt(-2).RawValue));
        }

        [Test]
        public void Constants_Are_Consistent()
        {
            Assert.That(FPInt64.Abs((FPInt64.HalfPi * 2) - FPInt64.Pi).RawValue, Is.LessThanOrEqualTo(1));
            Assert.That(FPInt64.Abs((FPInt64.TwoPi / 2) - FPInt64.Pi).RawValue, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void Lerp_T_Zero_Returns_A()
        {
            var a = FPInt64.FromInt(10);
            var b = FPInt64.FromInt(20);
            Assert.That(FPInt64.Lerp(a, b, FPInt64.Zero).RawValue, Is.EqualTo(a.RawValue));
        }

        [Test]
        public void Lerp_T_One_Returns_B()
        {
            var a = FPInt64.FromInt(10);
            var b = FPInt64.FromInt(20);
            Assert.That(FPInt64.Lerp(a, b, FPInt64.OneValue).RawValue, Is.EqualTo(b.RawValue));
        }
    }
}
