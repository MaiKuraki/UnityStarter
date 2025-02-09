using AdvancedSceneManager.Utility;

namespace AdvancedSceneManager.Loading
{

    /// <summary>Represents a listener for when progress changes.
    /// <list type="bullet">
    /// <item>Can be registered using <see cref="LoadingScreenUtility.RegisterLoadProgressListener(ILoadProgressListener)"/>.</item>
    /// <item>Progress can be reported using <see cref="LoadingScreenUtility.ReportProgress(ILoadProgressData)"/>.</item>
    /// </list>
    /// </summary>
    public interface ILoadProgressListener
    {
        /// <summary>Called when progress has changed.</summary>
        void OnProgressChanged(ILoadProgressData progress);
    }

}
