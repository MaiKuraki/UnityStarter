namespace InControl
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;
	#if NETFX_CORE
	using System.Reflection;
	#endif
	#if UNITY_5_4_OR_NEWER
	using UnityEngine.SceneManagement;


	#endif


	public enum InControlUpdateMode
	{
		Default,
		FixedUpdate,
		Manual
	}


	public class InControlManager : SingletonMonoBehavior<InControlManager>
	{
		public bool logDebugInfo = true;
		public bool invertYAxis = false;

		// ReSharper disable once NotAccessedField.Local
		#pragma warning disable 414
		[SerializeField]
		bool useFixedUpdate = false; // This is now deprecated and replaced by updateMode
		#pragma warning restore 414

		public bool dontDestroyOnLoad = true;
		public bool suspendInBackground = false;
		public InControlUpdateMode updateMode;

		public bool enableICade = false;

		public bool enableXInput = false;
		public bool xInputOverrideUpdateRate = false;
		public int xInputUpdateRate = 0;
		public bool xInputOverrideBufferSize = false;
		public int xInputBufferSize = 0;

		public bool enableNativeInput = true;
		public bool nativeInputEnableXInput = true;
		public bool nativeInputEnableMFi = true;
		public bool nativeInputPreventSleep = false;
		public bool nativeInputOverrideUpdateRate = false;
		public int nativeInputUpdateRate = 0;

		bool applicationHasQuit = false;


		void OnEnable()
		{
			if (EnforceSingleton)
			{
				return;
			}

			InputManager.InvertYAxis = invertYAxis;
			InputManager.SuspendInBackground = suspendInBackground;
			InputManager.EnableICade = enableICade;

			InputManager.EnableXInput = enableXInput;
			InputManager.XInputUpdateRate = (uint) Mathf.Max( xInputUpdateRate, 0 );
			InputManager.XInputBufferSize = (uint) Mathf.Max( xInputBufferSize, 0 );

			InputManager.EnableNativeInput = enableNativeInput;
			InputManager.NativeInputEnableXInput = nativeInputEnableXInput;
			InputManager.NativeInputEnableMFi = nativeInputEnableMFi;
			InputManager.NativeInputUpdateRate = (uint) Mathf.Max( nativeInputUpdateRate, 0 );
			InputManager.NativeInputPreventSleep = nativeInputPreventSleep;

			if (logDebugInfo)
			{
				Logger.OnLogMessage -= LogMessage;
				Logger.OnLogMessage += LogMessage;
			}

			InputManager.SetupInternal();

			#if UNITY_5_4_OR_NEWER
			SceneManager.sceneLoaded -= OnSceneWasLoaded;
			SceneManager.sceneLoaded += OnSceneWasLoaded;
			#endif

			if (dontDestroyOnLoad)
			{
				DontDestroyOnLoad( this );
			}
		}


		void OnDisable()
		{
			if (IsNotTheSingleton) return;
			#if UNITY_5_4_OR_NEWER
			SceneManager.sceneLoaded -= OnSceneWasLoaded;
			#endif
			InputManager.ResetInternal();
		}


		void Update()
		{
			if (IsNotTheSingleton) return;
			if (applicationHasQuit) return;
			if (updateMode == InControlUpdateMode.Default || (updateMode == InControlUpdateMode.FixedUpdate && Utility.IsZero( Time.timeScale )))
			{
				InputManager.UpdateInternal();
			}
		}


		void FixedUpdate()
		{
			if (IsNotTheSingleton) return;
			if (applicationHasQuit) return;
			if (updateMode == InControlUpdateMode.FixedUpdate)
			{
				InputManager.UpdateInternal();
			}
		}


		void OnApplicationFocus( bool focusState )
		{
			if (IsNotTheSingleton) return;
			if (applicationHasQuit) return;
			InputManager.OnApplicationFocus( focusState );
		}


		void OnApplicationPause( bool pauseState )
		{
			if (IsNotTheSingleton) return;
			if (applicationHasQuit) return;
			InputManager.OnApplicationPause( pauseState );
		}


		void OnApplicationQuit()
		{
			if (IsNotTheSingleton) return;
			if (applicationHasQuit) return;
			InputManager.OnApplicationQuit();
			applicationHasQuit = true;
		}


		#if UNITY_5_4_OR_NEWER
		void OnSceneWasLoaded( Scene scene, LoadSceneMode loadSceneMode )
		{
			if (IsNotTheSingleton) return;
			if (applicationHasQuit) return;
			if (loadSceneMode == LoadSceneMode.Single)
			{
				InputManager.OnLevelWasLoaded();
			}
		}
		#else
		void OnLevelWasLoaded( int level )
		{
			if (IsNotTheSingleton) return;
			if (applicationHasQuit) return;
			InputManager.OnLevelWasLoaded();
		}
		#endif


		static void LogMessage( LogMessage logMessage )
		{
			switch (logMessage.Type)
			{
				case LogMessageType.Info:
					Debug.Log( logMessage.Text );
					break;
				case LogMessageType.Warning:
					Debug.LogWarning( logMessage.Text );
					break;
				case LogMessageType.Error:
					Debug.LogError( logMessage.Text );
					break;
				default:
					break;
			}
		}
	}
}
