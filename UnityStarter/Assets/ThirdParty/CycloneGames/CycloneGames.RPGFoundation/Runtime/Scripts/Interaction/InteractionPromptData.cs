using System;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    [Serializable]
    public struct InteractionPromptData : IEquatable<InteractionPromptData>
    {
        public string LocalizationTableName;
        public string LocalizationKey;
        public string FallbackText;

        public InteractionPromptData(string tableName, string localizationKey, string fallbackText = "")
        {
            LocalizationTableName = tableName;
            LocalizationKey = localizationKey;
            FallbackText = fallbackText;
        }

        public bool IsValid => !string.IsNullOrEmpty(LocalizationTableName) && !string.IsNullOrEmpty(LocalizationKey);

        public bool Equals(InteractionPromptData other) =>
            LocalizationTableName == other.LocalizationTableName &&
            LocalizationKey == other.LocalizationKey;

        public override bool Equals(object obj) => obj is InteractionPromptData other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(LocalizationTableName, LocalizationKey);
    }
}