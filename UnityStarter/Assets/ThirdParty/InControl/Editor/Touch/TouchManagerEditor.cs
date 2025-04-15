#if UNITY_EDITOR
namespace InControl
{
	using UnityEditor;
	using UnityEngine;


	[CustomEditor( typeof(TouchManager) )]
	public class TouchManagerEditor : Editor
	{
		TouchManager touchManager;
		Texture headerTexture;


		void OnEnable()
		{
			touchManager = target as TouchManager;
			headerTexture = Internal.EditorTextures.TouchManagerHeader;
		}


		public override void OnInspectorGUI()
		{
			GUILayout.Space( 5.0f );

			var headerRect = GUILayoutUtility.GetRect( 0.0f, -22.0f );
			headerRect.width = headerTexture.width / 2.0f;
			headerRect.height = headerTexture.height / 2.0f;
			GUILayout.Space( headerRect.height );

			DrawDefaultInspector();

			var controlsLayer = EditorGUILayout.LayerField( "Controls Layer", touchManager.controlsLayer );
			if (touchManager.controlsLayer != controlsLayer)
			{
				touchManager.controlsLayer = controlsLayer;
				TouchBuilder.ChangeControlsLayer( controlsLayer );
			}

			touchManager.controlsEnabled = EditorGUILayout.Toggle( "Controls Enabled", touchManager.controlsEnabled );
			touchManager.enableControlsOnTouch = EditorGUILayout.Toggle( "Enable Controls On Touch", touchManager.enableControlsOnTouch );

			WarnAboutCamerasIncludingControlsLayer( controlsLayer );
			WarnAboutControlsNotUsingControlsLayer( controlsLayer );

			GUI.DrawTexture( headerRect, headerTexture );

			GUILayout.Space( 5.0f );
			GUILayout.Label( "Add Controls", EditorStyles.boldLabel );

			if (GUILayout.Button( "Create Button Control" ))
			{
				TouchBuilder.CreateTouchButtonControl();
			}

			if (GUILayout.Button( "Create Stick Control" ))
			{
				TouchBuilder.CreateTouchStickControl();
			}

			if (GUILayout.Button( "Create Swipe Control" ))
			{
				TouchBuilder.CreateTouchSwipeControl();
			}

			if (GUILayout.Button( "Create Track Control" ))
			{
				TouchBuilder.CreateTouchTrackControl();
			}
		}


		void WarnAboutCamerasIncludingControlsLayer( int controlsLayer )
		{
			GUIStyle style = new GUIStyle( GUI.skin.box );
			style.alignment = TextAnchor.UpperLeft;
			style.padding.left = 10;
			style.padding.right = 10;
			style.richText = true;
			bool showWarning = false;
			var text = "" +
			           "<b>Warning:</b>\n" +
			           "Some cameras are set to include the current touch controls layer (" +
			           LayerMask.LayerToName( controlsLayer ) +
			           ") in their culling mask. This may cause duplicates ghosting of controls or other " +
			           "unexpected visual results.";

			foreach (var camera in Camera.allCameras)
			{
				if (camera != touchManager.touchCamera && (camera.cullingMask & (1 << controlsLayer)) > 0)
				{
					text += "\n •  " + camera.gameObject.name;
					showWarning = true;
				}
			}

			if (showWarning)
			{
				GUI.backgroundColor = new Color( 1.0f, 0.8f, 0.8f, 1.0f );
				GUILayout.Box( text, style, GUILayout.ExpandWidth( true ) );
				GUI.backgroundColor = Color.white;
			}
		}


		void WarnAboutControlsNotUsingControlsLayer( int controlsLayer )
		{
			GUIStyle style = new GUIStyle( GUI.skin.box );
			style.alignment = TextAnchor.UpperLeft;
			style.padding.left = 10;
			style.padding.right = 10;
			style.richText = true;
			bool showWarning = false;
			var text = "" +
			           "<b>Warning:</b>\n" +
			           "Some controls are not on the current touch controls layer (" +
			           LayerMask.LayerToName( controlsLayer ) +
			           "). This will most likely cause these controls to both render and function incorrectly.";

			foreach (var control in FindObjectsByType<TouchControl>( FindObjectsSortMode.None ))
			{
				if (control.gameObject.layer != controlsLayer)
				{
					text += "\n •  " + control.gameObject.name;
					showWarning = true;
				}
			}

			if (showWarning)
			{
				GUI.backgroundColor = new Color( 1.0f, 0.8f, 0.8f, 1.0f );
				GUILayout.Box( text, style, GUILayout.ExpandWidth( true ) );
				GUI.backgroundColor = Color.white;
			}
		}
	}
}
#endif
