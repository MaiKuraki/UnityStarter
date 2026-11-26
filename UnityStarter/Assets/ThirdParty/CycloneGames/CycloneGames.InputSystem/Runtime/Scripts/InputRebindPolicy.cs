using System;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Acceptance rules for a captured control. Paths and layouts use Unity Input System syntax; an empty list
    /// allows everything.
    /// </summary>
    public sealed class InputRebindPolicy
    {
        /// <summary>Longest accepted control path, in characters.</summary>
        public static readonly int DefaultMaxPathLength = 128;

        /// <summary>Escape, so a rebind session can always be canceled from the keyboard.</summary>
        public static readonly string DefaultReservedPath = "<Keyboard>/escape";

        private readonly string[] _reservedPaths;
        private readonly string[] _allowedLayouts;
        private readonly int _maxPathLength;

        /// <summary>Creates a policy. A null or empty list allows every value in that dimension.</summary>
        /// <param name="reservedPaths">Paths that must never be bound, such as the cancel key.</param>
        /// <param name="allowedLayouts">Control layouts that may be bound, such as <c>Button</c> or <c>Key</c>.</param>
        /// <param name="maxPathLength">Longest accepted path. Zero or less uses <see cref="DefaultMaxPathLength"/>.</param>
        public InputRebindPolicy(string[] reservedPaths, string[] allowedLayouts, int maxPathLength)
        {
            _reservedPaths = reservedPaths != null && reservedPaths.Length > 0 ? reservedPaths : Array.Empty<string>();
            _allowedLayouts = allowedLayouts != null && allowedLayouts.Length > 0 ? allowedLayouts : Array.Empty<string>();
            _maxPathLength = maxPathLength > 0 ? maxPathLength : DefaultMaxPathLength;
        }

        /// <summary>Policy that only blocks Escape. Returns a new instance because the lists are caller-owned.</summary>
        public static InputRebindPolicy CreateDefault()
        {
            return new InputRebindPolicy(new[] { DefaultReservedPath }, null, DefaultMaxPathLength);
        }

        /// <summary>Number of reserved paths.</summary>
        public int ReservedPathCount => _reservedPaths.Length;

        /// <summary>Number of allowed layouts. Zero means every layout is allowed.</summary>
        public int AllowedLayoutCount => _allowedLayouts.Length;

        /// <summary>Longest accepted path.</summary>
        public int MaxPathLength => _maxPathLength;

        /// <summary>True when the path is empty or longer than <see cref="MaxPathLength"/>.</summary>
        public bool IsPathInvalid(string controlPath)
        {
            return string.IsNullOrEmpty(controlPath) || controlPath.Length > _maxPathLength;
        }

        /// <summary>True when the path is reserved. Comparison is ordinal and case-insensitive.</summary>
        public bool IsReserved(string controlPath)
        {
            if (string.IsNullOrEmpty(controlPath)) return false;
            for (int i = 0; i < _reservedPaths.Length; i++)
            {
                if (string.Equals(_reservedPaths[i], controlPath, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>True when the layout may be bound. Zero allowed layouts means every layout passes.</summary>
        public bool IsLayoutAllowed(string layout)
        {
            if (_allowedLayouts.Length == 0) return true;
            if (string.IsNullOrEmpty(layout)) return false;
            for (int i = 0; i < _allowedLayouts.Length; i++)
            {
                if (string.Equals(_allowedLayouts[i], layout, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
