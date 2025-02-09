#if UNITY_EDITOR

namespace AdvancedSceneManager.DependencyInjection.Editor
{

    /// <inheritdoc cref="SceneManager.package"/>
    public interface IPackage : DependencyInjectionUtility.IInjectable
    {
        string folder { get; }
        string version { get; }
    }

}

#endif
