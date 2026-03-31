#if USING_FPS_COUNTER

/// <summary>
/// Utility class for toggling FPSCounter visibility.
/// Enable by defining USING_FPS_COUNTER in your scripting define symbols.
///
/// ── Reflection Fallback (for projects that do NOT reference this DLL) ──
/// If your assembly cannot directly reference CycloneGames.Utility.Runtime,
/// you can use reflection to toggle the FPS counter. Copy the snippet below
/// into your own code. Note: reflection is slower, has GC overhead, and may
/// break under IL2CPP stripping — prefer the direct reference approach above.
///
/// <code>
/// using System;
/// using System.Reflection;
///
/// public static class FPSCounterReflection
/// {
///     private static Type _type;
///     private static PropertyInfo _instanceProp;
///     private static FieldInfo _isVisibleField;
///
///     public static void Toggle()
///     {
///         if (_type == null)
///         {
///             _type = Type.GetType("CycloneGames.Utility.Runtime.FPSCounter, CycloneGames.Utility.Runtime");
///             if (_type == null) return;
///             _instanceProp = _type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
///             _isVisibleField = _type.GetField("IsVisible", BindingFlags.Public | BindingFlags.Instance);
///         }
///         var instance = _instanceProp?.GetValue(null);
///         if (instance == null || _isVisibleField == null) return;
///         bool current = (bool)_isVisibleField.GetValue(instance);
///         _isVisibleField.SetValue(instance, !current);
///     }
/// }
/// </code>
///
/// IMPORTANT: When using IL2CPP, add a link.xml to prevent stripping:
/// <code>
/// <linker>
///   <assembly fullname="CycloneGames.Utility.Runtime" preserve="all"/>
/// </linker>
/// </code>
/// </summary>
public static class FPSCounterManager
{
    public static void ToggleFPSCounter()
    {
        var instance = CycloneGames.Utility.Runtime.FPSCounter.Instance;
        if (instance == null)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.LogWarning("[FPSCounterManager] No FPSCounter instance found.");
#endif
            return;
        }
        instance.SetVisible(!instance.IsVisible);
    }

    public static void SetFPSVisibility(bool isVisible)
    {
        var instance = CycloneGames.Utility.Runtime.FPSCounter.Instance;
        if (instance == null)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.LogWarning("[FPSCounterManager] No FPSCounter instance found.");
#endif
            return;
        }
        instance.SetVisible(isVisible);
    }
}
#endif