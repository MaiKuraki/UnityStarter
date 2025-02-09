using System;

namespace AdvancedSceneManager.Utility.CrossSceneReferences
{

    /// <summary>A collection of <see cref="CrossSceneReference"/> for a scene.</summary>
    [Serializable]
    public class SceneReferenceCollection
    {
        public string scene;
        public CrossSceneReference[] references;
    }

}
