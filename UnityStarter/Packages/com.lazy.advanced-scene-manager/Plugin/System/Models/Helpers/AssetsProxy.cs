using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.Models.Internal;
using AdvancedSceneManager.Models.Utility;
using AdvancedSceneManager.Utility;

namespace AdvancedSceneManager.Models.Helpers
{

    /// <summary>Provides access to the scenes, collections and profiles managed by ASM.</summary>
    public sealed class AssetsProxy : IAssetsProvider
    {

        /// <summary>Enumerates all profiles in the project.</summary>
        public IEnumerable<Profile> profiles => Assets.profiles;

        /// <summary>Enumerates all scenes.</summary>
        public IEnumerable<Scene> scenes => Assets.scenes;

        /// <summary>Enumerates all collections.</summary>
        public IEnumerable<SceneCollection> collections => Assets.collections;

        /// <summary>Enumerates all templates.</summary>
        public IEnumerable<SceneCollectionTemplate> templates => Assets.collectionTemplates;

        /// <summary>Provides access to the default ASM scenes.</summary>
        public DefaultScenes defaults => SceneManager.settings.project.m_defaultScenes;

        /// <summary>Enumerates <typeparamref name="T"/>.</summary>
        public IEnumerable<T> Enumerate<T>() where T : ASMModel =>
          Assets.allAssets.OfType<T>().NonNull();

    }

}
