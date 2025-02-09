using UnityEditor;

namespace AdvancedSceneManager.Utility
{

    /// <summary>Initializes a class in editor on recompile.</summary>
    /// <remarks>Available in build, but no effect.</remarks>
    class InitializeInEditorMethodAttribute
#if UNITY_EDITOR
        : InitializeOnLoadMethodAttribute
#else
        : System.Attribute
#endif
    { }

    /// <summary>Initializes a class in editor on recompile.</summary>
    /// <remarks>Available in build, but no effect.</remarks>
    class InitializeInEditorAttribute
#if UNITY_EDITOR
        : InitializeOnLoadAttribute
#else
        : System.Attribute
#endif
    { }

}
