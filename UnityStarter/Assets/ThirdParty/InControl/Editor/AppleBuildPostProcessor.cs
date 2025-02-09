#if UNITY_EDITOR && (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS)
namespace InControl
{
	using System.IO;
	using UnityEditor;
	using UnityEditor.Callbacks;
	using UnityEditor.iOS.Xcode;


	// The native plugin on Apple platforms requires CoreHaptics.framework.
	// This build post-processor is responsible for adding it to the UnityFramework target.
	public static class AppleBuildPostProcessor
	{
		[PostProcessBuildAttribute( 1 )]
		public static void OnPostProcessBuild( BuildTarget target, string path )
		{
			if (!ShouldPostProcessBuild( target )) return;

			var projectPath = PBXProject.GetPBXProjectPath( path );
			
			#if UNITY_VISIONOS
			if (target == BuildTarget.VisionOS)
			{
				projectPath = projectPath.Replace("Unity-iPhone.xcodeproj", "Unity-VisionOS.xcodeproj");
			}
			#endif
			
			var project = new PBXProject();
			project.ReadFromString( File.ReadAllText( projectPath ) );
			var targetGuid = project.GetUnityFrameworkTargetGuid();

			project.AddFrameworkToProject( targetGuid, "CoreHaptics.framework", false );

			File.WriteAllText( projectPath, project.WriteToString() );
		}


		static bool ShouldPostProcessBuild( BuildTarget target )
		{
			if (target == BuildTarget.iOS) return true;
			if (target == BuildTarget.tvOS) return true;
			#if UNITY_VISIONOS
			if (target == BuildTarget.VisionOS) return true;
			#endif
			return false;
		}
	}
}
#endif