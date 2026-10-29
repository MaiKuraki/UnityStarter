using System;

namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Cell size and world-space origin that define how a spatial index partitions the world.
    /// </summary>
    /// <remarks>
    /// Cell index on each axis is <c>floor((world - origin) / cellSize)</c>, so a position of -0.5 maps to cell -1.
    /// The division runs in <c>double</c>, which keeps the mapping identical across platforms and scripting backends;
    /// do not reproduce it with float multiply-and-floor. The origin keeps float positions away from large magnitudes
    /// where their resolution degrades; see <c>SpatialHashGrid.SetOrigin</c>.
    /// </remarks>
    public readonly struct GridSettings
    {
        /// <summary>Edge length of one cell in world units. Always finite and greater than zero.</summary>
        public readonly float CellSize;

        /// <summary>World X coordinate that maps to cell index zero.</summary>
        public readonly float OriginX;

        /// <summary>World Y coordinate that maps to cell index zero.</summary>
        public readonly float OriginY;

        /// <summary>World Z coordinate that maps to cell index zero.</summary>
        public readonly float OriginZ;

        private readonly double _inverseCellSize;

        /// <summary>
        /// Creates grid settings.
        /// </summary>
        /// <param name="cellSize">Edge length in world units. Must be finite and greater than zero.</param>
        /// <param name="originX">World X that maps to cell index zero.</param>
        /// <param name="originY">World Y that maps to cell index zero.</param>
        /// <param name="originZ">World Z that maps to cell index zero.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="cellSize"/> is zero, negative, NaN or infinite, or an origin component is not finite.
        /// </exception>
        public GridSettings(float cellSize, float originX = 0f, float originY = 0f, float originZ = 0f)
        {
            if (!IsFinite(cellSize) || cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be finite and greater than zero.");
            }

            if (!IsFinite(originX) || !IsFinite(originY) || !IsFinite(originZ))
            {
                throw new ArgumentOutOfRangeException(nameof(originX), "Grid origin must be finite on every axis.");
            }

            CellSize = cellSize;
            OriginX = originX;
            OriginY = originY;
            OriginZ = originZ;
            _inverseCellSize = 1d / cellSize;
        }

        /// <summary>Reciprocal of <see cref="CellSize"/> in double precision. Provided for caller-side math.</summary>
        public double InverseCellSize
        {
            get { return _inverseCellSize; }
        }

        /// <summary>Returns settings with the same cell size and a new origin.</summary>
        public GridSettings WithOrigin(float originX, float originY, float originZ)
        {
            return new GridSettings(CellSize, originX, originY, originZ);
        }

        /// <summary>
        /// Maps a world position to its cell.
        /// </summary>
        /// <remarks>
        /// <para>Complexity: O(1). Non-finite input is clamped to the representable index range.</para>
        /// <para>
        /// Divides rather than multiplying by a reciprocal. IEEE division is correctly rounded, so an exact cell
        /// boundary maps to the exact integer and floors to the cell that owns it. Multiplying by a precomputed
        /// reciprocal introduces a half-ulp error that can push a boundary position into the previous cell.
        /// </para>
        /// </remarks>
        public CellCoordinate WorldToCell(float x, float y, float z)
        {
            float cellSize = CellSize;
            return new CellCoordinate(
                FloorToIndex(((double)x - OriginX) / cellSize),
                FloorToIndex(((double)y - OriginY) / cellSize),
                FloorToIndex(((double)z - OriginZ) / cellSize));
        }

        /// <summary>World X of the cell's minimum corner.</summary>
        public float CellMinX(int cellX)
        {
            return (float)((cellX * (double)CellSize) + OriginX);
        }

        /// <summary>World Y of the cell's minimum corner.</summary>
        public float CellMinY(int cellY)
        {
            return (float)((cellY * (double)CellSize) + OriginY);
        }

        /// <summary>World Z of the cell's minimum corner.</summary>
        public float CellMinZ(int cellZ)
        {
            return (float)((cellZ * (double)CellSize) + OriginZ);
        }

        /// <summary>
        /// Computes the inclusive cell range that covers a sphere.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A partition conversion, not a geometric test: callers still test each candidate. The range is exact, with
        /// no padding, because <see cref="WorldToCell"/> is monotone and <c>center ± radius</c> is exact in double. A
        /// position inside the sphere therefore cannot fall outside the range.
        /// </para>
        /// <para>Complexity: O(1).</para>
        /// </remarks>
        /// <param name="centerX">Sphere center on X.</param>
        /// <param name="centerY">Sphere center on Y.</param>
        /// <param name="centerZ">Sphere center on Z.</param>
        /// <param name="radius">Sphere radius. Negative or non-finite values produce an empty range.</param>
        /// <param name="minCell">Inclusive lower corner of the cell range.</param>
        /// <param name="maxCell">Inclusive upper corner of the cell range.</param>
        /// <returns><c>false</c> when the range is empty or the inputs are not usable; the outputs are then zero.</returns>
        public bool CellRangeForSphere(
            float centerX,
            float centerY,
            float centerZ,
            float radius,
            out CellCoordinate minCell,
            out CellCoordinate maxCell)
        {
            if (!IsFinite(centerX) || !IsFinite(centerY) || !IsFinite(centerZ) || !IsFinite(radius) || radius < 0f)
            {
                minCell = default;
                maxCell = default;
                return false;
            }

            float cellSize = CellSize;
            minCell = new CellCoordinate(
                FloorToIndex((((double)centerX - radius) - OriginX) / cellSize),
                FloorToIndex((((double)centerY - radius) - OriginY) / cellSize),
                FloorToIndex((((double)centerZ - radius) - OriginZ) / cellSize));
            maxCell = new CellCoordinate(
                FloorToIndex((((double)centerX + radius) - OriginX) / cellSize),
                FloorToIndex((((double)centerY + radius) - OriginY) / cellSize),
                FloorToIndex((((double)centerZ + radius) - OriginZ) / cellSize));
            return true;
        }

        /// <summary>
        /// Computes the inclusive cell range that covers an axis-aligned box.
        /// </summary>
        /// <remarks>
        /// The range is exact for the box: every cell that can hold an overlapping element is included, and no
        /// geometric judgement is performed. Complexity: O(1).
        /// </remarks>
        /// <returns><c>false</c> when the box is inverted, empty, or not finite; the outputs are then zero.</returns>
        public bool CellRangeForAabb(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            out CellCoordinate minCell,
            out CellCoordinate maxCell)
        {
            if (!IsFinite(minX) || !IsFinite(minY) || !IsFinite(minZ) ||
                !IsFinite(maxX) || !IsFinite(maxY) || !IsFinite(maxZ) ||
                maxX < minX || maxY < minY || maxZ < minZ)
            {
                minCell = default;
                maxCell = default;
                return false;
            }

            minCell = WorldToCell(minX, minY, minZ);
            maxCell = WorldToCell(maxX, maxY, maxZ);
            return true;
        }

        /// <summary>Returns "cellSize=..., origin=(...)". Diagnostic use only; allocates.</summary>
        public override string ToString()
        {
            return "cellSize=" + CellSize + ", origin=(" + OriginX + ", " + OriginY + ", " + OriginZ + ")";
        }

        internal static int FloorToIndex(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            if (value <= int.MinValue)
            {
                return int.MinValue;
            }

            if (value >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)Math.Floor(value);
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
