#if DEBUG
using System;
using CycloneGames.Spatial.Core;
using CycloneGames.Spatial.Core.Internal;
using NUnit.Framework;

namespace CycloneGames.Spatial.Tests.Editor
{
    /// <summary>
    /// Proves the structure checker is not vacuous. A validator that has only ever returned <c>true</c> is
    /// indistinguishable from one that always returns <c>true</c>; each test here breaks a specific invariant and
    /// requires the validator to notice.
    /// </summary>
    /// <remarks>
    /// Compiled under the standard <c>DEBUG</c> symbol only, matching the corruption hook in Core. The hook is
    /// <c>internal</c>, so it is reachable here through <c>InternalsVisibleTo</c> and invisible to consumers.
    /// </remarks>
    public sealed class SpatialStructureDiagnosticsTests
    {
        private static SpatialHashGrid<int> BuildHealthyGrid()
        {
            var grid = new SpatialHashGrid<int>(new GridSettings(2f), 64);
            for (int i = 0; i < 120; i++)
            {
                grid.TryInsert(i, (i % 17) * 0.9f, (i % 13) * 1.1f, (i % 7) * 1.7f, out _);
            }

            return grid;
        }

        [Test]
        public void HealthyGrid_PassesValidation()
        {
            using (var grid = BuildHealthyGrid())
            {
                Assert.That(grid.Count, Is.EqualTo(120));
                Assert.That(grid.ValidateStructure(), Is.True, "A grid built through the public API must validate.");
            }
        }

        [Test]
        public void EmptyGrid_PassesValidation()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 8))
            {
                Assert.That(grid.ValidateStructure(), Is.True);
            }
        }

        [Test]
        public void EveryCorruption_IsDetected()
        {
            Array kinds = Enum.GetValues(typeof(SpatialCorruptionKind));
            Assert.That(kinds.Length, Is.GreaterThan(0), "There must be at least one corruption to inject.");

            foreach (SpatialCorruptionKind kind in kinds)
            {
                using (var grid = BuildHealthyGrid())
                {
                    Assert.That(grid.ValidateStructure(), Is.True, "Precondition failed for " + kind + ".");

                    Assert.That(grid.CorruptForTest(kind), Is.True, kind + " could not be applied.");

                    Assert.That(grid.ValidateStructure(), Is.False,
                        kind + " was not detected; the validator would accept a broken structure.");
                }
            }
        }

        [Test]
        public void CorruptingAHealthyGrid_DoesNotThrow()
        {
            using (var grid = BuildHealthyGrid())
            {
                Assert.DoesNotThrow(() => grid.CorruptForTest(SpatialCorruptionKind.BreakChainLink));
                // A cycle must terminate in the validator rather than hang the editor.
                Assert.DoesNotThrow(() => grid.ValidateStructure());
            }
        }

        [Test]
        public void CorruptionHook_IsRejectedAfterDispose()
        {
            var grid = BuildHealthyGrid();
            grid.Dispose();

            Assert.That(grid.CorruptForTest(SpatialCorruptionKind.BreakChainLink), Is.False,
                "A disposed grid has nothing to corrupt and must not report success.");
        }

        [Test]
        public void CorruptionHook_IsRejectedOnAnEmptyGrid()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 8))
            {
                Assert.That(grid.CorruptForTest(SpatialCorruptionKind.MismatchCellKey), Is.False);
            }
        }
    }
}
#endif
