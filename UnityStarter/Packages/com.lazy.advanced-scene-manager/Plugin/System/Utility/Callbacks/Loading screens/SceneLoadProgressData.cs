using AdvancedSceneManager.Core;
using scene = UnityEngine.SceneManagement.Scene;
using Scene = AdvancedSceneManager.Models.Scene;

namespace AdvancedSceneManager.Loading
{

    /// <summary>The default implementation of <see cref="ILoadProgressData"/>, used by ASM in most cases.</summary>
    public readonly struct SceneLoadProgressData : ILoadProgressData
    {

        public float value { get; }

        /// <summary>The scene that is being loaded or unloaded. Can be null.</summary>
        public Scene scene { get; }

        /// <summary>The operation that started this operation.</summary>
        public SceneOperation operation { get; }

        /// <summary>The kind of operation this is.</summary>
        public SceneOperationKind operationKind { get; }

        public SceneLoadProgressData(float value, SceneOperationKind operationKind, SceneOperation operation = null, Scene scene = null)
        {
            this.value = value;
            this.operationKind = operationKind;
            this.operation = operation;
            this.scene = scene;
        }

        public override string ToString()
        {
            return $"{operationKind} ({value * 100}%):{(scene ? scene.path : "Unknown")}";
        }

    }

}
