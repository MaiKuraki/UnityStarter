namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    [System.Serializable]
    public struct InteractionPromptData
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
    }
}