#if USING_FPS_COUNTER
using System;
using System.Reflection;

/// <summary>
/// Utility class for toggling FPSCounter visibility via reflection.
/// Enable by removing #if USING_FPS_COUNTER directive.
/// </summary>
public static class FPSCounterManager
{
    private static bool _isFPSVisible = false;
    
    private static Type _fpsCounterType;
    private static FieldInfo _isVisibleField;
    private static bool _reflectionInitialized;
    private static bool _reflectionFailed;

    public static void ToggleFPSCounter()
    {
        _isFPSVisible = !_isFPSVisible;
        SetFPSVisibility(_isFPSVisible);
    }

    private static void SetFPSVisibility(bool isVisible)
    {
        if (_reflectionFailed) return;

        try
        {
            if (!_reflectionInitialized)
            {
                InitializeReflectionCache();
                if (_reflectionFailed) return;
            }

            var fpsCounters = UnityEngine.Object.FindObjectsByType(_fpsCounterType, UnityEngine.FindObjectsSortMode.None);

            if (fpsCounters.Length == 0)
            {
                UnityEngine.Debug.LogWarning("[FPSCounterManager] No FPSCounter instances found");
                return;
            }

            foreach (UnityEngine.Object counter in fpsCounters)
            {
                _isVisibleField.SetValue(counter, isVisible);
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[FPSCounterManager] Error setting visibility: {ex.Message}");
        }
    }

    private static void InitializeReflectionCache()
    {
        _fpsCounterType = Type.GetType("CycloneGames.Utility.Runtime.FPSCounter, CycloneGames.Utility.Runtime");
        
        if (_fpsCounterType == null)
        {
            _fpsCounterType = Type.GetType("CycloneGames.Utility.Runtime.FPSCounter, Assembly-CSharp");
        }

        if (_fpsCounterType == null)
        {
            UnityEngine.Debug.LogError("[FPSCounterManager] FPSCounter type not found");
            _reflectionFailed = true;
            return;
        }

        _isVisibleField = _fpsCounterType.GetField("IsVisible", BindingFlags.Public | BindingFlags.Instance);

        if (_isVisibleField == null)
        {
            UnityEngine.Debug.LogError("[FPSCounterManager] IsVisible field not found");
            _reflectionFailed = true;
            return;
        }

        _reflectionInitialized = true;
    }
}
#endif