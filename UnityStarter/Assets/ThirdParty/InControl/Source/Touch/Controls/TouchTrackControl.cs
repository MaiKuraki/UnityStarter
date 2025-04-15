namespace InControl
{
	using UnityEngine;


	public class TouchTrackControl : TouchControl
	{
		[Header( "Dimensions" )]
		[SerializeField]
		TouchUnitType areaUnitType = TouchUnitType.Percent;

		[SerializeField]
		Rect activeArea = new Rect( 25.0f, 25.0f, 50.0f, 50.0f );


		[Header( "Analog Target" )]
		public AnalogTarget target = AnalogTarget.LeftStick;

		public float scale = 1.0f;

		[Header( "Button Target" )]
		public ButtonTarget tapTarget = ButtonTarget.None;

		public float maxTapDuration = 0.5f;
		public float maxTapMovement = 1.0f;


		Rect worldActiveArea;
		Vector3 lastPosition;
		Vector3 thisPosition;
		Touch currentTouch;
		bool dirty;

		bool fireButtonTarget;
		float beganTime;
		Vector3 beganPosition;


		public override void CreateControl()
		{
			ConfigureControl();
		}


		public override void DestroyControl()
		{
			if (currentTouch != null)
			{
				TouchEnded( currentTouch );
				currentTouch = null;
			}
		}


		public override void ConfigureControl()
		{
			worldActiveArea = TouchManager.ConvertToWorld( activeArea, areaUnitType );
		}


		public override void DrawGizmos()
		{
			Utility.DrawRectGizmo( worldActiveArea, Color.yellow );
		}


		void OnValidate()
		{
			if (maxTapDuration < 0.0f)
			{
				maxTapDuration = 0.0f;
			}
		}


		void Update()
		{
			if (dirty)
			{
				ConfigureControl();
				dirty = false;
			}
		}


		public override void SubmitControlState( ulong updateTick, float deltaTime )
		{
			var delta = thisPosition - lastPosition;
			SubmitRawAnalogValue( target, delta * scale, updateTick, deltaTime );
			lastPosition = thisPosition;

			SubmitButtonState( tapTarget, fireButtonTarget, updateTick, deltaTime );
			fireButtonTarget = false;
		}


		public override void CommitControlState( ulong updateTick, float deltaTime )
		{
			CommitAnalog( target );
			CommitButton( tapTarget );
		}


		public override void TouchBegan( Touch touch )
		{
			if (currentTouch != null)
			{
				return;
			}

			beganPosition = TouchManager.ScreenToWorldPoint( touch.position );
			if (worldActiveArea.Contains( beganPosition ))
			{
				thisPosition = TouchManager.ScreenToViewPoint( touch.position * 100.0f );
				lastPosition = thisPosition;
				currentTouch = touch;
				beganTime = Time.realtimeSinceStartup;
			}
		}


		public override void TouchMoved( Touch touch )
		{
			if (currentTouch != touch)
			{
				return;
			}

			thisPosition = TouchManager.ScreenToViewPoint( touch.position * 100.0f );
		}


		public override void TouchEnded( Touch touch )
		{
			if (currentTouch != touch)
			{
				return;
			}

			var endedPosition = TouchManager.ScreenToWorldPoint( touch.position );
			var deltaPosition = endedPosition - beganPosition;

			var deltaTime = Time.realtimeSinceStartup - beganTime;

			if (deltaPosition.magnitude <= maxTapMovement && deltaTime <= maxTapDuration)
			{
				if (tapTarget != ButtonTarget.None)
				{
					fireButtonTarget = true;
				}
			}

			thisPosition = Vector3.zero;
			lastPosition = Vector3.zero;
			currentTouch = null;
		}


		public Rect ActiveArea
		{
			get
			{
				return activeArea;
			}

			set
			{
				if (activeArea != value)
				{
					activeArea = value;
					dirty = true;
				}
			}
		}


		public TouchUnitType AreaUnitType
		{
			get
			{
				return areaUnitType;
			}

			set
			{
				if (areaUnitType != value)
				{
					areaUnitType = value;
					dirty = true;
				}
			}
		}
	}
}
