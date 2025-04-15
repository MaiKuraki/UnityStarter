namespace InControl
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using UnityEngine;


	[ExecuteInEditMode]
	public class TouchManager : SingletonMonoBehavior<TouchManager>
	{
		public enum GizmoShowOption
		{
			Never,
			WhenSelected,
			UnlessPlaying,
			Always
		}

		[Space( 10 )]
		public Camera touchCamera;

		public GizmoShowOption controlsShowGizmos = GizmoShowOption.Always;

		[HideInInspector]
		public bool enableControlsOnTouch = false;

		[SerializeField, HideInInspector]
		bool _controlsEnabled = true;

		// Defaults to UI layer.
		[HideInInspector]
		public int controlsLayer = 5;

		public static event Action OnSetup;

		InputDevice device;

		Vector3 viewSize;
		Vector2 screenSize;
		Vector2 halfScreenSize;
		float percentToWorld;
		float halfPercentToWorld;
		float pixelToWorld;
		float halfPixelToWorld;

		TouchControl[] touchControls;

		TouchPool cachedTouches;
		List<Touch> activeTouches;
		ReadOnlyCollection<Touch> readOnlyActiveTouches;

		bool isReady;

		#pragma warning disable 414
		readonly Touch[] mouseTouches = new Touch[3];
		#pragma warning restore 414


		protected TouchManager() {}


		void OnEnable()
		{
			var manager = GetComponent<InControlManager>();
			if (manager == null)
			{
				Logger.LogError( "Touch Manager component can only be added to the InControl Manager object." );
				DestroyImmediate( this );
				return;
			}

			if (EnforceSingleton)
			{
				return;
			}

			#if UNITY_EDITOR
			if (touchCamera == null)
			{
				foreach (var component in manager.gameObject.GetComponentsInChildren<Camera>())
				{
					DestroyImmediate( component.gameObject );
				}

				var cameraGameObject = new GameObject( "Touch Camera" );
				cameraGameObject.transform.parent = manager.gameObject.transform;
				cameraGameObject.transform.SetAsFirstSibling();

				touchCamera = cameraGameObject.AddComponent<Camera>();
				touchCamera.transform.position = new Vector3( 0.0f, 0.0f, -10.0f );
				touchCamera.clearFlags = CameraClearFlags.Nothing;
				touchCamera.cullingMask = 1 << LayerMask.NameToLayer( "UI" );
				touchCamera.orthographic = true;
				touchCamera.orthographicSize = 5.0f;
				touchCamera.nearClipPlane = 0.3f;
				touchCamera.farClipPlane = 1000.0f;
				touchCamera.rect = new Rect( 0.0f, 0.0f, 1.0f, 1.0f );
				touchCamera.depth = 100;
			}
			#endif

			touchControls = GetComponentsInChildren<TouchControl>( true );

			if (Application.isPlaying)
			{
				InputManager.OnSetup += Setup;
				InputManager.OnUpdateDevices += UpdateDevice;
				InputManager.OnCommitDevices += CommitDevice;
			}
		}


		void OnDisable()
		{
			if (Application.isPlaying)
			{
				InputManager.OnSetup -= Setup;
				InputManager.OnUpdateDevices -= UpdateDevice;
				InputManager.OnCommitDevices -= CommitDevice;
			}

			Reset();
		}


		void Setup()
		{
			UpdateScreenSize( GetCurrentScreenSize() );

			CreateDevice();
			CreateTouches();

			if (OnSetup != null)
			{
				OnSetup.Invoke();
				OnSetup = null;
			}
		}


		void Reset()
		{
			device = null;

			for (var i = 0; i < 3; i++)
			{
				mouseTouches[i] = null;
			}

			cachedTouches = null;
			activeTouches = null;
			readOnlyActiveTouches = null;
			touchControls = null;
			OnSetup = null;
		}


		IEnumerator UpdateScreenSizeAtEndOfFrame()
		{
			yield return new WaitForEndOfFrame();
			UpdateScreenSize( GetCurrentScreenSize() );
			yield return null;
		}


		void Update()
		{
			var currentScreenSize = GetCurrentScreenSize();

			if (!isReady)
			{
				// This little hack is necessary because right after Unity starts up,
				// cameras don't seem to have a correct projection matrix until after
				// their first update or around that time. So we basically need to
				// wait until the end of the first frame before everything is quite ready.
				StartCoroutine( UpdateScreenSizeAtEndOfFrame() );
				UpdateScreenSize( currentScreenSize );
				isReady = true;
				return;
			}

			if (screenSize != currentScreenSize)
			{
				UpdateScreenSize( currentScreenSize );
			}

			if (OnSetup != null)
			{
				OnSetup.Invoke();
				OnSetup = null;
			}
		}


		#if UNITY_EDITOR
		void OnGUI()
		{
			var currentScreenSize = GetCurrentScreenSize();
			if (screenSize != currentScreenSize)
			{
				UpdateScreenSize( currentScreenSize );
			}
		}
		#endif


		void CreateDevice()
		{
			device = new TouchInputDevice();

			device.AddControl( InputControlType.LeftStickLeft, "LeftStickLeft" );
			device.AddControl( InputControlType.LeftStickRight, "LeftStickRight" );
			device.AddControl( InputControlType.LeftStickUp, "LeftStickUp" );
			device.AddControl( InputControlType.LeftStickDown, "LeftStickDown" );

			device.AddControl( InputControlType.RightStickLeft, "RightStickLeft" );
			device.AddControl( InputControlType.RightStickRight, "RightStickRight" );
			device.AddControl( InputControlType.RightStickUp, "RightStickUp" );
			device.AddControl( InputControlType.RightStickDown, "RightStickDown" );

			device.AddControl( InputControlType.DPadUp, "DPadUp" );
			device.AddControl( InputControlType.DPadDown, "DPadDown" );
			device.AddControl( InputControlType.DPadLeft, "DPadLeft" );
			device.AddControl( InputControlType.DPadRight, "DPadRight" );

			device.AddControl( InputControlType.LeftTrigger, "LeftTrigger" );
			device.AddControl( InputControlType.RightTrigger, "RightTrigger" );

			device.AddControl( InputControlType.LeftBumper, "LeftBumper" );
			device.AddControl( InputControlType.RightBumper, "RightBumper" );

			for (var control = InputControlType.Action1; control <= InputControlType.Action12; control++)
			{
				device.AddControl( control, control.ToString() );
			}

			device.AddControl( InputControlType.Menu, "Menu" );

			for (var control = InputControlType.Button0; control <= InputControlType.Button19; control++)
			{
				device.AddControl( control, control.ToString() );
			}

			InputManager.AttachDevice( device );
		}


		void UpdateDevice( ulong updateTick, float deltaTime )
		{
			UpdateTouches( updateTick, deltaTime );
			SubmitControlStates( updateTick, deltaTime );
		}


		void CommitDevice( ulong updateTick, float deltaTime )
		{
			CommitControlStates( updateTick, deltaTime );
		}


		void SubmitControlStates( ulong updateTick, float deltaTime )
		{
			var touchControlCount = touchControls.Length;
			for (var i = 0; i < touchControlCount; i++)
			{
				var touchControl = touchControls[i];
				if (touchControl.enabled && touchControl.gameObject.activeInHierarchy)
				{
					touchControl.SubmitControlState( updateTick, deltaTime );
				}
			}
		}


		void CommitControlStates( ulong updateTick, float deltaTime )
		{
			var touchControlCount = touchControls.Length;
			for (var i = 0; i < touchControlCount; i++)
			{
				var touchControl = touchControls[i];
				if (touchControl.enabled && touchControl.gameObject.activeInHierarchy)
				{
					touchControl.CommitControlState( updateTick, deltaTime );
				}
			}
		}


		void UpdateScreenSize( Vector2 currentScreenSize )
		{
			// Somehow the camera's projection matrix doesn't always update correctly on
			// resolution changes. This seems to cause it to recalculate properly.
			touchCamera.rect = new Rect( 0, 0, 0.99f, 1 );
			// ReSharper disable once Unity.InefficientPropertyAccess
			touchCamera.rect = new Rect( 0, 0, 1, 1 );

			screenSize = currentScreenSize;
			halfScreenSize = screenSize / 2.0f;

			viewSize = ConvertViewToWorldPoint( Vector2.one ) * 0.02f;
			percentToWorld = Mathf.Min( viewSize.x, viewSize.y );
			halfPercentToWorld = percentToWorld / 2.0f;

			if (touchCamera != null)
			{
				halfPixelToWorld = touchCamera.orthographicSize / screenSize.y;
				pixelToWorld = halfPixelToWorld * 2.0f;
			}

			if (touchControls != null)
			{
				var touchControlCount = touchControls.Length;
				for (var i = 0; i < touchControlCount; i++)
				{
					touchControls[i].ConfigureControl();
				}
			}
		}


		void CreateTouches()
		{
			cachedTouches = new TouchPool();

			for (var i = 0; i < 3; i++)
			{
				mouseTouches[i] = new Touch();
				mouseTouches[i].fingerId = Touch.FingerID_Mouse;
			}

			activeTouches = new List<Touch>( 32 );
			readOnlyActiveTouches = new ReadOnlyCollection<Touch>( activeTouches );
		}


		void UpdateTouches( ulong updateTick, float deltaTime )
		{
			activeTouches.Clear();
			cachedTouches.FreeEndedTouches();

			#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBPLAYER || UNITY_WEBGL || UNITY_WSA
			for (var i = 0; i < 3; i++)
			{
				if (mouseTouches[i].SetWithMouseData( i, updateTick, deltaTime ))
				{
					activeTouches.Add( mouseTouches[i] );
				}
			}
			#endif

			for (var i = 0; i < Input.touchCount; i++)
			{
				var unityTouch = Input.GetTouch( i );
				var cacheTouch = cachedTouches.FindOrCreateTouch( unityTouch.fingerId );
				cacheTouch.SetWithTouchData( unityTouch, updateTick, deltaTime );
				activeTouches.Add( cacheTouch );
			}

			// Find any touches that Unity may have "forgotten" to end properly.
			var touchCount = cachedTouches.Touches.Count;
			for (var i = 0; i < touchCount; i++)
			{
				var touch = cachedTouches.Touches[i];
				if (touch.phase != TouchPhase.Ended && touch.updateTick != updateTick)
				{
					touch.phase = TouchPhase.Ended;
					activeTouches.Add( touch );
				}
			}

			InvokeTouchEvents();
		}


		void SendTouchBegan( Touch touch )
		{
			var touchControlCount = touchControls.Length;
			for (var i = 0; i < touchControlCount; i++)
			{
				var touchControl = touchControls[i];
				if (touchControl.enabled && touchControl.gameObject.activeInHierarchy)
				{
					touchControl.TouchBegan( touch );
				}
			}
		}


		void SendTouchMoved( Touch touch )
		{
			var touchControlCount = touchControls.Length;
			for (var i = 0; i < touchControlCount; i++)
			{
				var touchControl = touchControls[i];
				if (touchControl.enabled && touchControl.gameObject.activeInHierarchy)
				{
					touchControl.TouchMoved( touch );
				}
			}
		}


		void SendTouchEnded( Touch touch )
		{
			var touchControlCount = touchControls.Length;
			for (var i = 0; i < touchControlCount; i++)
			{
				var touchControl = touchControls[i];
				if (touchControl.enabled && touchControl.gameObject.activeInHierarchy)
				{
					touchControl.TouchEnded( touch );
				}
			}
		}


		void InvokeTouchEvents()
		{
			var touchCount = activeTouches.Count;

			if (enableControlsOnTouch)
			{
				if (touchCount > 0 && !controlsEnabled)
				{
					Device.RequestActivation();
					controlsEnabled = true;
				}
			}

			for (var i = 0; i < touchCount; i++)
			{
				var touch = activeTouches[i];
				switch (touch.phase)
				{
					case TouchPhase.Began:
						SendTouchBegan( touch );
						break;

					case TouchPhase.Moved:
						SendTouchMoved( touch );
						break;

					case TouchPhase.Ended:
						SendTouchEnded( touch );
						break;

					case TouchPhase.Canceled:
						SendTouchEnded( touch );
						break;

					case TouchPhase.Stationary:
						break;

					default:
						throw new ArgumentOutOfRangeException();
				}
			}
		}


		bool TouchCameraIsValid()
		{
			if (touchCamera == null)
			{
				return false;
			}

			if (Utility.IsZero( touchCamera.orthographicSize ))
			{
				return false;
			}

			if (Utility.IsZero( touchCamera.rect.width ) && Utility.IsZero( touchCamera.rect.height ))
			{
				return false;
			}

			if (Utility.IsZero( touchCamera.pixelRect.width ) && Utility.IsZero( touchCamera.pixelRect.height ))
			{
				return false;
			}

			return true;
		}


		Vector3 ConvertScreenToWorldPoint( Vector2 point )
		{
			if (TouchCameraIsValid())
			{
				return touchCamera.ScreenToWorldPoint( new Vector3( point.x, point.y, -touchCamera.transform.position.z ) );
			}

			return Vector3.zero;
		}


		Vector3 ConvertViewToWorldPoint( Vector2 point )
		{
			if (TouchCameraIsValid())
			{
				return touchCamera.ViewportToWorldPoint( new Vector3( point.x, point.y, -touchCamera.transform.position.z ) );
			}

			return Vector3.zero;
		}


		Vector3 ConvertScreenToViewPoint( Vector2 point )
		{
			if (TouchCameraIsValid())
			{
				return touchCamera.ScreenToViewportPoint( new Vector3( point.x, point.y, -touchCamera.transform.position.z ) );
			}

			return Vector3.zero;
		}


		Vector2 GetCurrentScreenSize()
		{
			if (TouchCameraIsValid())
			{
				return new Vector2( touchCamera.pixelWidth, touchCamera.pixelHeight );
			}

			return new Vector2( Screen.width, Screen.height );
		}


		public bool controlsEnabled
		{
			get { return _controlsEnabled; }

			set
			{
				if (_controlsEnabled != value)
				{
					var touchControlCount = touchControls.Length;
					for (var i = 0; i < touchControlCount; i++)
					{
						touchControls[i].enabled = value;
					}

					_controlsEnabled = value;
				}
			}
		}


		#region Static interface.

		public static ReadOnlyCollection<Touch> Touches
		{
			get { return Instance.readOnlyActiveTouches; }
		}


		public static int TouchCount
		{
			get { return Instance.activeTouches.Count; }
		}


		public static Touch GetTouch( int touchIndex )
		{
			return Instance.activeTouches[touchIndex];
		}


		public static Touch GetTouchByFingerId( int fingerId )
		{
			return Instance.cachedTouches.FindTouch( fingerId );
		}


		public static Vector3 ScreenToWorldPoint( Vector2 point )
		{
			return Instance.ConvertScreenToWorldPoint( point );
		}


		public static Vector3 ViewToWorldPoint( Vector2 point )
		{
			return Instance.ConvertViewToWorldPoint( point );
		}


		public static Vector3 ScreenToViewPoint( Vector2 point )
		{
			return Instance.ConvertScreenToViewPoint( point );
		}


		public static float ConvertToWorld( float value, TouchUnitType unitType )
		{
			return value * (unitType == TouchUnitType.Pixels ? PixelToWorld : PercentToWorld);
		}


		public static Rect PercentToWorldRect( Rect rect )
		{
			return new Rect(
				(rect.xMin - 50.0f) * ViewSize.x,
				(rect.yMin - 50.0f) * ViewSize.y,
				rect.width * ViewSize.x,
				rect.height * ViewSize.y
			);
		}


		public static Rect PixelToWorldRect( Rect rect )
		{
			return new Rect(
				Mathf.Round( rect.xMin - HalfScreenSize.x ) * PixelToWorld,
				Mathf.Round( rect.yMin - HalfScreenSize.y ) * PixelToWorld,
				Mathf.Round( rect.width ) * PixelToWorld,
				Mathf.Round( rect.height ) * PixelToWorld
			);
		}


		public static Rect ConvertToWorld( Rect rect, TouchUnitType unitType )
		{
			return unitType == TouchUnitType.Pixels ? PixelToWorldRect( rect ) : PercentToWorldRect( rect );
		}


		public static Camera Camera
		{
			get { return Instance.touchCamera; }
		}


		public static InputDevice Device
		{
			get { return Instance.device; }
		}


		public static Vector3 ViewSize
		{
			get { return Instance.viewSize; }
		}


		public static float PercentToWorld
		{
			get { return Instance.percentToWorld; }
		}


		public static float HalfPercentToWorld
		{
			get { return Instance.halfPercentToWorld; }
		}


		public static float PixelToWorld
		{
			get { return Instance.pixelToWorld; }
		}


		public static float HalfPixelToWorld
		{
			get { return Instance.halfPixelToWorld; }
		}


		public static Vector2 ScreenSize
		{
			get { return Instance.screenSize; }
		}


		public static Vector2 HalfScreenSize
		{
			get { return Instance.halfScreenSize; }
		}


		public static GizmoShowOption ControlsShowGizmos
		{
			get { return Instance.controlsShowGizmos; }
		}


		public static bool ControlsEnabled
		{
			get { return Instance.controlsEnabled; }

			set { Instance.controlsEnabled = value; }
		}

		#endregion


		public static implicit operator bool( TouchManager instance )
		{
			return instance != null;
		}
	}
}
