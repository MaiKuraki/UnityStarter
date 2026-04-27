using CycloneGames.Networking;

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
        public float Duration;
        public float TimeRemaining;
        public float PeriodTimeRemaining;
        public int PredictionKey;
        public int PredictionKeyOwner;
        public int PredictionInputSequence;
        public int SetByCallerCount;
        public SetByCallerEntry[] SetByCallerEntries;
    }

    public struct SetByCallerEntry
    {
        public int TagHash;
        public float Value;
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
        public float Duration;
        public float TimeRemaining;
        public float PeriodTimeRemaining;
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
        public float BaseValue;
        public float CurrentValue;
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
}
