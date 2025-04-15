#if UNITY_EDITOR
namespace InControl
{
	using UnityEditor;
	using UnityEngine;


	static class InControlBuilder
	{
		[MenuItem( "GameObject/InControl/Manager", false, 100 )]
		static void CreateInputManager()
		{
			var component = Object.FindFirstObjectByType<InControlManager>();
			if (component)
			{
				Selection.activeGameObject = component.gameObject;

				Debug.LogError( "InControlManager component is already attached to selected object." );
				return;
			}

			var gameObject = GameObject.Find( "InControl" ) ?? new GameObject( "InControl" );
			gameObject.AddComponent<InControlManager>();
			Selection.activeGameObject = gameObject;

			Debug.Log( "InControl manager object has been created." );
		}
	}
}
#endif
