#if UNITY_EDITOR
namespace InControl
{
	using UnityEditor;
	using UnityEngine;


	static class TouchBuilder
	{
		[MenuItem( "GameObject/InControl/Touch/Manager", false, 2 )]
		static void CreateTouchManager()
		{
			foreach (var component in Object.FindObjectsByType<TouchManager>( FindObjectsSortMode.None ))
			{
				Debug.LogWarning( "Existing Touch Manager component on game object '" + component.gameObject.name + "' will be destroyed." );
				Object.DestroyImmediate( component );
			}

			var manager = Object.FindFirstObjectByType<InControlManager>();
			if (manager)
			{
				var gameObject = manager.gameObject;
				gameObject.AddComponent<TouchManager>();
				Selection.activeGameObject = gameObject;
				Debug.Log( "Touch Manager component has been attached to the InControl Manager object." );
				return;
			}
			
			Debug.LogError( "Could not find InControl Manager object." );
		}


		[MenuItem( "GameObject/InControl/Touch/Button Control", false, 3 )]
		public static void CreateTouchButtonControl()
		{
			var touchManager = Object.FindFirstObjectByType<TouchManager>();
			if (touchManager != null)
			{
				var gameObject = touchManager.gameObject;

				var controlGameObject = new GameObject( "Touch Button Control" );
				controlGameObject.transform.parent = gameObject.transform;
				controlGameObject.layer = touchManager.controlsLayer;

				var control = controlGameObject.AddComponent<TouchButtonControl>();
				control.button.Sprite = FindSpriteWithName( "TouchButton_A" );

				Selection.activeGameObject = controlGameObject;

				Debug.Log( "Touch Button Control object has been created." );
			}
			else
			{
				Debug.LogError( "Could not find InControl Manager object." );
			}
		}


		[MenuItem( "GameObject/InControl/Touch/Stick Control", false, 3 )]
		public static void CreateTouchStickControl()
		{
			var touchManager = Object.FindFirstObjectByType<TouchManager>();
			if (touchManager != null)
			{
				var gameObject = touchManager.gameObject;

				var controlGameObject = new GameObject( "Touch Stick Control" );
				controlGameObject.transform.parent = gameObject.transform;
				controlGameObject.layer = touchManager.controlsLayer;

				var control = controlGameObject.AddComponent<TouchStickControl>();
				control.ring.Sprite = FindSpriteWithName( "TouchStick_Ring" );
				control.knob.Sprite = FindSpriteWithName( "TouchStick_Knob" );

				Selection.activeGameObject = controlGameObject;

				Debug.Log( "Touch Stick Control object has been created." );
			}
			else
			{
				Debug.LogError( "Could not find InControl Manager object." );
			}
		}


		[MenuItem( "GameObject/InControl/Touch/Swipe Control", false, 3 )]
		public static void CreateTouchSwipeControl()
		{
			var touchManager = Object.FindFirstObjectByType<TouchManager>();
			if (touchManager != null)
			{
				var gameObject = touchManager.gameObject;

				var controlGameObject = new GameObject( "Touch Swipe Control" );
				controlGameObject.transform.parent = gameObject.transform;
				controlGameObject.AddComponent<TouchSwipeControl>();
				controlGameObject.layer = touchManager.controlsLayer;

				Selection.activeGameObject = controlGameObject;

				Debug.Log( "Touch Swipe Control object has been created." );
			}
			else
			{
				Debug.LogError( "Could not find InControl Manager object." );
			}
		}


		[MenuItem( "GameObject/InControl/Touch/Track Control", false, 3 )]
		public static void CreateTouchTrackControl()
		{
			var touchManager = Object.FindFirstObjectByType<TouchManager>();
			if (touchManager != null)
			{
				var gameObject = touchManager.gameObject;

				var controlGameObject = new GameObject( "Touch Track Control" );
				controlGameObject.transform.parent = gameObject.transform;
				controlGameObject.AddComponent<TouchTrackControl>();
				controlGameObject.layer = touchManager.controlsLayer;

				Selection.activeGameObject = controlGameObject;

				Debug.Log( "Touch Track Control object has been created." );
			}
			else
			{
				Debug.LogError( "Could not find InControl Manager object." );
			}
		}


		public static void ChangeControlsLayer( int layer )
		{
			TouchManager.Instance.touchCamera.cullingMask = 1 << layer;

			foreach (var control in Object.FindObjectsByType<TouchControl>( FindObjectsSortMode.None ))
			{
				foreach (var transform in control.gameObject.GetComponentsInChildren<Transform>( true ))
				{
					transform.gameObject.layer = layer;
				}
			}
		}


		static Sprite FindSpriteWithName( string name )
		{
			foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
			{
				if (sprite.name == name)
				{
					return sprite;
				}
			}

			return null;
		}
	}
}
#endif
