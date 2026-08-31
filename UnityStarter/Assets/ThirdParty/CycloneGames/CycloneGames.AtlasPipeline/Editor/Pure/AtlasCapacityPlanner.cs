using System;
using System.Collections.Generic;
using System.Globalization;

namespace CycloneGames.AtlasPipeline.Pure
{
    /// <summary>
    /// Per-atlas capacity budget and page planning.
    /// Unity does not report "this atlas did not fit" as an error: sprites that exceed the atlas max
    /// texture size are silently dropped from the packed result and surface much later as white quads
    /// at runtime. With tens of thousands of images that is close to untraceable, so capacity is
    /// evaluated up front and reported as a build-blocking error instead of a runtime mystery.
    /// </summary>
    public readonly struct AtlasCapacityRequest
    {
        public AtlasCapacityRequest(
            int spriteCount,
            long requiredArea,
            int maxTextureSize,
            int padding,
            float packingEfficiency = AtlasCapacityPlanner.DefaultPackingEfficiency)
        {
            SpriteCount = spriteCount < 0 ? 0 : spriteCount;
            RequiredArea = requiredArea < 0L ? 0L : requiredArea;
            MaxTextureSize = maxTextureSize <= 0 ? 2048 : maxTextureSize;
            Padding = padding < 0 ? 0 : padding;
            PackingEfficiency = packingEfficiency <= 0f
                ? AtlasCapacityPlanner.DefaultPackingEfficiency
                : (packingEfficiency > 1f ? 1f : packingEfficiency);
        }

        public int SpriteCount { get; }
        public long RequiredArea { get; }
        public int MaxTextureSize { get; }
        public int Padding { get; }
        public float PackingEfficiency { get; }
    }

    public readonly struct AtlasCapacityReport
    {
        internal AtlasCapacityReport(
            long usableAreaPerPage,
            long requiredArea,
            int pageCount,
            float utilization)
        {
            UsableAreaPerPage = usableAreaPerPage;
            RequiredArea = requiredArea;
            PageCount = pageCount;
            Utilization = utilization;
        }

        /// <summary>Usable pixels on one page: maxSize squared, scaled by packing efficiency.</summary>
        public long UsableAreaPerPage { get; }

        public long RequiredArea { get; }

        /// <summary>Pages needed to hold <see cref="RequiredArea"/>. Always at least 1 when non-empty.</summary>
        public int PageCount { get; }

        /// <summary>RequiredArea over the capacity of the allocated pages, in the range 0..1+.</summary>
        public float Utilization { get; }

        public bool IsEmpty => RequiredArea <= 0L;

        /// <summary>True when the content needs more than one page, i.e. the atlas will be split.</summary>
        public bool RequiresSplitting => PageCount > 1;

        public long TotalCapacity => UsableAreaPerPage * (PageCount <= 0 ? 0 : PageCount);
    }

    public static class AtlasCapacityPlanner
    {
        /// <summary>
        /// Empirically achievable fill rate for a rectangle packer with rotation and tight packing
        /// enabled. The real value depends on the size distribution of the source art; 0.85 is a
        /// conservative default that avoids under-splitting. Projects with wildly mixed sprite sizes
        /// should lower it, because those pack worse than uniform sheets.
        /// </summary>
        public const float DefaultPackingEfficiency = 0.85f;

        /// <summary>
        /// Padded area occupied by one sprite. Padding is applied to both axes on both sides because
        /// the packer inserts it between neighbours, so each sprite effectively reserves
        /// (w + 2 * padding) x (h + 2 * padding) unless it sits on the atlas border.
        /// </summary>
        public static long ComputePaddedArea(int width, int height, int padding)
        {
            if (width <= 0 || height <= 0)
            {
                return 0L;
            }

            int pad = padding < 0 ? 0 : padding;
            long w = (long)width + (pad * 2L);
            long h = (long)height + (pad * 2L);
            return w * h;
        }

        /// <summary>
        /// True when a sprite can never be packed at this atlas size regardless of how empty the
        /// atlas is. These are the sprites Unity drops silently, so they are reported separately from
        /// the aggregate "does not fit" case.
        /// </summary>
        public static bool IsSpriteTooLarge(int width, int height, int maxTextureSize, int padding)
        {
            if (maxTextureSize <= 0)
            {
                return false;
            }

            int pad = padding < 0 ? 0 : padding;
            return width + (pad * 2) > maxTextureSize
                   || height + (pad * 2) > maxTextureSize;
        }

        public static AtlasCapacityReport Evaluate(in AtlasCapacityRequest request)
        {
            if (request.RequiredArea <= 0L || request.SpriteCount <= 0)
            {
                return new AtlasCapacityReport(0L, 0L, 0, 0f);
            }

            long side = request.MaxTextureSize;
            long usableAreaPerPage = (long)(side * side * request.PackingEfficiency);
            if (usableAreaPerPage <= 0L)
            {
                usableAreaPerPage = 1L;
            }

            int pageCount = (int)((request.RequiredArea + usableAreaPerPage - 1L)
                                  / usableAreaPerPage);
            if (pageCount < 1)
            {
                pageCount = 1;
            }

            float utilization = (float)((double)request.RequiredArea
                                        / (usableAreaPerPage * (double)pageCount));
            return new AtlasCapacityReport(
                usableAreaPerPage,
                request.RequiredArea,
                pageCount,
                utilization);
        }

        /// <summary>
        /// Splits <paramref name="totalCount"/> ordered members across <paramref name="pageCount"/>
        /// pages as evenly as possible, with the remainder distributed to the lowest page indices.
        /// Slicing the deterministic ordered list (rather than bucketing by hash) keeps related art
        /// together, because neighbouring paths land on the same page; it also makes the split
        /// trivially reproducible, which matters more here than minimizing churn when a sprite is
        /// added — an atlas has to be repacked on any membership change anyway.
        /// </summary>
        public static void AssignPageRange(
            int totalCount,
            int pageCount,
            int pageIndex,
            out int start,
            out int count)
        {
            start = 0;
            count = 0;

            if (totalCount <= 0 || pageCount <= 0 || pageIndex < 0 || pageIndex >= pageCount)
            {
                return;
            }

            if (pageCount == 1)
            {
                count = totalCount;
                return;
            }

            int perPage = totalCount / pageCount;
            int remainder = totalCount % pageCount;

            start = (pageIndex * perPage) + Math.Min(pageIndex, remainder);
            count = perPage + (pageIndex < remainder ? 1 : 0);
        }

        /// <summary>
        /// Page count for one atlas across every platform it ships on. The pages are shared, so the
        /// answer has to be the worst case: an atlas that fits on iOS at 2048px but needs three pages
        /// on an Android build capped at 1024px is three pages on both, or the packable lists would
        /// differ per platform and the outputs would not be reproducible.
        /// When paging is disabled the answer is always one; the caller reports the overflow instead.
        /// </summary>
        public static int ComputePageCount(
            int memberCount,
            long requiredArea,
            IReadOnlyList<int> platformMaxSizes,
            int padding,
            bool pagingEnabled)
        {
            if (!pagingEnabled
                || memberCount <= 0
                || requiredArea <= 0L
                || platformMaxSizes == null
                || platformMaxSizes.Count == 0)
            {
                return 1;
            }

            int worst = 1;
            for (int i = 0; i < platformMaxSizes.Count; i++)
            {
                AtlasCapacityReport report = Evaluate(
                    new AtlasCapacityRequest(memberCount, requiredArea, platformMaxSizes[i], padding));
                if (report.PageCount > worst)
                {
                    worst = report.PageCount;
                }
            }

            return worst;
        }

        /// <summary>
        /// Output key for one page of an atlas. A single-page atlas keeps the plain key, so enabling
        /// paging changes nothing for any atlas that already fits — only an atlas that would
        /// otherwise fail the build starts producing paged files.
        /// The page suffix is fixed-width and zero-padded, so page 7 never becomes page 10's name
        /// with a digit missing, and every machine derives the same file name from the same index.
        /// </summary>
        public static string BuildPageKey(string atlasKey, int pageIndex, int pageCount)
        {
            if (string.IsNullOrEmpty(atlasKey) || pageCount <= 1 || pageIndex < 0)
            {
                return atlasKey;
            }

            int lastPageIndex = pageCount - 1;
            int width = Math.Max(3, lastPageIndex == 0 ? 1 : PageDigitCount(lastPageIndex));
            return atlasKey + "__p" + pageIndex.ToString(
                "D" + width.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Strips a page suffix from an atlas file stem, so a page-unaware consumer (the orphan sweep)
        /// can recognize every page of a known atlas as expected. Returns the base key, or the input
        /// unchanged when it carries no page suffix.
        /// </summary>
        public static string StripPageSuffix(string atlasKey)
        {
            if (string.IsNullOrEmpty(atlasKey))
            {
                return atlasKey;
            }

            int marker = atlasKey.LastIndexOf("__p", StringComparison.Ordinal);
            if (marker <= 0 || marker + 3 >= atlasKey.Length)
            {
                return atlasKey;
            }

            for (int i = marker + 3; i < atlasKey.Length; i++)
            {
                if (atlasKey[i] < '0' || atlasKey[i] > '9')
                {
                    return atlasKey;
                }
            }

            return atlasKey.Substring(0, marker);
        }

        /// <summary>
        /// True when the key is a page of the given base atlas key — "ui__p000" is a page of "ui".
        /// The base key itself is not a page of itself: the suffix has to actually have been present.
        /// </summary>
        public static bool IsPageOf(string atlasKey, string baseKey)
        {
            if (string.IsNullOrEmpty(atlasKey) || string.IsNullOrEmpty(baseKey))
            {
                return false;
            }

            string stripped = StripPageSuffix(atlasKey);
            return !string.Equals(stripped, atlasKey, StringComparison.Ordinal)
                   && string.Equals(stripped, baseKey, StringComparison.Ordinal);
        }

        private static int PageDigitCount(int value)
        {
            int digits = 1;
            while (value >= 10)
            {
                value /= 10;
                digits++;
            }

            return digits;
        }

        /// <summary>
        /// Rough compressed-size estimate in bytes, used only for reporting and CI budgets. It is a
        /// planning number, not a promise: real output depends on the platform encoder.
        /// </summary>
        public static long EstimateBytes(long pixelArea, double bytesPerPixel)
        {
            if (pixelArea <= 0L || bytesPerPixel <= 0d)
            {
                return 0L;
            }

            return (long)(pixelArea * bytesPerPixel);
        }
    }
}
