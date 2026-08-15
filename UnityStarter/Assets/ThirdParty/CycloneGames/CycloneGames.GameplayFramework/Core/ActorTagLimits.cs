namespace CycloneGames.GameplayFramework.Core
{
    public enum ActorTagValidationResult : byte
    {
        Valid = 0,
        NullOrWhiteSpace = 1,
        TooLong = 2,
    }

    /// <summary>Shared bounded validation contract for lightweight Actor tags.</summary>
    public static class ActorTagLimits
    {
        public const int MaximumTagCount = 64;
        public const int MaximumTagLength = 128;

        public static bool TryValidate(string tag, out ActorTagValidationResult result)
        {
            if (string.IsNullOrEmpty(tag))
            {
                result = ActorTagValidationResult.NullOrWhiteSpace;
                return false;
            }

            if (tag.Length > MaximumTagLength)
            {
                result = ActorTagValidationResult.TooLong;
                return false;
            }

            if (string.IsNullOrWhiteSpace(tag))
            {
                result = ActorTagValidationResult.NullOrWhiteSpace;
                return false;
            }

            result = ActorTagValidationResult.Valid;
            return true;
        }
    }
}
