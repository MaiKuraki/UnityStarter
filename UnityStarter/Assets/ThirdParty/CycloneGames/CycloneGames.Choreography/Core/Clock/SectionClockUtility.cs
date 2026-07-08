namespace CycloneGames.Choreography.Core
{
    internal static class SectionClockUtility
    {
        public static bool IsExternalSection(ChoreographySectionClockSource source)
        {
            return source == ChoreographySectionClockSource.Audio
                || source == ChoreographySectionClockSource.Animation
                || source == ChoreographySectionClockSource.Timeline
                || source == ChoreographySectionClockSource.External;
        }
    }
}
