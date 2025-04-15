// ReSharper disable InconsistentNaming
// ReSharper disable NotAccessedField.Global
// ReSharper disable UnusedMember.Global
namespace InControl
{
	using System;
	using UnityEngine;


	public class Touch
	{
		public const int FingerID_None = -1;
		public const int FingerID_Mouse = -2;

		public int fingerId;
		public int mouseButton;

		public TouchPhase phase;
		public int tapCount;

		public Vector2 position;
		public Vector2 startPosition;
		public Vector2 deltaPosition;
		public Vector2 lastPosition;

		public float deltaTime;
		public ulong updateTick;

		public TouchType type;

		public float altitudeAngle;
		public float azimuthAngle;
		public float maximumPossiblePressure;
		public float pressure;
		public float radius;
		public float radiusVariance;


		internal Touch()
		{
			fingerId = FingerID_None;
			phase = TouchPhase.Ended;
		}


		internal void Reset()
		{
			fingerId = FingerID_None;
			mouseButton = 0;
			phase = TouchPhase.Ended;
			tapCount = 0;
			position = Vector2.zero;
			startPosition = Vector2.zero;
			deltaPosition = Vector2.zero;
			lastPosition = Vector2.zero;
			deltaTime = 0.0f;
			updateTick = 0;
			type = 0;
			altitudeAngle = 0.0f;
			azimuthAngle = 0.0f;
			maximumPossiblePressure = 1.0f;
			pressure = 0.0f;
			radius = 0.0f;
			radiusVariance = 0.0f;
		}


		[Obsolete( "normalizedPressure is deprecated, please use NormalizedPressure instead." )]
		public float normalizedPressure
		{
			get
			{
				// Return at least a tiny value otherwise pressure can be zero.
				return Mathf.Clamp( pressure / maximumPossiblePressure, 0.001f, 1.0f );
			}
		}


		public float NormalizedPressure
		{
			get
			{
				// Return at least a tiny value otherwise pressure can be zero.
				return Mathf.Clamp( pressure / maximumPossiblePressure, 0.001f, 1.0f );
			}
		}


		public bool IsMouse
		{
			get { return type == TouchType.Mouse; }
		}


		internal void SetWithTouchData( UnityEngine.Touch touch, ulong updateTick, float deltaTime )
		{
			phase = touch.phase;
			tapCount = touch.tapCount;
			mouseButton = 0;

			#if UNITY_4_3 || UNITY_4_5 || UNITY_4_6 || UNITY_4_7 || UNITY_5_0 || UNITY_5_1 || UNITY_5_2
			type = TouchType.Direct;
			altitudeAngle = Mathf.PI / 2.0f;
			azimuthAngle = Mathf.PI / 2.0f;
			maximumPossiblePressure = 1.0f;
			pressure = 1.0f;
			radius = 1.0f;
			radiusVariance = 0.0f;
			#else
			altitudeAngle = touch.altitudeAngle;
			azimuthAngle = touch.azimuthAngle;
			maximumPossiblePressure = touch.maximumPossiblePressure;
			pressure = touch.pressure;
			radius = touch.radius;
			radiusVariance = touch.radiusVariance;
			#endif

			var touchPosition = touch.position;
			touchPosition.x = Mathf.Clamp( touchPosition.x, 0.0f, Screen.width );
			touchPosition.y = Mathf.Clamp( touchPosition.y, 0.0f, Screen.height );

			if (phase == TouchPhase.Began)
			{
				startPosition = touchPosition;
				deltaPosition = Vector2.zero;
				lastPosition = touchPosition;
				position = touchPosition;
			}
			else
			{
				if (phase == TouchPhase.Stationary)
				{
					phase = TouchPhase.Moved;
				}

				deltaPosition = touchPosition - lastPosition;
				lastPosition = position;
				position = touchPosition;
			}

			this.deltaTime = deltaTime;
			this.updateTick = updateTick;
		}


		internal bool SetWithMouseData( int button, ulong updateTick, float deltaTime )
		{
			// Unity Remote and possibly some platforms like WP8 simulates mouse with
			// touches so detect that situation and reject the mouse.
			if (Input.touchCount > 0)
			{
				return false;
			}

			if (button < 0 || button > 2)
			{
				return false;
			}

			var inputMousePosition = InputManager.MouseProvider.GetPosition();
			var mousePosition = new Vector2( Mathf.Round( inputMousePosition.x ), Mathf.Round( inputMousePosition.y ) );

			var mouseControl = Mouse.LeftButton + button;

			if (InputManager.MouseProvider.GetButtonWasPressed( mouseControl ))
			{
				phase = TouchPhase.Began;
				pressure = 1.0f;
				maximumPossiblePressure = 1.0f;

				tapCount = 1;
				type = TouchType.Mouse;
				mouseButton = button;

				startPosition = mousePosition;
				deltaPosition = Vector2.zero;
				lastPosition = mousePosition;
				position = mousePosition;

				this.deltaTime = deltaTime;
				this.updateTick = updateTick;

				return true;
			}

			if (InputManager.MouseProvider.GetButtonWasReleased( mouseControl ))
			{
				phase = TouchPhase.Ended;
				pressure = 0.0f;
				maximumPossiblePressure = 1.0f;

				tapCount = 1;
				type = TouchType.Mouse;
				mouseButton = button;

				deltaPosition = mousePosition - lastPosition;
				lastPosition = position;
				position = mousePosition;

				this.deltaTime = deltaTime;
				this.updateTick = updateTick;

				return true;
			}

			if (InputManager.MouseProvider.GetButtonIsPressed( mouseControl ))
			{
				phase = TouchPhase.Moved;
				pressure = 1.0f;
				maximumPossiblePressure = 1.0f;

				tapCount = 1;
				type = TouchType.Mouse;
				mouseButton = button;

				deltaPosition = mousePosition - lastPosition;
				lastPosition = position;
				position = mousePosition;

				this.deltaTime = deltaTime;
				this.updateTick = updateTick;

				return true;
			}

			return false;
		}
	}
}
