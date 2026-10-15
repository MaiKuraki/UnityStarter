using System;
using NUnit.Framework;
using CycloneGames.EventBus.Editor;

namespace CycloneGames.EventBus.Tests
{
    /// <summary>
    /// Unit tests for the table layout arithmetic (EventBusTableLayout.LayoutColumns). The GUI cannot be
    /// verified in batchmode, but the sizing math is a pure function and can be pinned here:
    ///
    ///   total == max(viewport, Σ min)   // fit exactly, or fall back to a scrollbar
    ///   no column is ever narrower than its minimum
    ///   the source (user) width array is never written
    /// </summary>
    public sealed class EventBusTableLayoutTests
    {
        private const float Tolerance = 0.01f;

        private struct Table
        {
            public string Name;
            public float[] Defaults;
            public float[] Mins;
            public float[] Maxes;
            public int[] TextColumns;
            public int Flex;
        }

        private static Table[] AllTables()
        {
            return new[]
            {
                new Table
                {
                    Name = "Buses",
                    Defaults = EventBusTableLayout.BusDefaults,
                    Mins = EventBusTableLayout.BusMins,
                    Maxes = EventBusTableLayout.BusMaxes,
                    TextColumns = EventBusTableLayout.BusTextColumns,
                    Flex = EventBusTableLayout.BusFlex,
                },
                new Table
                {
                    Name = "Issues",
                    Defaults = EventBusTableLayout.IssueDefaults,
                    Mins = EventBusTableLayout.IssueMins,
                    Maxes = EventBusTableLayout.IssueMaxes,
                    TextColumns = EventBusTableLayout.IssueTextColumns,
                    Flex = EventBusTableLayout.IssueFlex,
                },
                new Table
                {
                    Name = "Containers",
                    Defaults = EventBusTableLayout.ContainerDefaults,
                    Mins = EventBusTableLayout.ContainerMins,
                    Maxes = EventBusTableLayout.ContainerMaxes,
                    TextColumns = EventBusTableLayout.ContainerTextColumns,
                    Flex = EventBusTableLayout.ContainerFlex,
                },
            };
        }

        private static float Sum(float[] values)
        {
            float total = 0f;
            for (int index = 0; index < values.Length; index++)
            {
                total += values[index];
            }

            return total;
        }

        private static void AssertClose(float expected, float actual, string message)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(Tolerance), message);
        }

        private static void AssertAtLeast(float actual, float minimum, string message)
        {
            Assert.That(actual >= minimum - Tolerance, Is.True,
                message + $" (was {actual}, minimum {minimum})");
        }

        private static void AssertAtMost(float actual, float maximum, string message)
        {
            Assert.That(actual <= maximum + Tolerance, Is.True,
                message + $" (was {actual}, maximum {maximum})");
        }

        // ---------------------------------------------------------------- 1. wide viewport

        [Test]
        public void WideViewport_FillsExactly_AndLeavesUserColumnsUntouched()
        {
            const float viewport = 1600f;

            foreach (Table table in AllTables())
            {
                var render = new float[table.Defaults.Length];
                float total = EventBusTableLayout.LayoutColumns(table.Defaults, table.Mins, table.Flex, viewport, render);

                AssertClose(viewport, total, $"{table.Name}: total must equal the viewport.");
                AssertClose(viewport, Sum(render), $"{table.Name}: Σ render must equal the viewport.");

                float others = 0f;
                for (int index = 0; index < render.Length; index++)
                {
                    if (index != table.Flex)
                    {
                        AssertClose(table.Defaults[index], render[index],
                            $"{table.Name}: non-flex column {index} must keep the user width.");
                        others += render[index];
                    }
                }

                AssertClose(viewport - others, render[table.Flex],
                    $"{table.Name}: the flexible column must absorb exactly the surplus.");
            }
        }

        // ---------------------------------------------------------------- 2. typical viewport

        [Test]
        public void TypicalViewport_NoOverflow_AndNoColumnBelowItsMinimum()
        {
            // 1050 px is the viewport of a ~1080 px window: the table must fit it exactly, with every
            // column at or above its minimum.
            const float viewport = 1050f;

            foreach (Table table in AllTables())
            {
                var render = new float[table.Defaults.Length];
                float total = EventBusTableLayout.LayoutColumns(table.Defaults, table.Mins, table.Flex, viewport, render);
                float minTotal = Sum(table.Mins);

                AssertAtMost(minTotal, viewport,
                    $"{table.Name}: defaults+minimums must fit the typical viewport (Σmin={minTotal}).");
                AssertClose(viewport, total, $"{table.Name}: the table must fit (no scrollbar).");
                AssertClose(viewport, Sum(render), $"{table.Name}: Σ render must equal the viewport.");

                for (int index = 0; index < render.Length; index++)
                {
                    AssertAtLeast(render[index], table.Mins[index],
                        $"{table.Name}: column {index} fell below its minimum.");
                }
            }
        }

        // ---------------------------------------------------------------- 3. narrow viewport

        [Test]
        public void NarrowViewport_SqueezesTowardTheViewport()
        {
            const float viewport = 900f;

            foreach (Table table in AllTables())
            {
                var render = new float[table.Defaults.Length];
                float total = EventBusTableLayout.LayoutColumns(table.Defaults, table.Mins, table.Flex, viewport, render);
                float minTotal = Sum(table.Mins);
                float expected = Math.Max(viewport, minTotal);

                // Buses/Containers fit at 900; Issues (Σmin 910) cannot, and correctly keeps its
                // scrollbar width instead of squeezing below the minimums.
                AssertClose(expected, total, $"{table.Name}: total must be max(viewport, Σmin).");
                AssertClose(expected, Sum(render), $"{table.Name}: Σ render must equal the total.");

                for (int index = 0; index < render.Length; index++)
                {
                    AssertAtLeast(render[index], table.Mins[index],
                        $"{table.Name}: column {index} fell below its minimum.");
                    Assert.That(float.IsNaN(render[index]) || float.IsInfinity(render[index]), Is.False,
                        $"{table.Name}: column {index} is not finite.");
                }

                if (viewport >= minTotal)
                {
                    // The squeeze fits: excess must be removed from non-flex slack, never added.
                    for (int index = 0; index < render.Length; index++)
                    {
                        if (index != table.Flex)
                        {
                            AssertAtMost(render[index], table.Defaults[index],
                                $"{table.Name}: squeezed column {index} grew.");
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- 4. extreme viewport

        [Test]
        public void ExtremeNarrowViewport_PinsEveryColumnAtItsMinimum()
        {
            const float viewport = 500f;

            foreach (Table table in AllTables())
            {
                var render = new float[table.Defaults.Length];
                float total = EventBusTableLayout.LayoutColumns(table.Defaults, table.Mins, table.Flex, viewport, render);
                float minTotal = Sum(table.Mins);

                Assert.That(minTotal > viewport, Is.True,
                    $"{table.Name}: premise — Σmin should exceed 500 here (Σmin={minTotal}).");
                AssertAtLeast(total, viewport, $"{table.Name}: never narrower than the viewport.");
                AssertClose(minTotal, total, $"{table.Name}: everything pinned at minimum.");

                for (int index = 0; index < render.Length; index++)
                {
                    AssertClose(table.Mins[index], render[index],
                        $"{table.Name}: column {index} must be pinned at its minimum.");
                }
            }
        }

        // ---------------------------------------------------------------- 5. flex invariant

        [Test]
        public void FlexibleColumn_NeverBelowItsMinimum_AcrossViewports()
        {
            foreach (Table table in AllTables())
            {
                for (float viewport = 400f; viewport <= 2400f; viewport += 50f)
                {
                    var render = new float[table.Defaults.Length];
                    float total = EventBusTableLayout.LayoutColumns(table.Defaults, table.Mins, table.Flex, viewport, render);

                    AssertAtLeast(render[table.Flex], table.Mins[table.Flex],
                        $"{table.Name}@{viewport}: flexible column below its minimum.");
                    AssertClose(Math.Max(viewport, Sum(table.Mins)), total,
                        $"{table.Name}@{viewport}: unexpected total.");
                }
            }
        }

        // ---------------------------------------------------------------- 6. no write-back

        [Test]
        public void SourceUserWidths_AreNeverModified()
        {
            foreach (Table table in AllTables())
            {
                var userWidths = (float[])table.Defaults.Clone();
                var before = (float[])userWidths.Clone();
                var render = new float[userWidths.Length];

                // A viewport small enough to force squeezing.
                EventBusTableLayout.LayoutColumns(userWidths, table.Mins, table.Flex, 700f, render);

                for (int index = 0; index < userWidths.Length; index++)
                {
                    AssertClose(before[index], userWidths[index],
                        $"{table.Name}: the source width array was written (index {index}); a narrow window "
                        + "would then permanently lose the user's drag sizes.");
                }
            }
        }

        // ---------------------------------------------------------------- 7. structural invariant

        [Test]
        public void FlexColumn_IsTheLastColumn()
        {
            foreach (Table table in AllTables())
            {
                Assert.AreEqual(table.Defaults.Length - 1, table.Flex,
                    $"{table.Name}: the absorbing column MUST be the last column, so a dragged boundary "
                    + "moves with the mouse instead of pulling the content left of it.");
            }
        }

        [Test]
        public void MaxWidths_AreAtLeastDefaults()
        {
            foreach (Table table in AllTables())
            {
                Assert.AreEqual(table.Defaults.Length, table.Mins.Length, $"{table.Name}: length mismatch (mins).");
                Assert.AreEqual(table.Defaults.Length, table.Maxes.Length, $"{table.Name}: length mismatch (maxes).");

                for (int index = 0; index < table.Defaults.Length; index++)
                {
                    AssertAtLeast(table.Maxes[index], table.Defaults[index],
                        $"{table.Name}: max[{index}] must be at least the default.");
                    AssertAtLeast(table.Defaults[index], table.Mins[index],
                        $"{table.Name}: default[{index}] must be at least the minimum.");
                }
            }
        }

        [Test]
        public void MaxWidths_TextColumnsTriple_OthersDouble()
        {
            // Text columns (Event Type / Subject / Where / Value / Fix / Container / Note) get x3 so
            // "drag to max" fits typical long text; every other column stays at x2.
            foreach (Table table in AllTables())
            {
                for (int index = 0; index < table.Defaults.Length; index++)
                {
                    float multiplier = EventBusTableLayout.IsTextColumn(table.TextColumns, index) ? 3f : 2f;
                    AssertClose(table.Defaults[index] * multiplier, table.Maxes[index],
                        $"{table.Name}: max[{index}] must be Defaults x {multiplier}.");
                }
            }
        }
    }
}
