using System.Collections.Generic;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Helpers;
using AdvancedSceneManager.Models.Utility;

namespace AdvancedSceneManager.DependencyInjection
{

    /// <inheritdoc cref="SceneManager.assets"/>
    public interface IAssetsProvider : DependencyInjectionUtility.IInjectable
    {

        DefaultScenes defaults { get; }

        IEnumerable<SceneCollection> collections { get; }
        IEnumerable<Profile> profiles { get; }
        IEnumerable<Scene> scenes { get; }
        IEnumerable<SceneCollectionTemplate> templates { get; }
        IEnumerable<T> Enumerate<T>() where T : ASMModel;

    }

}
