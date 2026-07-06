using CycloneGames.Choreography.Core;

namespace CycloneGames.Choreography
{
    /// <summary>
    /// Mutable, host-owned implementation of <see cref="IChoreographyProviderSet"/>. A composition root (or a
    /// scheduler component) registers whichever providers the host supports; unregistered members stay null and
    /// the scheduler skips the corresponding track kind with a warning. Not thread-safe: register during setup.
    /// </summary>
    public sealed class ChoreographyProviderRegistry : IChoreographyProviderSet
    {
        public IAnimationProvider Animation { get; private set; }

        public IAudioProvider Audio { get; private set; }

        public IVfxProvider Vfx { get; private set; }

        public IResourceProvider Resources { get; private set; }

        public void RegisterAnimation(IAnimationProvider provider) => Animation = provider;

        public void RegisterAudio(IAudioProvider provider) => Audio = provider;

        public void RegisterVfx(IVfxProvider provider) => Vfx = provider;

        public void RegisterResources(IResourceProvider provider) => Resources = provider;

        public void Clear()
        {
            Animation = null;
            Audio = null;
            Vfx = null;
            Resources = null;
        }
    }
}
