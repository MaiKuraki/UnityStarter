using System;

namespace CycloneGames.EventBus.Editor
{
    /// <summary>
    /// Pure layout math for the debugger's three tables. One flexible column absorbs the surplus
    /// viewport width; when the window is too narrow, the remaining columns are squeezed in
    /// proportion to each one's slack above its own minimum, so the table keeps fitting until every
    /// column has reached its minimum.
    ///
    /// Free of any UnityEngine dependency: this arithmetic is the part of the window that is unit
    /// tested and mirrored outside the Editor, which keeps the sizing rules verifiable without a GUI.
    /// </summary>
    public static class EventBusTableLayout
    {
        // Invariant: the absorbing (flexible) column MUST be the LAST column, so it sits to the right of
        // every boundary a user can drag; the dragged edge then follows the mouse instead of pulling the
        // content left of it. Buses and Issues have no natural trailing column, so they end in an
        // invisible filler (empty header, no tooltip, min 0, nothing drawn).

        // Buses: 10 real columns + 1 filler; the flexible column is the filler.
        public static readonly float[] BusDefaults = { 260f, 52f, 46f, 60f, 92f, 58f, 60f, 48f, 48f, 52f, 0f };
        public static readonly float[] BusMins = { 120f, 44f, 36f, 46f, 60f, 44f, 48f, 40f, 40f, 44f, 0f };
        public static readonly float[] BusMaxes = { 780f, 104f, 92f, 120f, 184f, 116f, 120f, 96f, 96f, 104f, 0f };
        public const int BusFlex = 10;

        // Text columns are left-aligned (header and content) and their maximum width is Defaults x 3
        // (vs x 2 for every other column) so that "drag to max" fits typical long text.
        public static readonly int[] BusTextColumns = { 0 };

        // Issues: 8 real columns + 1 filler; the flexible column is the filler. Defaults are tuned so
        // the table fits a typical window (~1050 px of viewport) with no squeeze.
        public static readonly float[] IssueDefaults = { 62f, 100f, 150f, 190f, 200f, 46f, 200f, 80f, 0f };
        public static readonly float[] IssueMins = { 48f, 96f, 120f, 150f, 180f, 44f, 200f, 72f, 0f };
        public static readonly float[] IssueMaxes = { 124f, 300f, 450f, 570f, 600f, 92f, 600f, 160f, 0f };
        public const int IssueFlex = 8;
        public static readonly int[] IssueTextColumns = { 1, 2, 3, 4, 6 };

        // Containers: the flexible column is "Note", which is already the last column (no filler).
        public static readonly float[] ContainerDefaults = { 250f, 52f, 56f, 78f, 78f, 78f, 58f, 200f };
        public static readonly float[] ContainerMins = { 220f, 44f, 50f, 70f, 70f, 70f, 54f, 200f };
        public static readonly float[] ContainerMaxes = { 750f, 104f, 112f, 156f, 156f, 156f, 116f, 600f };
        public const int ContainerFlex = 7;
        public static readonly int[] ContainerTextColumns = { 0, 7 };

        /// <summary>True when <paramref name="column"/> is one of the table's text columns.</summary>
        public static bool IsTextColumn(int[] textColumns, int column)
        {
            for (int index = 0; index < textColumns.Length; index++)
            {
                if (textColumns[index] == column)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Computes the display widths for one table and returns the total table width.
        ///
        /// <paramref name="widths"/> is the user's per-column width and is never written; the result
        /// goes into <paramref name="destination"/>, a per-window scratch buffer of equal length. Not
        /// writing back the source is what makes "narrow the window, then widen it" restore the user's
        /// drag sizes.
        /// </summary>
        /// <param name="viewport">Usable width for the table, already floored to the minimum the
        /// caller allows. Injected so this method has no dependency on the Editor.</param>
        public static float LayoutColumns(
            float[] widths,
            float[] mins,
            int flexColumn,
            float viewport,
            float[] destination)
        {
            if (widths == null)
            {
                throw new ArgumentNullException(nameof(widths));
            }

            if (mins == null)
            {
                throw new ArgumentNullException(nameof(mins));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (widths.Length != mins.Length || widths.Length != destination.Length)
            {
                throw new ArgumentException("widths, mins and destination must have equal length.");
            }

            if (flexColumn < 0 || flexColumn >= widths.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(flexColumn));
            }

            for (int index = 0; index < widths.Length; index++)
            {
                destination[index] = widths[index];
            }

            float others = 0f;
            for (int index = 0; index < destination.Length; index++)
            {
                if (index != flexColumn)
                {
                    others += destination[index];
                }
            }

            // Stage 1: the flexible column absorbs the whole surplus, if it can reach it.
            float flexTarget = viewport - others;
            if (flexTarget >= mins[flexColumn])
            {
                destination[flexColumn] = flexTarget;
                return viewport; // Σ == viewport
            }

            // Stage 2: the flexible column is pinned at its minimum; squeeze the other columns in
            // proportion to their slack above their own minimums, until the deficit is absorbed.
            destination[flexColumn] = mins[flexColumn];

            float deficit = (others + mins[flexColumn]) - viewport;
            if (deficit > 0f)
            {
                float slackTotal = 0f;
                for (int index = 0; index < destination.Length; index++)
                {
                    if (index == flexColumn)
                    {
                        continue;
                    }

                    float slack = destination[index] - mins[index];
                    if (slack > 0f)
                    {
                        slackTotal += slack;
                    }
                }

                if (slackTotal > 0f)
                {
                    float ratio = deficit / slackTotal;
                    if (ratio > 1f)
                    {
                        ratio = 1f;
                    }

                    for (int index = 0; index < destination.Length; index++)
                    {
                        if (index == flexColumn)
                        {
                            continue;
                        }

                        float slack = destination[index] - mins[index];
                        if (slack > 0f)
                        {
                            destination[index] -= slack * ratio;
                        }
                    }
                }
            }

            // If every column is at its minimum and the sum still exceeds the viewport, the table is
            // genuinely wider than the window: return the larger sum so a scrollbar appears.
            float total = 0f;
            for (int index = 0; index < destination.Length; index++)
            {
                total += destination[index];
            }

            return total > viewport ? total : viewport;
        }

    }
}
