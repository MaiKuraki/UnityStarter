using System;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Relationship between two teams. The numeric order matches the bit order used by
    /// <see cref="PerceptionAffiliation"/>, so a filter is a single shift and test.
    /// </summary>
    public enum PerceptionTeamAttitude : byte
    {
        Friendly = 0,
        Neutral = 1,
        Hostile = 2
    }

    /// <summary>
    /// Which attitudes a sensor reacts to. Three bits indexed by <see cref="PerceptionTeamAttitude"/>.
    /// </summary>
    /// <remarks>
    /// <c>default(PerceptionAffiliation)</c> is <see cref="None"/>, which detects nothing. Sensor
    /// configs initialise to <see cref="All"/>; only a hand-built job or a zeroed struct hits the
    /// empty default.
    /// </remarks>
    [Flags]
    public enum PerceptionAffiliation : byte
    {
        None = 0,
        Friendlies = 1 << PerceptionTeamAttitude.Friendly,
        Neutrals = 1 << PerceptionTeamAttitude.Neutral,
        Enemies = 1 << PerceptionTeamAttitude.Hostile,
        All = Friendlies | Neutrals | Enemies
    }

    /// <summary>
    /// Optional team identity for a perceptible. Implement it when the game has teams; a perceptible
    /// without it is treated as <see cref="PerceptionTeam.NoTeam"/>, which resolves to a neutral
    /// attitude.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="IPerceptible"/> so a game without teams - and every third-party
    /// adapter implementing <see cref="IPerceptible"/> - does not have to invent a team id.
    /// </remarks>
    public interface IPerceptionTeamProvider
    {
        /// <summary>Team index in 0..254. 255 means no team.</summary>
        int TeamId { get; }
    }

    /// <summary>
    /// Team identity shared by perceptibles, sensors and the attitude table.
    /// </summary>
    public static class PerceptionTeam
    {
        /// <summary>Team index meaning "no team". Attitude towards it is always neutral.</summary>
        public const byte NoTeam = 255;

        /// <summary>Number of addressable team indices, including <see cref="NoTeam"/>.</summary>
        public const int TeamCount = 256;

        /// <summary>Size in bytes of the attitude table owned by one sensor manager.</summary>
        public const int AttitudeTableSize = TeamCount * TeamCount;

        /// <summary>
        /// Attitude lookup index. The observer row is contiguous, so a sensor only touches
        /// <see cref="TeamCount"/> bytes of the table no matter how many teams exist.
        /// </summary>
        public static int AttitudeIndex(int observerTeam, int targetTeam)
        {
            return observerTeam * TeamCount + targetTeam;
        }
    }
}
