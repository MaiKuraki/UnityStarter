using System;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionTargetSnapshot
    {
        public static readonly string[] EmptyActions = Array.Empty<string>();

        public readonly int WorldId;
        public readonly ulong TargetStableId;
        public readonly InteractionVector3 Position;
        public readonly float InteractionRange;
        public readonly bool IsAvailable;
        public readonly bool AllowDefaultAction;
        public readonly string[] EnabledActionIds;
        public readonly int Version;

        public InteractionTargetSnapshot(
            int worldId,
            ulong targetStableId,
            InteractionVector3 position,
            float interactionRange,
            bool isAvailable,
            bool allowDefaultAction = true,
            string[] enabledActionIds = null,
            int version = 0)
        {
            WorldId = worldId;
            TargetStableId = targetStableId;
            Position = position;
            InteractionRange = interactionRange > 0f ? interactionRange : 0f;
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
    }
}
