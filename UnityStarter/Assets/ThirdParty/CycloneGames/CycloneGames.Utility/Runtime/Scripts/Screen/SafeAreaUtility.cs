using System;
using System.Runtime.CompilerServices;

using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Immutable policy used by <see cref="SafeAreaUtility"/>.
    /// </summary>
    public readonly struct SafeAreaPolicy
    {
        /// <summary>
        /// Padding order is left, bottom, right, top, in screen pixels.
        /// </summary>
        public Vector4 PaddingPixels { get; }

        public bool ExtendIntoBottomSafeArea { get; }
        /// <summary>
        /// Ensures the bottom inset is at least the top inset after bottom extension is applied.
        /// This balances a top cutout; it does not increase the top inset when the bottom is larger.
        /// </summary>
        public bool EnforceVerticalSymmetry { get; }
        public bool EnforceHorizontalSymmetry { get; }

        public SafeAreaPolicy(
            bool extendIntoBottomSafeArea,
            bool enforceVerticalSymmetry,
            bool enforceHorizontalSymmetry,
            Vector4 paddingPixels = default)
        {
            ExtendIntoBottomSafeArea = extendIntoBottomSafeArea;
            EnforceVerticalSymmetry = enforceVerticalSymmetry;
            EnforceHorizontalSymmetry = enforceHorizontalSymmetry;
            PaddingPixels = paddingPixels;
        }
    }

    /// <summary>
    /// Pure safe-area calculations shared by runtime UI components.
    /// </summary>
    public static class SafeAreaUtility
    {
        /// <summary>
        /// Calculates a bounded screen-space rectangle using Unity's bottom-left pixel coordinates.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="screenWidth"/> or <paramref name="screenHeight"/> is not positive.
        /// </exception>
        public static Rect CalculatePixelRect(
            Rect safeArea,
            int screenWidth,
            int screenHeight,
            in SafeAreaPolicy policy)
        {
            if (screenWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(screenWidth), screenWidth, "Screen width must be positive.");
            }

            if (screenHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(screenHeight), screenHeight, "Screen height must be positive.");
            }

            float x0 = ClampCoordinate(Mathf.Min(safeArea.xMin, safeArea.xMax), screenWidth, 0f);
            float x1 = ClampCoordinate(Mathf.Max(safeArea.xMin, safeArea.xMax), screenWidth, screenWidth);
            float y0 = ClampCoordinate(Mathf.Min(safeArea.yMin, safeArea.yMax), screenHeight, 0f);
            float y1 = ClampCoordinate(Mathf.Max(safeArea.yMin, safeArea.yMax), screenHeight, screenHeight);

            float leftInset = x0;
            float rightInset = screenWidth - x1;
            float bottomInset = y0;
            float topInset = screenHeight - y1;

            if (policy.ExtendIntoBottomSafeArea)
            {
                bottomInset = 0f;
            }

            if (policy.EnforceVerticalSymmetry)
            {
                bottomInset = Mathf.Max(bottomInset, topInset);
            }

            if (policy.EnforceHorizontalSymmetry)
            {
                float horizontalInset = Mathf.Max(leftInset, rightInset);
                leftInset = horizontalInset;
                rightInset = horizontalInset;
            }

            Vector4 padding = policy.PaddingPixels;
            leftInset += NonNegativeFiniteOrZero(padding.x);
            bottomInset += NonNegativeFiniteOrZero(padding.y);
            rightInset += NonNegativeFiniteOrZero(padding.z);
            topInset += NonNegativeFiniteOrZero(padding.w);

            FitInsets(ref leftInset, ref rightInset, screenWidth);
            FitInsets(ref bottomInset, ref topInset, screenHeight);

            return new Rect(
                leftInset,
                bottomInset,
                screenWidth - leftInset - rightInset,
                screenHeight - bottomInset - topInset);
        }

        /// <summary>
        /// Calculates normalized anchors. Invalid screen dimensions return <see langword="false"/>.
        /// </summary>
        public static bool TryCalculateAnchors(
            Rect safeArea,
            int screenWidth,
            int screenHeight,
            in SafeAreaPolicy policy,
            out Vector2 anchorMin,
            out Vector2 anchorMax)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
                return false;
            }

            Rect pixelRect = CalculatePixelRect(safeArea, screenWidth, screenHeight, in policy);
            float inverseWidth = 1f / screenWidth;
            float inverseHeight = 1f / screenHeight;
            anchorMin = new Vector2(pixelRect.xMin * inverseWidth, pixelRect.yMin * inverseHeight);
            anchorMax = new Vector2(pixelRect.xMax * inverseWidth, pixelRect.yMax * inverseHeight);
            return true;
        }

        /// <summary>
        /// Converts a bottom-left screen-space rectangle to IMGUI's top-left coordinates.
        /// </summary>
        public static Rect ToGuiRect(Rect pixelRect, int screenHeight)
        {
            if (screenHeight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(screenHeight), screenHeight, "Screen height cannot be negative.");
            }

            return new Rect(pixelRect.x, screenHeight - pixelRect.yMax, pixelRect.width, pixelRect.height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float NonNegativeFiniteOrZero(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        }

        private static float ClampCoordinate(float value, float maximum, float fallback)
        {
            if (float.IsNaN(value))
            {
                return fallback;
            }

            if (float.IsPositiveInfinity(value))
            {
                return maximum;
            }

            if (float.IsNegativeInfinity(value))
            {
                return 0f;
            }

            return Mathf.Clamp(value, 0f, maximum);
        }

        private static void FitInsets(ref float leading, ref float trailing, float dimension)
        {
            float sum = leading + trailing;
            if (sum <= dimension || sum <= 0f)
            {
                return;
            }

            float scale = dimension / sum;
            leading = Mathf.Min(dimension, leading * scale);
            trailing = Mathf.Max(0f, dimension - leading);
        }
    }
}
