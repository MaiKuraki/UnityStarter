#if UNITY_EDITOR
using UnityEditor;


namespace InControl
{
	using System;
	using UnityEngine;
	using Internal;


	[CustomEditor( typeof(InControlManager) )]
	public class InControlManagerEditor : Editor
	{
		SerializedProperty logDebugInfo;
		SerializedProperty invertYAxis;
		SerializedProperty useFixedUpdate;
		SerializedProperty dontDestroyOnLoad;
		SerializedProperty suspendInBackground;
		SerializedProperty updateMode;

		SerializedProperty enableICade;

		SerializedProperty enableXInput;
		SerializedProperty xInputOverrideUpdateRate;
		SerializedProperty xInputUpdateRate;
		SerializedProperty xInputOverrideBufferSize;
		SerializedProperty xInputBufferSize;

		SerializedProperty enableNativeInput;
		SerializedProperty nativeInputEnableXInput;
		SerializedProperty nativeInputEnableMFi;
		SerializedProperty nativeInputPreventSleep;
		SerializedProperty nativeInputOverrideUpdateRate;
		SerializedProperty nativeInputUpdateRate;

		Texture headerTexture;


		void OnEnable()
		{
			logDebugInfo = serializedObject.FindProperty( "logDebugInfo" );
			invertYAxis = serializedObject.FindProperty( "invertYAxis" );
			useFixedUpdate = serializedObject.FindProperty( "useFixedUpdate" );
			dontDestroyOnLoad = serializedObject.FindProperty( "dontDestroyOnLoad" );
			suspendInBackground = serializedObject.FindProperty( "suspendInBackground" );
			updateMode = serializedObject.FindProperty( "updateMode" );

			enableICade = serializedObject.FindProperty( "enableICade" );

			enableXInput = serializedObject.FindProperty( "enableXInput" );
			xInputOverrideUpdateRate = serializedObject.FindProperty( "xInputOverrideUpdateRate" );
			xInputUpdateRate = serializedObject.FindProperty( "xInputUpdateRate" );
			xInputOverrideBufferSize = serializedObject.FindProperty( "xInputOverrideBufferSize" );
			xInputBufferSize = serializedObject.FindProperty( "xInputBufferSize" );

			enableNativeInput = serializedObject.FindProperty( "enableNativeInput" );
			nativeInputEnableXInput = serializedObject.FindProperty( "nativeInputEnableXInput" );
			nativeInputEnableMFi = serializedObject.FindProperty( "nativeInputEnableMFi" );
			nativeInputPreventSleep = serializedObject.FindProperty( "nativeInputPreventSleep" );
			nativeInputOverrideUpdateRate = serializedObject.FindProperty( "nativeInputOverrideUpdateRate" );
			nativeInputUpdateRate = serializedObject.FindProperty( "nativeInputUpdateRate" );

			headerTexture = EditorTextures.InControlHeader;
		}


		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			GUILayout.Space( 5.0f );

			var headerRect = GUILayoutUtility.GetRect( 0.0f, 5.0f );
			headerRect.width = headerTexture.width / 2;
			headerRect.height = headerTexture.height / 2;
			GUILayout.Space( headerRect.height );
			GUI.DrawTexture( headerRect, headerTexture );

			EditorUtility.SetTintColor();
			var versionStyle = new GUIStyle( EditorUtility.wellStyle );
			versionStyle.alignment = TextAnchor.MiddleCenter;
			GUILayout.Box( "Version " + InputManager.Version, versionStyle, GUILayout.ExpandWidth( true ) );
			EditorUtility.PopTintColor();

			EditorUtility.BeginGroup( "General Settings" );

			logDebugInfo.boolValue = EditorGUILayout.ToggleLeft( "Log Debug Info", logDebugInfo.boolValue, EditorUtility.labelStyle );
			invertYAxis.boolValue = EditorGUILayout.ToggleLeft( "Invert Y Axis", invertYAxis.boolValue, EditorUtility.labelStyle );

			dontDestroyOnLoad.boolValue = EditorGUILayout.ToggleLeft( "Don't Destroy On Load <color=#777>(Recommended)</color>", dontDestroyOnLoad.boolValue, EditorUtility.labelStyle );
			suspendInBackground.boolValue = EditorGUILayout.ToggleLeft( "Suspend In Background", suspendInBackground.boolValue, EditorUtility.labelStyle );

			GUILayout.Space( 5.0f );
			var rect = EditorGUILayout.GetControlRect( false, 1 );
			rect.height = 1;
			EditorGUI.DrawRect( rect, EditorGUIUtility.isProSkin ? new Color( 1, 1, 1, 0.2f ) : new Color( 0, 0, 0, 0.2f ) );
			GUILayout.Space( 5.0f );

			var selectedUpdateMode = (InControlUpdateMode) Enum.GetValues( typeof(InControlUpdateMode) ).GetValue( updateMode.enumValueIndex );
			if (useFixedUpdate.boolValue)
			{
				selectedUpdateMode = InControlUpdateMode.FixedUpdate;
				useFixedUpdate.boolValue = false;
			}

			updateMode.enumValueIndex = (int) (InControlUpdateMode) EditorGUILayout.EnumPopup( "Update Mode", selectedUpdateMode );
			// EditorGUILayout.PropertyField( updateMode );

			EditorUtility.EndGroup();


			EditorUtility.GroupTitle( "Enable ICade <color=#777>- iOS/tvOS</color>", enableICade );


			EditorUtility.GroupTitle( "Enable XInput <color=#777>- Windows, Deprecated</color>", enableXInput );
			if (enableXInput.boolValue)
			{
				EditorUtility.BeginGroup();

				xInputOverrideUpdateRate.boolValue = EditorGUILayout.ToggleLeft( "Override Update Rate <color=#777>(Not Recommended)</color>", xInputOverrideUpdateRate.boolValue, EditorUtility.labelStyle );
				xInputUpdateRate.intValue = xInputOverrideUpdateRate.boolValue ? Mathf.Max( EditorGUILayout.IntField( "Update Rate (Hz)", xInputUpdateRate.intValue ), 0 ) : 0;

				xInputOverrideBufferSize.boolValue = EditorGUILayout.ToggleLeft( "Override Buffer Size <color=#777>(Not Recommended)</color>", xInputOverrideBufferSize.boolValue, EditorUtility.labelStyle );
				xInputBufferSize.intValue = xInputOverrideBufferSize.boolValue ? Mathf.Max( xInputBufferSize.intValue, EditorGUILayout.IntField( "Buffer Size", xInputBufferSize.intValue ), 0 ) : 0;

				EditorUtility.EndGroup();
			}


			EditorUtility.GroupTitle( "Enable Native Input <color=#777>- Windows/macOS/iOS/tvOS</color>", enableNativeInput );
			if (enableNativeInput.boolValue)
			{
				EditorUtility.BeginGroup();

				const string text1 = "" +
				                     "Enabling native input will disable using Unity input internally, " +
				                     "but should provide more efficient and robust input support.";
				EditorUtility.SetTintColor();
				GUILayout.Box( text1, EditorUtility.wellStyle, GUILayout.ExpandWidth( true ) );
				EditorUtility.PopTintColor();

				nativeInputEnableXInput.boolValue = EditorGUILayout.ToggleLeft( "Enable XInput Support <color=#777>(Windows, Recommended)</color>", nativeInputEnableXInput.boolValue, EditorUtility.labelStyle );
				nativeInputEnableMFi.boolValue = EditorGUILayout.ToggleLeft( "Enable MFi Support <color=#777>(macOS, Recommended)</color>", nativeInputEnableMFi.boolValue, EditorUtility.labelStyle );
				nativeInputPreventSleep.boolValue = EditorGUILayout.ToggleLeft( "Prevent Screensaver / Sleep", nativeInputPreventSleep.boolValue, EditorUtility.labelStyle );

				nativeInputOverrideUpdateRate.boolValue = EditorGUILayout.ToggleLeft( "Override Update Rate <color=#777>(Not Recommended)</color>", nativeInputOverrideUpdateRate.boolValue, EditorUtility.labelStyle );
				nativeInputUpdateRate.intValue = nativeInputOverrideUpdateRate.boolValue ? Mathf.Max( nativeInputUpdateRate.intValue, EditorGUILayout.IntField( "Update Rate (Hz)", nativeInputUpdateRate.intValue ), 0 ) : 0;

				EditorUtility.EndGroup();
			}

			GUILayout.Space( 10.0f );

			serializedObject.ApplyModifiedProperties();
		}
	}
}
#endif
