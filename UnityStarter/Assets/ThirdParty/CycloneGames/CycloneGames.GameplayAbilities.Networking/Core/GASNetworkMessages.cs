using System;
using CycloneGames.Networking;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.GameplayAbilities.Networking
{
    public struct AbilityActivateRequest
    {
        public int AbilityIndex;
        public int PredictionKey;
        public int PredictionKeyOwner;
        public int PredictionInputSequence;
        public NetworkVector3 TargetPosition;
        public NetworkVector3 Direction;
        public uint TargetNetworkId;
    }

    public struct AbilityActivateConfirm
    {
        public int AbilityIndex;
        public int PredictionKey;
        public int PredictionKeyOwner;
        public int PredictionInputSequence;
    }

    public struct AbilityActivateReject
    {
        public int AbilityIndex;
        public int PredictionKey;
        public int PredictionKeyOwner;
        public int PredictionInputSequence;
    }

    public struct AbilityEndMessage
    {
        public int AbilityIndex;
    }

    public struct AbilityCancelMessage
    {
        public int AbilityIndex;
    }

    public struct EffectReplicationData
    {
        public uint TargetNetworkId;
        public uint SourceNetworkId;
        public int EffectInstanceId;
        public int EffectDefinitionId;
        public int Level;
        public int StackCount;
        public long DurationRaw;
        public long TimeRemainingRaw;
        public long PeriodTimeRemainingRaw;
        public int PredictionKey;
        public int PredictionKeyOwner;
        public int PredictionInputSequence;
        public int SetByCallerCount;
        public SetByCallerEntry[] SetByCallerEntries;
    }

    public struct SetByCallerEntry
    {
        public int TagHash;
        public long ValueRaw;
    }

    public struct EffectRemoveData
    {
        public uint TargetNetworkId;
        public int EffectInstanceId;
    }

    public struct EffectStackChangeData
    {
        public uint TargetNetworkId;
        public int EffectInstanceId;
        public int NewStackCount;
    }

    public struct EffectUpdateData
    {
        public uint TargetNetworkId;
        public uint SourceNetworkId;
        public int EffectInstanceId;
        public int EffectDefinitionId;
        public int Level;
        public int StackCount;
        public long DurationRaw;
        public long TimeRemainingRaw;
        public long PeriodTimeRemainingRaw;
        public int PredictionKey;
        public int PredictionKeyOwner;
        public int PredictionInputSequence;
        public int SetByCallerCount;
        public SetByCallerEntry[] SetByCallerEntries;
    }

    public struct AttributeUpdateData
    {
        public uint TargetNetworkId;
        public bool IsFullSync;
        public int AttributeCount;
        public AttributeEntry[] Attributes;
    }

    public struct AttributeEntry
    {
        public int AttributeId;
        public long BaseValueRaw;
        public long CurrentValueRaw;
    }

    public struct TagUpdateData
    {
        public uint TargetNetworkId;
        public int AddedCount;
        public int RemovedCount;
        public int[] AddedTagHashes;
        public int[] RemovedTagHashes;
    }

    public struct AbilityMulticastData
    {
        public uint SourceNetworkId;
        public int AbilityIndex;
        public NetworkVector3 TargetPosition;
        public NetworkVector3 Direction;
        public uint TargetNetworkId;
        public int GameplayCueTagHash;
        public byte EventType;
    }

    public struct GASFullStateData
    {
        public uint TargetNetworkId;
        public ulong StateVersion;
        public uint StateChecksum;

        public int AbilityCount;
        public GrantedAbilityEntry[] Abilities;

        public int EffectCount;
        public EffectReplicationData[] Effects;

        public int AttributeCount;
        public AttributeEntry[] Attributes;

        public int TagCount;
        public int[] TagHashes;
    }

    public struct GrantedAbilityEntry
    {
        public int AbilityDefinitionId;
        public int Level;
        public bool IsActive;
    }

    public struct FullStateRequest
    {
        public uint TargetNetworkId;
    }

    public struct GASStateSyncMetadata
    {
        public uint TargetNetworkId;
        public uint Sequence;
        public ulong BaseVersion;
        public ulong CurrentVersion;
        public uint StateChecksum;
        public uint ChangeMask;
    }

    public enum GASNetworkCapacityProfile : byte
    {
        Conservative,
        Balanced,
        LargeServer
    }

    public sealed class GASNetworkSerializerOptions
    {
        public static GASNetworkSerializerOptions Default => CreateBalanced();

        public int MaxAbilities { get; set; } = 256;
        public int MaxEffects { get; set; } = 1024;
        public int MaxAttributes { get; set; } = 512;
        public int MaxTags { get; set; } = 1024;
        public int MaxSetByCallerEntries { get; set; } = 64;

        public static GASNetworkSerializerOptions CreateConservative()
        {
            return new GASNetworkSerializerOptions
            {
                MaxAbilities = 64,
                MaxEffects = 256,
                MaxAttributes = 128,
                MaxTags = 256,
                MaxSetByCallerEntries = 16
            };
        }

        public static GASNetworkSerializerOptions CreateBalanced()
        {
            return new GASNetworkSerializerOptions
            {
                MaxAbilities = 256,
                MaxEffects = 1024,
                MaxAttributes = 512,
                MaxTags = 1024,
                MaxSetByCallerEntries = 64
            };
        }

        public static GASNetworkSerializerOptions CreateLargeServer()
        {
            return new GASNetworkSerializerOptions
            {
                MaxAbilities = 1024,
                MaxEffects = 4096,
                MaxAttributes = 2048,
                MaxTags = 4096,
                MaxSetByCallerEntries = 128
            };
        }

        public static GASNetworkSerializerOptions CreateForProfile(GASNetworkCapacityProfile profile)
        {
            return profile switch
            {
                GASNetworkCapacityProfile.Conservative => CreateConservative(),
                GASNetworkCapacityProfile.LargeServer => CreateLargeServer(),
                _ => CreateBalanced()
            };
        }

        public GASNetworkSerializerOptions Clone()
        {
            return new GASNetworkSerializerOptions
            {
                MaxAbilities = MaxAbilities,
                MaxEffects = MaxEffects,
                MaxAttributes = MaxAttributes,
                MaxTags = MaxTags,
                MaxSetByCallerEntries = MaxSetByCallerEntries
            };
        }

        public void Validate()
        {
            ValidatePositive(MaxAbilities, nameof(MaxAbilities));
            ValidatePositive(MaxEffects, nameof(MaxEffects));
            ValidatePositive(MaxAttributes, nameof(MaxAttributes));
            ValidatePositive(MaxTags, nameof(MaxTags));
            ValidatePositive(MaxSetByCallerEntries, nameof(MaxSetByCallerEntries));
        }

        private static void ValidatePositive(int value, string name)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(name, "GAS network serializer limits must be positive.");
        }
    }

    public sealed class GASNetworkSerializer : INetSerializer
    {
        private readonly INetSerializer _fallback;
        private readonly GASNetworkSerializerOptions _options;

        public GASNetworkSerializerOptions Options => _options.Clone();

        public GASNetworkSerializer(INetSerializer fallback, GASNetworkSerializerOptions options = null)
        {
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _options = (options ?? GASNetworkSerializerOptions.Default).Clone();
            _options.Validate();
        }

        public static INetSerializer Wrap(INetSerializer serializer, GASNetworkSerializerOptions options = null)
        {
            if (serializer is GASNetworkSerializer gasSerializer)
                return options == null ? gasSerializer : new GASNetworkSerializer(gasSerializer._fallback, options);

            return new GASNetworkSerializer(serializer, options);
        }

        public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
        {
            using var writer = NetworkBufferPool.Get();
            Serialize(value, writer);
            ArraySegment<byte> segment = writer.ToArraySegment();
            if (offset < 0 || offset > buffer.Length || segment.Count > buffer.Length - offset)
                throw new ArgumentException("Buffer too small for serialized GAS message.", nameof(buffer));

            Buffer.BlockCopy(segment.Array, segment.Offset, buffer, offset, segment.Count);
            writtenBytes = segment.Count;
        }

        public void Serialize<T>(in T value, INetWriter writer) where T : struct
        {
            Type type = typeof(T);
            object boxed = value;

            if (type == typeof(EffectReplicationData))
            {
                WriteEffectReplicationData(writer, (EffectReplicationData)boxed);
                return;
            }

            if (type == typeof(EffectUpdateData))
            {
                WriteEffectUpdateData(writer, (EffectUpdateData)boxed);
                return;
            }

            if (type == typeof(AttributeUpdateData))
            {
                WriteAttributeUpdateData(writer, (AttributeUpdateData)boxed);
                return;
            }

            if (type == typeof(TagUpdateData))
            {
                WriteTagUpdateData(writer, (TagUpdateData)boxed);
                return;
            }

            if (type == typeof(GASFullStateData))
            {
                WriteFullStateData(writer, (GASFullStateData)boxed);
                return;
            }

            _fallback.Serialize(value, writer);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
        {
            using var reader = NetworkBufferPool.GetWithData(data);
            return Deserialize<T>(reader);
        }

        public T Deserialize<T>(INetReader reader) where T : struct
        {
            Type type = typeof(T);

            if (type == typeof(EffectReplicationData))
                return (T)(object)ReadEffectReplicationData(reader);
            if (type == typeof(EffectUpdateData))
                return (T)(object)ReadEffectUpdateData(reader);
            if (type == typeof(AttributeUpdateData))
                return (T)(object)ReadAttributeUpdateData(reader);
            if (type == typeof(TagUpdateData))
                return (T)(object)ReadTagUpdateData(reader);
            if (type == typeof(GASFullStateData))
                return (T)(object)ReadFullStateData(reader);

            return _fallback.Deserialize<T>(reader);
        }

        private void WriteFullStateData(INetWriter writer, GASFullStateData value)
        {
            writer.WriteUInt(value.TargetNetworkId);
            WriteULong(writer, value.StateVersion);
            writer.WriteUInt(value.StateChecksum);

            int abilityCount = GetSafeCount(value.Abilities, value.AbilityCount, _options.MaxAbilities, nameof(value.Abilities));
            writer.WriteInt(abilityCount);
            for (int i = 0; i < abilityCount; i++)
                WriteGrantedAbilityEntry(writer, value.Abilities[i]);

            int effectCount = GetSafeCount(value.Effects, value.EffectCount, _options.MaxEffects, nameof(value.Effects));
            writer.WriteInt(effectCount);
            for (int i = 0; i < effectCount; i++)
                WriteEffectReplicationData(writer, value.Effects[i]);

            int attributeCount = GetSafeCount(value.Attributes, value.AttributeCount, _options.MaxAttributes, nameof(value.Attributes));
            writer.WriteInt(attributeCount);
            for (int i = 0; i < attributeCount; i++)
                WriteAttributeEntry(writer, value.Attributes[i]);

            int tagCount = GetSafeCount(value.TagHashes, value.TagCount, _options.MaxTags, nameof(value.TagHashes));
            writer.WriteInt(tagCount);
            for (int i = 0; i < tagCount; i++)
                writer.WriteInt(value.TagHashes[i]);
        }

        private GASFullStateData ReadFullStateData(INetReader reader)
        {
            var value = new GASFullStateData
            {
                TargetNetworkId = reader.ReadUInt(),
                StateVersion = ReadULong(reader),
                StateChecksum = reader.ReadUInt()
            };

            value.AbilityCount = ReadCount(reader, _options.MaxAbilities, nameof(value.Abilities));
            value.Abilities = new GrantedAbilityEntry[value.AbilityCount];
            for (int i = 0; i < value.AbilityCount; i++)
                value.Abilities[i] = ReadGrantedAbilityEntry(reader);

            value.EffectCount = ReadCount(reader, _options.MaxEffects, nameof(value.Effects));
            value.Effects = new EffectReplicationData[value.EffectCount];
            for (int i = 0; i < value.EffectCount; i++)
                value.Effects[i] = ReadEffectReplicationData(reader);

            value.AttributeCount = ReadCount(reader, _options.MaxAttributes, nameof(value.Attributes));
            value.Attributes = new AttributeEntry[value.AttributeCount];
            for (int i = 0; i < value.AttributeCount; i++)
                value.Attributes[i] = ReadAttributeEntry(reader);

            value.TagCount = ReadCount(reader, _options.MaxTags, nameof(value.TagHashes));
            value.TagHashes = new int[value.TagCount];
            for (int i = 0; i < value.TagCount; i++)
                value.TagHashes[i] = reader.ReadInt();

            return value;
        }

        private void WriteEffectReplicationData(INetWriter writer, EffectReplicationData value)
        {
            writer.WriteUInt(value.TargetNetworkId);
            writer.WriteUInt(value.SourceNetworkId);
            writer.WriteInt(value.EffectInstanceId);
            writer.WriteInt(value.EffectDefinitionId);
            writer.WriteInt(value.Level);
            writer.WriteInt(value.StackCount);
            WriteLong(writer, value.DurationRaw);
            WriteLong(writer, value.TimeRemainingRaw);
            WriteLong(writer, value.PeriodTimeRemainingRaw);
            writer.WriteInt(value.PredictionKey);
            writer.WriteInt(value.PredictionKeyOwner);
            writer.WriteInt(value.PredictionInputSequence);

            int count = GetSafeCount(value.SetByCallerEntries, value.SetByCallerCount, _options.MaxSetByCallerEntries, nameof(value.SetByCallerEntries));
            writer.WriteInt(count);
            for (int i = 0; i < count; i++)
                WriteSetByCallerEntry(writer, value.SetByCallerEntries[i]);
        }

        private EffectReplicationData ReadEffectReplicationData(INetReader reader)
        {
            var value = new EffectReplicationData
            {
                TargetNetworkId = reader.ReadUInt(),
                SourceNetworkId = reader.ReadUInt(),
                EffectInstanceId = reader.ReadInt(),
                EffectDefinitionId = reader.ReadInt(),
                Level = reader.ReadInt(),
                StackCount = reader.ReadInt(),
                DurationRaw = ReadLong(reader),
                TimeRemainingRaw = ReadLong(reader),
                PeriodTimeRemainingRaw = ReadLong(reader),
                PredictionKey = reader.ReadInt(),
                PredictionKeyOwner = reader.ReadInt(),
                PredictionInputSequence = reader.ReadInt()
            };

            value.SetByCallerCount = ReadCount(reader, _options.MaxSetByCallerEntries, nameof(value.SetByCallerEntries));
            value.SetByCallerEntries = new SetByCallerEntry[value.SetByCallerCount];
            for (int i = 0; i < value.SetByCallerCount; i++)
                value.SetByCallerEntries[i] = ReadSetByCallerEntry(reader);
            return value;
        }

        private void WriteEffectUpdateData(INetWriter writer, EffectUpdateData value)
        {
            WriteEffectReplicationData(writer, new EffectReplicationData
            {
                TargetNetworkId = value.TargetNetworkId,
                SourceNetworkId = value.SourceNetworkId,
                EffectInstanceId = value.EffectInstanceId,
                EffectDefinitionId = value.EffectDefinitionId,
                Level = value.Level,
                StackCount = value.StackCount,
                DurationRaw = value.DurationRaw,
                TimeRemainingRaw = value.TimeRemainingRaw,
                PeriodTimeRemainingRaw = value.PeriodTimeRemainingRaw,
                PredictionKey = value.PredictionKey,
                PredictionKeyOwner = value.PredictionKeyOwner,
                PredictionInputSequence = value.PredictionInputSequence,
                SetByCallerCount = value.SetByCallerCount,
                SetByCallerEntries = value.SetByCallerEntries
            });
        }

        private EffectUpdateData ReadEffectUpdateData(INetReader reader)
        {
            EffectReplicationData value = ReadEffectReplicationData(reader);
            return new EffectUpdateData
            {
                TargetNetworkId = value.TargetNetworkId,
                SourceNetworkId = value.SourceNetworkId,
                EffectInstanceId = value.EffectInstanceId,
                EffectDefinitionId = value.EffectDefinitionId,
                Level = value.Level,
                StackCount = value.StackCount,
                DurationRaw = value.DurationRaw,
                TimeRemainingRaw = value.TimeRemainingRaw,
                PeriodTimeRemainingRaw = value.PeriodTimeRemainingRaw,
                PredictionKey = value.PredictionKey,
                PredictionKeyOwner = value.PredictionKeyOwner,
                PredictionInputSequence = value.PredictionInputSequence,
                SetByCallerCount = value.SetByCallerCount,
                SetByCallerEntries = value.SetByCallerEntries
            };
        }

        private void WriteAttributeUpdateData(INetWriter writer, AttributeUpdateData value)
        {
            writer.WriteUInt(value.TargetNetworkId);
            writer.WriteByte(value.IsFullSync ? (byte)1 : (byte)0);

            int count = GetSafeCount(value.Attributes, value.AttributeCount, _options.MaxAttributes, nameof(value.Attributes));
            writer.WriteInt(count);
            for (int i = 0; i < count; i++)
                WriteAttributeEntry(writer, value.Attributes[i]);
        }

        private AttributeUpdateData ReadAttributeUpdateData(INetReader reader)
        {
            var value = new AttributeUpdateData
            {
                TargetNetworkId = reader.ReadUInt(),
                IsFullSync = reader.ReadByte() != 0
            };

            value.AttributeCount = ReadCount(reader, _options.MaxAttributes, nameof(value.Attributes));
            value.Attributes = new AttributeEntry[value.AttributeCount];
            for (int i = 0; i < value.AttributeCount; i++)
                value.Attributes[i] = ReadAttributeEntry(reader);
            return value;
        }

        private void WriteTagUpdateData(INetWriter writer, TagUpdateData value)
        {
            writer.WriteUInt(value.TargetNetworkId);

            int addedCount = GetSafeCount(value.AddedTagHashes, value.AddedCount, _options.MaxTags, nameof(value.AddedTagHashes));
            writer.WriteInt(addedCount);
            for (int i = 0; i < addedCount; i++)
                writer.WriteInt(value.AddedTagHashes[i]);

            int removedCount = GetSafeCount(value.RemovedTagHashes, value.RemovedCount, _options.MaxTags, nameof(value.RemovedTagHashes));
            writer.WriteInt(removedCount);
            for (int i = 0; i < removedCount; i++)
                writer.WriteInt(value.RemovedTagHashes[i]);
        }

        private TagUpdateData ReadTagUpdateData(INetReader reader)
        {
            var value = new TagUpdateData
            {
                TargetNetworkId = reader.ReadUInt()
            };

            value.AddedCount = ReadCount(reader, _options.MaxTags, nameof(value.AddedTagHashes));
            value.AddedTagHashes = new int[value.AddedCount];
            for (int i = 0; i < value.AddedCount; i++)
                value.AddedTagHashes[i] = reader.ReadInt();

            value.RemovedCount = ReadCount(reader, _options.MaxTags, nameof(value.RemovedTagHashes));
            value.RemovedTagHashes = new int[value.RemovedCount];
            for (int i = 0; i < value.RemovedCount; i++)
                value.RemovedTagHashes[i] = reader.ReadInt();

            return value;
        }

        private static void WriteGrantedAbilityEntry(INetWriter writer, GrantedAbilityEntry value)
        {
            writer.WriteInt(value.AbilityDefinitionId);
            writer.WriteInt(value.Level);
            writer.WriteByte(value.IsActive ? (byte)1 : (byte)0);
        }

        private static GrantedAbilityEntry ReadGrantedAbilityEntry(INetReader reader)
        {
            return new GrantedAbilityEntry
            {
                AbilityDefinitionId = reader.ReadInt(),
                Level = reader.ReadInt(),
                IsActive = reader.ReadByte() != 0
            };
        }

        private static void WriteAttributeEntry(INetWriter writer, AttributeEntry value)
        {
            writer.WriteInt(value.AttributeId);
            WriteLong(writer, value.BaseValueRaw);
            WriteLong(writer, value.CurrentValueRaw);
        }

        private static AttributeEntry ReadAttributeEntry(INetReader reader)
        {
            return new AttributeEntry
            {
                AttributeId = reader.ReadInt(),
                BaseValueRaw = ReadLong(reader),
                CurrentValueRaw = ReadLong(reader)
            };
        }

        private static void WriteSetByCallerEntry(INetWriter writer, SetByCallerEntry value)
        {
            writer.WriteInt(value.TagHash);
            WriteLong(writer, value.ValueRaw);
        }

        private static SetByCallerEntry ReadSetByCallerEntry(INetReader reader)
        {
            return new SetByCallerEntry
            {
                TagHash = reader.ReadInt(),
                ValueRaw = ReadLong(reader)
            };
        }

        private static int GetSafeCount<T>(T[] values, int requestedCount, int maxCount, string name)
        {
            if (requestedCount < 0)
                throw new InvalidOperationException($"{name} count cannot be negative.");
            if (requestedCount > maxCount)
                throw new InvalidOperationException($"{name} count exceeds the configured limit.");
            if (requestedCount == 0)
                return 0;
            if (values == null || requestedCount > values.Length)
                throw new InvalidOperationException($"{name} count exceeds the available array length.");
            return requestedCount;
        }

        private static int ReadCount(INetReader reader, int maxCount, string name)
        {
            int count = reader.ReadInt();
            if (count < 0 || count > maxCount)
                throw new InvalidOperationException($"{name} count is outside the configured limit.");
            return count;
        }

        private static void WriteLong(INetWriter writer, long value)
        {
            writer.WriteUInt(unchecked((uint)value));
            writer.WriteUInt(unchecked((uint)(value >> 32)));
        }

        private static long ReadLong(INetReader reader)
        {
            uint low = reader.ReadUInt();
            uint high = reader.ReadUInt();
            return unchecked((long)((ulong)low | ((ulong)high << 32)));
        }

        private static void WriteULong(INetWriter writer, ulong value)
        {
            writer.WriteUInt(unchecked((uint)value));
            writer.WriteUInt(unchecked((uint)(value >> 32)));
        }

        private static ulong ReadULong(INetReader reader)
        {
            uint low = reader.ReadUInt();
            uint high = reader.ReadUInt();
            return (ulong)low | ((ulong)high << 32);
        }
    }
}
