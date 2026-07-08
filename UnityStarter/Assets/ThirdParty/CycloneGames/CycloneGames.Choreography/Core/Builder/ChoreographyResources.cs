namespace CycloneGames.Choreography.Core
{
    public static class ChoreographyResources
    {
        public static ChoreographyResourceReference Animation(string address, string tag = null)
        {
            return new ChoreographyResourceReference(address, ChoreographyResourceKind.Animation, tag);
        }

        public static ChoreographyResourceReference AudioClip(string address, string tag = null)
        {
            return new ChoreographyResourceReference(address, ChoreographyResourceKind.AudioClip, tag);
        }

        public static ChoreographyResourceReference AudioEvent(string eventName, string provider = null, string bank = null, string tag = null)
        {
            return new ChoreographyResourceReference(eventName, ChoreographyResourceKind.AudioEvent, tag, provider, bank);
        }

        public static ChoreographyResourceReference BackendCue(string cue, string provider, string group = null, string tag = null)
        {
            return new ChoreographyResourceReference(cue, ChoreographyResourceKind.BackendCue, tag, provider, group);
        }

        public static ChoreographyResourceReference Vfx(string address, string tag = null)
        {
            return new ChoreographyResourceReference(address, ChoreographyResourceKind.Vfx, tag);
        }
    }
}
