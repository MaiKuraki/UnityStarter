#if USING_FPS_COUNTER
// if you are using the CycloneGames.Utility.Runtime.FPSCounter in your project, remove the #if USING_FPS_COUNTER to enable the FPSCounter manager.
// You can use this script into your own debug tools, such as 'Unity Debug Sheet' or 'SRDebugger' or any other debug tool.

using System;
using System.Reflection;

public static class FPSCounterManager
{
    public static void ToggleFPSCounter()
    {
        IsFPSVisible = !IsFPSVisible;
        SetFPSVisibility(IsFPSVisible);
    }
    
    private static bool IsFPSVisible = false;
    private static void SetFPSVisibility(bool isVisible)
    {
        try
        {
            Type fpsType = Type.GetType("CycloneGames.Utility.Runtime.FPSCounter, Assembly-CSharp");

            if (fpsType == null)
            {
                fpsType = Type.GetType("CycloneGames.Utility.Runtime.FPSCounter, CycloneGames.Utility.Runtime");

                if (fpsType == null)
                {
                    UnityEngine.Debug.LogError("FPSCounter type not found");
                    return;
                }
            }
            UnityEngine.Object[] fpsCounters = UnityEngine.Object.FindObjectsByType(fpsType, UnityEngine.FindObjectsSortMode.None);

            if (fpsCounters.Length == 0)
            {
                UnityEngine.Debug.LogWarning("No FPSCounter instances found");
                return;
            }
            FieldInfo field = fpsType.GetField("IsVisible",
                BindingFlags.Public | BindingFlags.Instance);

            if (field == null)
            {
                UnityEngine.Debug.LogError("IsVisible field not found");
                return;
            }
            foreach (UnityEngine.Object counter in fpsCounters)
            {
                field.SetValue(counter, isVisible);
            }
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"Reflection failed: {ex}");
        }
    }
}

#endif