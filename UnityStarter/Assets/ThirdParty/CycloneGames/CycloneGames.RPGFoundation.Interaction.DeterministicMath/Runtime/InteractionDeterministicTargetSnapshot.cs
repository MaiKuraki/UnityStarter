using System;
using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath
{
    public readonly struct InteractionDeterministicTargetSnapshot
    {
        public static readonly string[] EmptyActions = Array.Empty<string>();

        public readonly int WorldId;
        public readonly ulong TargetStableId;
        public readonly FPVector3 Position;
        public readonly FPInt64 InteractionRange;
        public readonly bool IsAvailable;
        public readonly bool AllowDefaultAction;
        public readonly string[] EnabledActionIds;
        public readonly int Version;

        public InteractionDeterministicTargetSnapshot(
            int worldId,
            ulong targetStableId,
            FPVector3 position,
            FPInt64 interactionRange,
            bool isAvailable,
            bool allowDefaultAction = true,
            string[] enabledActionIds = null,
            int version = 0)
        {
            WorldId = worldId;
            TargetStableId = targetStableId;
            Position = position;
            InteractionRange = interactionRange.RawValue > 0L ? interactionRange : FPInt64.Zero;
            IsAvailable = isAvailable;
            AllowDefaultAction = allowDefaultAction;
            EnabledActionIds = enabledActionIds ?? EmptyActions;
            Version = version;
        }

        public bool IsValid => TargetStableId != InteractionStableId.None;

        public bool CanExecuteAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                return AllowDefaultAction;
            }

            string[] actionIds = EnabledActionIds ?? EmptyActions;
            for (int i = 0; i < actionIds.Length; i++)
            {
                if (string.Equals(actionIds[i], actionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public InteractionDeterministicVector3Payload ToPositionPayload()
        {
            return new InteractionDeterministicVector3Payload(Position);
        }

        public InteractionTargetSnapshot ToInteractionTargetSnapshot()
        {
            return new InteractionTargetSnapshot(
                WorldId,
                TargetStableId,
                Position.ToInteractionVector3(),
                InteractionRange.ToFloat(),
                IsAvailable,
                AllowDefaultAction,
                EnabledActionIds,
                Version);
        }
    }
}
