#if UNITY_EDITOR
namespace InControl
{
	using System.IO;
	using UnityEngine;
	using UnityEditor;


	// This file exists so editor scripts can find the plugin path.
	// @cond nodoc
	public class InControlPluginsPath : ScriptableObject
	{
		public static string Get()
		{
			var instance = CreateInstance<InControlPluginsPath>();
			var pluginsPath = Path.GetDirectoryName( AssetDatabase.GetAssetPath( MonoScript.FromScriptableObject( instance ) ) );
			DestroyImmediate( instance );
			return pluginsPath;
		}
	}

	// @endcond
}
#endif
