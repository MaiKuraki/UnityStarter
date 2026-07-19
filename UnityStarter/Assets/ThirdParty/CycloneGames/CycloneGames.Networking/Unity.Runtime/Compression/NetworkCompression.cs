using System;
using System.Runtime.CompilerServices;
using CycloneGames.Networking.Serialization;
using UnityEngine;

namespace CycloneGames.Networking.Compression
{
    /// <summary>
    /// Quantized Vector3 for bandwidth-efficient position synchronization.
    /// The wire contract uses exactly 100 quantized units per Unity world unit.
    /// </summary>
    public struct QuantizedVector3
    {
        /// <summary>
        /// Frozen wire-scale used by both encoding and decoding. Changing this value is a protocol change.
        /// </summary>
        public const int QuantizationUnitsPerWorldUnit = 100;

        public int X;
        public int Y;
        public int Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static QuantizedVector3 FromVector3(Vector3 v)
        {
            return new QuantizedVector3
            {
                X = Mathf.RoundToInt(v.x * QuantizationUnitsPerWorldUnit),
                Y = Mathf.RoundToInt(v.y * QuantizationUnitsPerWorldUnit),
                Z = Mathf.RoundToInt(v.z * QuantizationUnitsPerWorldUnit)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 ToVector3()
        {
            const float inv = 1f / QuantizationUnitsPerWorldUnit;
            return new Vector3(X * inv, Y * inv, Z * inv);
        }

        /// <summary>
        /// Write as variable-length integers for optimal bandwidth.
        /// Small position values near origin use fewer bytes.
        /// </summary>
        public void WriteTo(INetWriter writer)
        {
            WriteVarInt(writer, ZigZagEncode(X));
            WriteVarInt(writer, ZigZagEncode(Y));
            WriteVarInt(writer, ZigZagEncode(Z));
        }

        public static QuantizedVector3 ReadFrom(INetReader reader)
        {
            return new QuantizedVector3
            {
                X = ZigZagDecode(ReadVarInt(reader)),
                Y = ZigZagDecode(ReadVarInt(reader)),
                Z = ZigZagDecode(ReadVarInt(reader))
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ZigZagEncode(int value) => (uint)((value << 1) ^ (value >> 31));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ZigZagDecode(uint value) => (int)(value >> 1) ^ -(int)(value & 1);

        private static void WriteVarInt(INetWriter writer, uint value)
        {
            while (value >= 0x80)
            {
                writer.WriteByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            writer.WriteByte((byte)value);
        }

        private static uint ReadVarInt(INetReader reader)
        {
            uint result = 0;
            for (int byteIndex = 0; byteIndex < 5; byteIndex++)
            {
                byte value = reader.ReadByte();
                if (byteIndex == 4 && (value & 0xF0) != 0)
                {
                    throw new FormatException("VarUInt32 exceeds 32 bits or has an unterminated fifth byte.");
                }

                result |= (uint)(value & 0x7F) << (byteIndex * 7);
                if ((value & 0x80) == 0)
                {
                    if (byteIndex > 0 && (value & 0x7F) == 0)
                    {
                        throw new FormatException("VarUInt32 uses a non-canonical overlong encoding.");
                    }

                    return result;
                }
            }

            throw new FormatException("VarUInt32 is unterminated.");
        }
    }

    /// <summary>
    /// Quantized Quaternion using smallest-three compression.
    /// Compresses 16 bytes (Quaternion) down to 4 bytes (32 bits).
    /// </summary>
    public struct QuantizedQuaternion
    {
        public uint Packed;

        private const int BitsPerComponent = 10;
        private const int MaxValue = (1 << BitsPerComponent) - 1;
        private const float PackScale = MaxValue / 1.41421356f; // sqrt(2)
        private const float UnpackScale = 1.41421356f / MaxValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static QuantizedQuaternion FromQuaternion(Quaternion q)
        {
            // Find largest component
            float absX = Mathf.Abs(q.x), absY = Mathf.Abs(q.y);
            float absZ = Mathf.Abs(q.z), absW = Mathf.Abs(q.w);

            int largestIndex = 0;
            float largest = absX;
            if (absY > largest) { largest = absY; largestIndex = 1; }
            if (absZ > largest) { largest = absZ; largestIndex = 2; }
            if (absW > largest) { largestIndex = 3; }

            // Ensure largest is positive
            float sign = largestIndex switch
            {
                0 => Mathf.Sign(q.x),
                1 => Mathf.Sign(q.y),
                2 => Mathf.Sign(q.z),
                _ => Mathf.Sign(q.w)
            };

            float a, b, c;
            switch (largestIndex)
            {
                case 0: a = q.y * sign; b = q.z * sign; c = q.w * sign; break;
                case 1: a = q.x * sign; b = q.z * sign; c = q.w * sign; break;
                case 2: a = q.x * sign; b = q.y * sign; c = q.w * sign; break;
                default: a = q.x * sign; b = q.y * sign; c = q.z * sign; break;
            }

            uint pa = (uint)Mathf.Clamp(Mathf.RoundToInt((a + 0.70710678f) * PackScale), 0, MaxValue);
            uint pb = (uint)Mathf.Clamp(Mathf.RoundToInt((b + 0.70710678f) * PackScale), 0, MaxValue);
            uint pc = (uint)Mathf.Clamp(Mathf.RoundToInt((c + 0.70710678f) * PackScale), 0, MaxValue);

            return new QuantizedQuaternion
            {
                Packed = ((uint)largestIndex << 30) | (pa << 20) | (pb << 10) | pc
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion ToQuaternion()
        {
            int largest = (int)(Packed >> 30);
            float a = ((Packed >> 20) & MaxValue) * UnpackScale - 0.70710678f;
            float b = ((Packed >> 10) & MaxValue) * UnpackScale - 0.70710678f;
            float c = (Packed & MaxValue) * UnpackScale - 0.70710678f;

            float d = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));

            return largest switch
            {
                0 => new Quaternion(d, a, b, c),
                1 => new Quaternion(a, d, b, c),
                2 => new Quaternion(a, b, d, c),
                _ => new Quaternion(a, b, c, d)
            };
        }

        public void WriteTo(INetWriter writer) => writer.WriteUInt(Packed);
        public static QuantizedQuaternion ReadFrom(INetReader reader) =>
            new QuantizedQuaternion { Packed = reader.ReadUInt() };
    }

    /// <summary>
    /// Delta compression for position updates.
    /// Sends only the difference from the last known position.
    /// Ideal for entities that move slowly or in small increments.
    /// </summary>
    public sealed class DeltaCompressor
    {
        private const DeltaFlags SupportedReadFlags =
            DeltaFlags.DeltaPosition | DeltaFlags.FullPosition | DeltaFlags.FullRotation;

        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private bool _hasBaseline;

        // Threshold below which delta is considered zero (skip sending)
        public float PositionThreshold { get; set; } = 0.001f;
        public float RotationThreshold { get; set; } = 0.001f;

        /// <summary>
        /// Returns true if the delta is significant enough to send.
        /// </summary>
        public bool ShouldSend(Vector3 position, Quaternion rotation)
        {
            if (!_hasBaseline) return true;

            float posDelta = (position - _lastPosition).sqrMagnitude;
            float rotDelta = 1f - Mathf.Abs(Quaternion.Dot(_lastRotation, rotation));

            return posDelta > PositionThreshold * PositionThreshold ||
                   rotDelta > RotationThreshold;
        }

        /// <summary>
        /// Write delta or full state. Returns DeltaFlags indicating what was written.
        /// </summary>
        public DeltaFlags WriteDelta(INetWriter writer, Vector3 position, Quaternion rotation)
        {
            DeltaFlags flags = DeltaFlags.None;

            if (!_hasBaseline)
            {
                flags = DeltaFlags.FullPosition | DeltaFlags.FullRotation;
                writer.WriteByte((byte)flags);
                WriteVector3(writer, position);
                WriteQuaternion(writer, rotation);
            }
            else
            {
                Vector3 posDelta = position - _lastPosition;
                bool posChanged = posDelta.sqrMagnitude > PositionThreshold * PositionThreshold;
                bool rotChanged = 1f - Mathf.Abs(Quaternion.Dot(_lastRotation, rotation)) > RotationThreshold;

                if (posChanged) flags |= DeltaFlags.DeltaPosition;
                if (rotChanged) flags |= DeltaFlags.FullRotation;

                writer.WriteByte((byte)flags);

                if (posChanged)
                {
                    var qDelta = QuantizedVector3.FromVector3(posDelta);
                    qDelta.WriteTo(writer);
                }

                if (rotChanged)
                {
                    var qRot = QuantizedQuaternion.FromQuaternion(rotation);
                    qRot.WriteTo(writer);
                }
            }

            _lastPosition = position;
            _lastRotation = rotation;
            _hasBaseline = true;
            return flags;
        }

        public void ReadDelta(INetReader reader, out Vector3 position, out Quaternion rotation)
        {
            var flags = (DeltaFlags)reader.ReadByte();
            ValidateReadFlags(flags);

            if ((flags & DeltaFlags.FullPosition) != 0)
            {
                position = ReadVector3(reader);
            }
            else if ((flags & DeltaFlags.DeltaPosition) != 0)
            {
                var qDelta = QuantizedVector3.ReadFrom(reader);
                position = _lastPosition + qDelta.ToVector3();
            }
            else
            {
                position = _lastPosition;
            }

            if ((flags & DeltaFlags.FullRotation) != 0)
            {
                if ((flags & DeltaFlags.FullPosition) != 0) // Full baseline includes raw quat
                    rotation = ReadQuaternion(reader);
                else
                    rotation = QuantizedQuaternion.ReadFrom(reader).ToQuaternion();
            }
            else
            {
                rotation = _lastRotation;
            }

            _lastPosition = position;
            _lastRotation = rotation;
            _hasBaseline = true;
        }

        private void ValidateReadFlags(DeltaFlags flags)
        {
            if ((flags & ~SupportedReadFlags) != 0)
            {
                throw new FormatException($"Delta payload contains unsupported flags: 0x{(byte)flags:X2}.");
            }

            bool hasFullPosition = (flags & DeltaFlags.FullPosition) != 0;
            bool hasDeltaPosition = (flags & DeltaFlags.DeltaPosition) != 0;
            bool hasFullRotation = (flags & DeltaFlags.FullRotation) != 0;

            if (hasFullPosition && hasDeltaPosition)
            {
                throw new FormatException("Delta payload cannot contain both full and delta position data.");
            }

            if (hasFullPosition && !hasFullRotation)
            {
                throw new FormatException("A full-position baseline must include a full rotation.");
            }

            if (!_hasBaseline && flags != (DeltaFlags.FullPosition | DeltaFlags.FullRotation))
            {
                throw new FormatException("The first delta payload must contain a complete baseline.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteVector3(INetWriter writer, Vector3 value)
        {
            writer.WriteFloat(value.x);
            writer.WriteFloat(value.y);
            writer.WriteFloat(value.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ReadVector3(INetReader reader)
        {
            return new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteQuaternion(INetWriter writer, Quaternion value)
        {
            writer.WriteFloat(value.x);
            writer.WriteFloat(value.y);
            writer.WriteFloat(value.z);
            writer.WriteFloat(value.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Quaternion ReadQuaternion(INetReader reader)
        {
            return new Quaternion(
                reader.ReadFloat(),
                reader.ReadFloat(),
                reader.ReadFloat(),
                reader.ReadFloat());
        }

        public void Reset()
        {
            _hasBaseline = false;
            _lastPosition = Vector3.zero;
            _lastRotation = Quaternion.identity;
        }
    }

    [Flags]
    public enum DeltaFlags : byte
    {
        None = 0,
        DeltaPosition = 1 << 0,
        FullPosition = 1 << 1,
        FullRotation = 1 << 2,
        DeltaRotation = 1 << 3,
        Scale = 1 << 4
    }
}
