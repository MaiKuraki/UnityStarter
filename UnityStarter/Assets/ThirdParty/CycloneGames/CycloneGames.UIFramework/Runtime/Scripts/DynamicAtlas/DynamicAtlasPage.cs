using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    internal enum DynamicAtlasPageInsertResult
    {
        Success = 0,
        NoSpace = 1,
        CopyUnsupported = 2,
        CopyFailed = 3,
    }

    internal readonly struct DynamicAtlasPlacement
    {
        internal readonly RectInt SlotRect;
        internal readonly RectInt ContentRect;

        internal DynamicAtlasPlacement(RectInt slotRect, RectInt contentRect)
        {
            SlotRect = slotRect;
            ContentRect = contentRect;
        }
    }

    internal sealed class DynamicAtlasPage : IDisposable
    {
        private static int _nextPageId;

        private readonly int _size;
        private readonly int _padding;
        private readonly int _maxEntries;
        private readonly bool _enableBleed;
        private readonly bool _allowSynchronousReadback;
        private readonly int _bytesPerPixel;
        private readonly List<RectInt> _releasedSlots;

        private int _cursorX;
        private int _cursorY;
        private int _rowHeight;
        private int _entryCount;
        private long _payloadPixelArea;
        private long _reservedPixelArea;
        private bool _needsUpload;
        private bool _disposed;

        internal int PageId { get; }
        internal Texture2D Texture { get; private set; }
        internal DynamicAtlasPageMode Mode { get; }
        internal int EntryCount => _entryCount;
        internal int ReleasedSlotCount => _releasedSlots.Count;
        internal long PayloadPixelArea => _payloadPixelArea;
        internal long ReservedPixelArea => _reservedPixelArea;
        internal bool IsEmpty => _entryCount == 0;
        internal bool HasPendingUpload => _needsUpload;

        internal DynamicAtlasPage(
            int size,
            TextureFormat format,
            FilterMode filterMode,
            int padding,
            bool enableBleed,
            int maxEntries,
            DynamicAtlasPageMode mode,
            bool allowSynchronousReadback)
        {
            _size = size;
            _padding = padding;
            _maxEntries = maxEntries;
            _enableBleed = enableBleed;
            _allowSynchronousReadback = allowSynchronousReadback;
            _bytesPerPixel = TextureFormatHelper.GetBytesPerPixel(format);
            _releasedSlots = new List<RectInt>(Mathf.Min(maxEntries, 128));
            Mode = mode;
            PageId = ++_nextPageId;

            Texture = new Texture2D(size, size, format, mipChain: false, linear: false)
            {
                name = $"DynamicAtlasPage_{PageId}_{mode}",
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };

            ClearAndInitializeTexture(mode == DynamicAtlasPageMode.GpuOnly);
        }

        internal DynamicAtlasPageInsertResult TryInsert(
            Texture2D source,
            RectInt sourceRect,
            out DynamicAtlasPlacement placement,
            out DynamicAtlasCopyPath copyPath)
        {
            placement = default;
            copyPath = DynamicAtlasCopyPath.None;

            if (_disposed || source == null || _entryCount >= _maxEntries || !IsValidSourceRect(source, sourceRect))
            {
                return DynamicAtlasPageInsertResult.NoSpace;
            }

            int slotWidth = sourceRect.width + (_padding * 2);
            int slotHeight = sourceRect.height + (_padding * 2);
            if (slotWidth > _size || slotHeight > _size)
            {
                return DynamicAtlasPageInsertResult.NoSpace;
            }

            if (!TryReserveSlot(slotWidth, slotHeight, out RectInt slotRect))
            {
                return DynamicAtlasPageInsertResult.NoSpace;
            }

            RectInt contentRect = new RectInt(
                slotRect.x + _padding,
                slotRect.y + _padding,
                sourceRect.width,
                sourceRect.height);

            DynamicAtlasPageInsertResult copyResult = Mode == DynamicAtlasPageMode.GpuOnly
                ? TryCopyGpu(source, sourceRect, contentRect)
                : TryCopyCpuBacked(source, sourceRect, contentRect, out copyPath);

            if (Mode == DynamicAtlasPageMode.GpuOnly && copyResult == DynamicAtlasPageInsertResult.Success)
            {
                copyPath = DynamicAtlasCopyPath.GpuCopy;
            }

            if (copyResult != DynamicAtlasPageInsertResult.Success)
            {
                RetainReleasedSlot(slotRect);
                return copyResult;
            }

            _entryCount++;
            _payloadPixelArea += (long)sourceRect.width * sourceRect.height;
            _reservedPixelArea += (long)slotRect.width * slotRect.height;
            placement = new DynamicAtlasPlacement(slotRect, contentRect);
            return DynamicAtlasPageInsertResult.Success;
        }

        internal void Release(DynamicAtlasPlacement placement)
        {
            if (_disposed || _entryCount <= 0)
            {
                return;
            }

            _entryCount--;
            _payloadPixelArea -= (long)placement.ContentRect.width * placement.ContentRect.height;
            _reservedPixelArea -= (long)placement.SlotRect.width * placement.SlotRect.height;

            if (_entryCount == 0)
            {
                _cursorX = 0;
                _cursorY = 0;
                _rowHeight = 0;
                _payloadPixelArea = 0;
                _reservedPixelArea = 0;
                _releasedSlots.Clear();
                return;
            }

            RetainReleasedSlot(placement.SlotRect);
        }

        internal void FlushPendingUpload()
        {
            if (_disposed || !_needsUpload || Texture == null)
            {
                return;
            }

            Texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            _needsUpload = false;
        }

        internal DynamicAtlasPageSnapshot CreateSnapshot()
        {
            return new DynamicAtlasPageSnapshot(
                PageId,
                Texture,
                Mode,
                _entryCount,
                _releasedSlots.Count,
                _payloadPixelArea,
                _reservedPixelArea);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _releasedSlots.Clear();

            if (Texture != null)
            {
                DestroyUnityObject(Texture);
                Texture = null;
            }
        }

        private void ClearAndInitializeTexture(bool releaseCpuCopy)
        {
            var rawData = Texture.GetRawTextureData<byte>();
            for (int i = 0; i < rawData.Length; i++)
            {
                rawData[i] = 0;
            }

            Texture.Apply(updateMipmaps: false, makeNoLongerReadable: releaseCpuCopy);
        }

        private bool TryReserveSlot(int width, int height, out RectInt slotRect)
        {
            int bestIndex = -1;
            long bestWaste = long.MaxValue;

            for (int i = 0; i < _releasedSlots.Count; i++)
            {
                RectInt candidate = _releasedSlots[i];
                if (width > candidate.width || height > candidate.height)
                {
                    continue;
                }

                long waste = ((long)candidate.width * candidate.height) - ((long)width * height);
                if (waste < bestWaste)
                {
                    bestWaste = waste;
                    bestIndex = i;
                    if (waste == 0)
                    {
                        break;
                    }
                }
            }

            if (bestIndex >= 0)
            {
                slotRect = _releasedSlots[bestIndex];
                int lastIndex = _releasedSlots.Count - 1;
                _releasedSlots[bestIndex] = _releasedSlots[lastIndex];
                _releasedSlots.RemoveAt(lastIndex);
                return true;
            }

            if (_cursorX + width > _size)
            {
                _cursorX = 0;
                _cursorY += _rowHeight;
                _rowHeight = 0;
            }

            if (_cursorY + height > _size)
            {
                slotRect = default;
                return false;
            }

            slotRect = new RectInt(_cursorX, _cursorY, width, height);
            _cursorX += width;
            if (height > _rowHeight)
            {
                _rowHeight = height;
            }

            return true;
        }

        private void RetainReleasedSlot(RectInt slotRect)
        {
            for (int i = _releasedSlots.Count - 1; i >= 0; i--)
            {
                if (!TryMergeAdjacent(slotRect, _releasedSlots[i], out RectInt combined))
                {
                    continue;
                }

                slotRect = combined;
                int lastIndex = _releasedSlots.Count - 1;
                _releasedSlots[i] = _releasedSlots[lastIndex];
                _releasedSlots.RemoveAt(lastIndex);
            }

            if (_releasedSlots.Count < _maxEntries)
            {
                _releasedSlots.Add(slotRect);
                return;
            }

            int smallestIndex = 0;
            long smallestArea = GetArea(_releasedSlots[0]);
            for (int i = 1; i < _releasedSlots.Count; i++)
            {
                long area = GetArea(_releasedSlots[i]);
                if (area < smallestArea)
                {
                    smallestArea = area;
                    smallestIndex = i;
                }
            }

            if (GetArea(slotRect) > smallestArea)
            {
                _releasedSlots[smallestIndex] = slotRect;
            }
        }

        private DynamicAtlasPageInsertResult TryCopyGpu(Texture2D source, RectInt sourceRect, RectInt destinationRect)
        {
            if (!TextureFormatHelper.CanUseGpuCopy(source, Texture))
            {
                return DynamicAtlasPageInsertResult.CopyUnsupported;
            }

            try
            {
                Graphics.CopyTexture(
                    source,
                    0,
                    0,
                    sourceRect.x,
                    sourceRect.y,
                    sourceRect.width,
                    sourceRect.height,
                    Texture,
                    0,
                    0,
                    destinationRect.x,
                    destinationRect.y);

                if (_enableBleed)
                {
                    CopyBleedGpu(source, sourceRect, destinationRect);
                }

                return DynamicAtlasPageInsertResult.Success;
            }
            catch (Exception)
            {
                return DynamicAtlasPageInsertResult.CopyFailed;
            }
        }

        private DynamicAtlasPageInsertResult TryCopyCpuBacked(
            Texture2D source,
            RectInt sourceRect,
            RectInt destinationRect,
            out DynamicAtlasCopyPath copyPath)
        {
            copyPath = DynamicAtlasCopyPath.None;

            DynamicAtlasPageInsertResult rawCopyResult = TryCopyRaw(source, sourceRect, destinationRect);
            if (rawCopyResult == DynamicAtlasPageInsertResult.Success)
            {
                copyPath = DynamicAtlasCopyPath.CpuRawCopy;
                return DynamicAtlasPageInsertResult.Success;
            }

            if (!_allowSynchronousReadback)
            {
                return rawCopyResult;
            }

            DynamicAtlasPageInsertResult readbackResult = TryCopyViaReadback(source, sourceRect, destinationRect);
            if (readbackResult == DynamicAtlasPageInsertResult.Success)
            {
                copyPath = DynamicAtlasCopyPath.SynchronousGpuReadback;
                return DynamicAtlasPageInsertResult.Success;
            }

            return readbackResult;
        }

        private DynamicAtlasPageInsertResult TryCopyRaw(Texture2D source, RectInt sourceRect, RectInt destinationRect)
        {
            if (!source.isReadable ||
                source.format != Texture.format ||
                source.graphicsFormat != Texture.graphicsFormat ||
                _bytesPerPixel <= 0)
            {
                return DynamicAtlasPageInsertResult.CopyUnsupported;
            }

            try
            {
                var sourceData = source.GetRawTextureData<byte>();
                var destinationData = Texture.GetRawTextureData<byte>();
                int sourceStride = source.width * _bytesPerPixel;
                int destinationStride = _size * _bytesPerPixel;
                int rowBytes = sourceRect.width * _bytesPerPixel;

                for (int row = 0; row < sourceRect.height; row++)
                {
                    int sourceOffset = ((sourceRect.y + row) * sourceStride) + (sourceRect.x * _bytesPerPixel);
                    int destinationOffset = ((destinationRect.y + row) * destinationStride) + (destinationRect.x * _bytesPerPixel);
                    Unity.Collections.NativeArray<byte>.Copy(
                        sourceData,
                        sourceOffset,
                        destinationData,
                        destinationOffset,
                        rowBytes);
                }

                if (_enableBleed)
                {
                    CopyBleedCpu(destinationData, destinationRect);
                }

                _needsUpload = true;
                return DynamicAtlasPageInsertResult.Success;
            }
            catch (Exception)
            {
                return DynamicAtlasPageInsertResult.CopyFailed;
            }
        }

        private DynamicAtlasPageInsertResult TryCopyViaReadback(Texture2D source, RectInt sourceRect, RectInt destinationRect)
        {
            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;

            try
            {
                temporary = RenderTexture.GetTemporary(
                    sourceRect.width,
                    sourceRect.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default);
                Vector2 scale = new Vector2(
                    (float)sourceRect.width / source.width,
                    (float)sourceRect.height / source.height);
                Vector2 offset = new Vector2(
                    (float)sourceRect.x / source.width,
                    (float)sourceRect.y / source.height);

                Graphics.Blit(source, temporary, scale, offset);
                RenderTexture.active = temporary;
                Texture.ReadPixels(
                    new Rect(0f, 0f, sourceRect.width, sourceRect.height),
                    destinationRect.x,
                    destinationRect.y,
                    recalculateMipMaps: false);

                if (_enableBleed)
                {
                    var destinationData = Texture.GetRawTextureData<byte>();
                    CopyBleedCpu(destinationData, destinationRect);
                }

                _needsUpload = true;
                return DynamicAtlasPageInsertResult.Success;
            }
            catch (Exception)
            {
                return DynamicAtlasPageInsertResult.CopyFailed;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null)
                {
                    RenderTexture.ReleaseTemporary(temporary);
                }
            }
        }

        private static bool TryMergeAdjacent(RectInt first, RectInt second, out RectInt combined)
        {
            if (first.y == second.y && first.height == second.height &&
                (first.xMax == second.x || second.xMax == first.x))
            {
                int x = Mathf.Min(first.x, second.x);
                combined = new RectInt(x, first.y, first.width + second.width, first.height);
                return true;
            }

            if (first.x == second.x && first.width == second.width &&
                (first.yMax == second.y || second.yMax == first.y))
            {
                int y = Mathf.Min(first.y, second.y);
                combined = new RectInt(first.x, y, first.width, first.height + second.height);
                return true;
            }

            combined = default;
            return false;
        }

        private static long GetArea(RectInt rect)
        {
            return (long)rect.width * rect.height;
        }

        private void CopyBleedGpu(Texture2D source, RectInt sourceRect, RectInt destinationRect)
        {
            int sourceRight = sourceRect.xMax - 1;
            int sourceTop = sourceRect.yMax - 1;
            int destinationRight = destinationRect.xMax;
            int destinationTop = destinationRect.yMax;

            Graphics.CopyTexture(source, 0, 0, sourceRect.x, sourceRect.y, 1, sourceRect.height, Texture, 0, 0, destinationRect.x - 1, destinationRect.y);
            Graphics.CopyTexture(source, 0, 0, sourceRight, sourceRect.y, 1, sourceRect.height, Texture, 0, 0, destinationRight, destinationRect.y);
            Graphics.CopyTexture(source, 0, 0, sourceRect.x, sourceRect.y, sourceRect.width, 1, Texture, 0, 0, destinationRect.x, destinationRect.y - 1);
            Graphics.CopyTexture(source, 0, 0, sourceRect.x, sourceTop, sourceRect.width, 1, Texture, 0, 0, destinationRect.x, destinationTop);
            Graphics.CopyTexture(source, 0, 0, sourceRect.x, sourceRect.y, 1, 1, Texture, 0, 0, destinationRect.x - 1, destinationRect.y - 1);
            Graphics.CopyTexture(source, 0, 0, sourceRight, sourceRect.y, 1, 1, Texture, 0, 0, destinationRight, destinationRect.y - 1);
            Graphics.CopyTexture(source, 0, 0, sourceRect.x, sourceTop, 1, 1, Texture, 0, 0, destinationRect.x - 1, destinationTop);
            Graphics.CopyTexture(source, 0, 0, sourceRight, sourceTop, 1, 1, Texture, 0, 0, destinationRight, destinationTop);
        }

        private void CopyBleedCpu(Unity.Collections.NativeArray<byte> destinationData, RectInt destinationRect)
        {
            int destinationStride = _size * _bytesPerPixel;
            int leftX = destinationRect.x;
            int rightX = destinationRect.xMax - 1;
            int bottomY = destinationRect.y;
            int topY = destinationRect.yMax - 1;

            for (int row = bottomY; row <= topY; row++)
            {
                CopyPixel(destinationData, leftX, row, leftX - 1, row, destinationStride);
                CopyPixel(destinationData, rightX, row, rightX + 1, row, destinationStride);
            }

            for (int column = leftX - 1; column <= rightX + 1; column++)
            {
                int clampedColumn = Mathf.Clamp(column, leftX, rightX);
                CopyPixel(destinationData, clampedColumn, bottomY, column, bottomY - 1, destinationStride);
                CopyPixel(destinationData, clampedColumn, topY, column, topY + 1, destinationStride);
            }
        }

        private void CopyPixel(
            Unity.Collections.NativeArray<byte> data,
            int sourceX,
            int sourceY,
            int destinationX,
            int destinationY,
            int stride)
        {
            int sourceOffset = (sourceY * stride) + (sourceX * _bytesPerPixel);
            int destinationOffset = (destinationY * stride) + (destinationX * _bytesPerPixel);
            for (int i = 0; i < _bytesPerPixel; i++)
            {
                data[destinationOffset + i] = data[sourceOffset + i];
            }
        }

        private static bool IsValidSourceRect(Texture2D source, RectInt sourceRect)
        {
            return sourceRect.width > 0 &&
                   sourceRect.height > 0 &&
                   sourceRect.x >= 0 &&
                   sourceRect.y >= 0 &&
                   sourceRect.width <= source.width &&
                   sourceRect.height <= source.height &&
                   sourceRect.x <= source.width - sourceRect.width &&
                   sourceRect.y <= source.height - sourceRect.height;
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
