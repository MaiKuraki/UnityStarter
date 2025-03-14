#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace CycloneGames.Utility.Runtime
{
    public sealed class SplashScreenModifier : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            RemoveAllLogos();
            SetSplashScreenBackground();
        }

        private static void RemoveAllLogos() => PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[0];

        private static void SetSplashScreenBackground()
        {
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
            PlayerSettings.SplashScreen.backgroundColor = Color.black;
        }
    }
}
#endif