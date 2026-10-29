using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.Spatial.Tests.Editor
{
    /// <summary>
    /// Coordinate value semantics. The hash must be stable across processes because the bucket layout of a grid with
    /// a known operation history is part of its observable behaviour.
    /// </summary>
    public sealed class CellCoordinateTests
    {
        [Test]
        public void Equality_ComparesAllThreeAxes()
        {
            var coordinate = new CellCoordinate(3, -4, 5);

            Assert.That(coordinate, Is.EqualTo(new CellCoordinate(3, -4, 5)));
            Assert.That(coordinate, Is.Not.EqualTo(new CellCoordinate(3, -4, 6)));
            Assert.That(coordinate, Is.Not.EqualTo(new CellCoordinate(4, -4, 5)));
            Assert.That(coordinate == new CellCoordinate(3, -4, 5), Is.True);
            Assert.That(coordinate != new CellCoordinate(3, -4, 6), Is.True);
        }

        [Test]
        public void Hash_IsStableForIdenticalCoordinates()
        {
            var first = new CellCoordinate(-17, 0, 42);
            var second = new CellCoordinate(-17, 0, 42);

            Assert.That(first.ComputeHash(), Is.EqualTo(second.ComputeHash()));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }

        [Test]
        public void Hash_DiffersForAxisPermutations()
        {
            // A hash that folds the axes positionally must not treat a permutation as the same cell.
            uint xyz = new CellCoordinate(1, 2, 3).ComputeHash();
            uint zyx = new CellCoordinate(3, 2, 1).ComputeHash();
            uint yxz = new CellCoordinate(2, 1, 3).ComputeHash();

            Assert.That(xyz, Is.Not.EqualTo(zyx));
            Assert.That(xyz, Is.Not.EqualTo(yxz));
        }

        [Test]
        public void Hash_HandlesNegativeAxesDistinctly()
        {
            Assert.That(new CellCoordinate(-1, 0, 0).ComputeHash(),
                Is.Not.EqualTo(new CellCoordinate(1, 0, 0).ComputeHash()));
            Assert.That(new CellCoordinate(0, -1, 0).ComputeHash(),
                Is.Not.EqualTo(new CellCoordinate(0, 0, -1).ComputeHash()));
        }

        [Test]
        public void Offset_ShiftsEveryAxis()
        {
            var origin = new CellCoordinate(10, -10, 0);
            Assert.That(origin.Offset(1, -1, 5), Is.EqualTo(new CellCoordinate(11, -11, 5)));
            Assert.That(origin.Offset(0, 0, 0), Is.EqualTo(origin));
        }

        [Test]
        public void Comparison_OrdersByXThenYThenZ()
        {
            var low = new CellCoordinate(0, 5, 5);
            var middle = new CellCoordinate(1, 0, 9);
            var high = new CellCoordinate(1, 1, 0);

            Assert.That(low.CompareTo(middle), Is.LessThan(0));
            Assert.That(middle.CompareTo(high), Is.LessThan(0));
            Assert.That(low.CompareTo(low), Is.Zero);
            Assert.That(high.CompareTo(middle), Is.GreaterThan(0));
        }
    }
}
