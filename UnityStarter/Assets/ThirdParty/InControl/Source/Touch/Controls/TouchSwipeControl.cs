namespace InControl
{
	using UnityEngine;


	public class TouchSwipeControl : TouchControl
	{
		[Header( "Position" )]
		[SerializeField]
		TouchUnitType areaUnitType = TouchUnitType.Percent;

		[SerializeField]
		Rect activeArea = new Rect( 25.0f, 25.0f, 50.0f, 50.0f );


		[Header( "Options" )]
		[Range( 0, 1 )]
		public float sensitivity = 0.1f;

		public bool oneSwipePerTouch = false;


		[Header( "Analog Target" )]
		public AnalogTarget target = AnalogTarget.None;

		public SnapAngles snapAngles = SnapAngles.None;


		[Header( "Button Targets" )]
		public ButtonTarget upTarget = ButtonTarget.None;

		public ButtonTarget downTarget = ButtonTarget.None;
		public ButtonTarget leftTarget = ButtonTarget.None;
		public ButtonTarget rightTarget = ButtonTarget.None;
		public ButtonTarget tapTarget = ButtonTarget.None;


		Rect worldActiveArea;
		Vector3 currentVector;
		bool currentVectorIsSet;
		Vector3 beganPosition;
		Vector3 lastPosition;
		Touch currentTouch;
		bool fireButtonTarget;
		ButtonTarget nextButtonTarget;
		ButtonTarget lastButtonTarget;
		bool dirty;


		public override void CreateControl() {}


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
			//Gizmos.color = Color.red;
			//Gizmos.DrawLine( Vector3.zero, currentVector * 2.0f );
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
			var value = SnapTo( currentVector, snapAngles );
			SubmitAnalogValue( target, value, 0.0f, 1.0f, updateTick, deltaTime );

			SubmitButtonState( upTarget, fireButtonTarget && nextButtonTarget == upTarget, updateTick, deltaTime );
			SubmitButtonState( downTarget, fireButtonTarget && nextButtonTarget == downTarget, updateTick, deltaTime );
			SubmitButtonState( leftTarget, fireButtonTarget && nextButtonTarget == leftTarget, updateTick, deltaTime );
			SubmitButtonState( rightTarget, fireButtonTarget && nextButtonTarget == rightTarget, updateTick, deltaTime );
			SubmitButtonState( tapTarget, fireButtonTarget && nextButtonTarget == tapTarget, updateTick, deltaTime );

			if (fireButtonTarget && nextButtonTarget != ButtonTarget.None)
			{
				fireButtonTarget = !oneSwipePerTouch;
				lastButtonTarget = nextButtonTarget;
				nextButtonTarget = ButtonTarget.None;
			}
		}


		public override void CommitControlState( ulong updateTick, float deltaTime )
		{
			CommitAnalog( target );
			CommitButton( upTarget );
			CommitButton( downTarget );
			CommitButton( leftTarget );
			CommitButton( rightTarget );
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
				lastPosition = beganPosition;
				currentTouch = touch;
				currentVector = Vector2.zero;
				currentVectorIsSet = false;

				fireButtonTarget = true;
				nextButtonTarget = ButtonTarget.None;
				lastButtonTarget = ButtonTarget.None;
			}
		}


		public override void TouchMoved( Touch touch )
		{
			if (currentTouch != touch)
			{
				return;
			}

			var movedPosition = TouchManager.ScreenToWorldPoint( touch.position );
			var delta = movedPosition - lastPosition;
			if (delta.magnitude >= sensitivity)
			{
				lastPosition = movedPosition;

				if (!(oneSwipePerTouch && currentVectorIsSet))
				{
					currentVector = delta.normalized;
					currentVectorIsSet = true;
				}

				if (fireButtonTarget)
				{
					var thisButtonTarget = GetButtonTargetForVector( currentVector );
					if (thisButtonTarget != lastButtonTarget)
					{
						nextButtonTarget = thisButtonTarget;
					}
				}
			}
		}


		public override void TouchEnded( Touch touch )
		{
			if (currentTouch != touch)
			{
				return;
			}

			currentTouch = null;
			currentVector = Vector2.zero;
			currentVectorIsSet = false;

			var touchPosition = TouchManager.ScreenToWorldPoint( touch.position );
			var delta = beganPosition - touchPosition;
			if (delta.magnitude < sensitivity)
			{
				fireButtonTarget = true;
				nextButtonTarget = tapTarget;
				lastButtonTarget = ButtonTarget.None;
				return;
			}

			fireButtonTarget = false;
			nextButtonTarget = ButtonTarget.None;
			lastButtonTarget = ButtonTarget.None;
		}


		ButtonTarget GetButtonTargetForVector( Vector2 vector )
		{
			Vector2 snappedVector = SnapTo( vector, SnapAngles.Four );

			if (snappedVector == Vector2.up)
			{
				return upTarget;
			}

			if (snappedVector == Vector2.right)
			{
				return rightTarget;
			}

			if (snappedVector == -Vector2.up)
			{
				return downTarget;
			}

			if (snappedVector == -Vector2.right)
			{
				return leftTarget;
			}

			return ButtonTarget.None;
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
