namespace AdvancedSceneManager.Loading
{

    /// <summary>Represents progress in ASM. Used for <see cref="ILoadProgressListener"/>.</summary>
    public interface ILoadProgressData
    {
        /// <summary>The current load percent.</summary>
        float value { get; }
    }

}