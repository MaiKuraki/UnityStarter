namespace InControl
{
	using UnityEngine;


	public class TouchButtonControl : TouchControl
	{
		[Header( "Position" )]
		[SerializeField]
		TouchControlAnchor anchor = TouchControlAnchor.BottomRight;

		[SerializeField]
		TouchUnitType offsetUnitType = TouchUnitType.Percent;

		[SerializeField]
		Vector2 offset = new Vector2( -10.0f, 10.0f );

		[SerializeField]
		bool lockAspectRatio = true;


		[Header( "Options" )]
		public ButtonTarget target = ButtonTarget.Action1;

		public bool allowSlideToggle = true;
		public bool toggleOnLeave = false;

		#if !(UNITY_4_3 || UNITY_4_5 || UNITY_4_6 || UNITY_4_7 || UNITY_5_0 || UNITY_5_1 || UNITY_5_2)
		public bool pressureSensitive = false;
		#endif


		[Header( "Sprites" )]
		public TouchSprite button = new TouchSprite( 15.0f );


		bool buttonState;
		Touch currentTouch;
		bool dirty;


		public override void CreateControl()
		{
			button.Create( "Button", transform, 1000 );
		}


		public override void DestroyControl()
		{
			button.Delete();

			if (currentTouch != null)
			{
				TouchEnded( currentTouch );
				currentTouch = null;
			}
		}


		public override void ConfigureControl()
		{
			transform.position = OffsetToWorldPosition( anchor, offset, offsetUnitType, lockAspectRatio );
			button.Update( true );
		}


		public override void DrawGizmos()
		{
			button.DrawGizmos( ButtonPosition, Color.yellow );
		}


		void Update()
		{
			if (dirty)
			{
				ConfigureControl();
				dirty = false;
			}
			else
			{
				button.Update();
			}
		}


		public override void SubmitControlState( ulong updateTick, float deltaTime )
		{
			#if !(UNITY_4_3 || UNITY_4_5 || UNITY_4_6 || UNITY_4_7 || UNITY_5_0 || UNITY_5_1 || UNITY_5_2)
			if (pressureSensitive)
			{
				var buttonValue = 0.0f;
				if (currentTouch == null)
				{
					if (allowSlideToggle)
					{
						var touchCount = TouchManager.TouchCount;
						for (var i = 0; i < touchCount; i++)
						{
							var touch = TouchManager.GetTouch( i );
							if (button.Contains( touch ))
							{
								buttonValue = Utility.Max( buttonValue, touch.NormalizedPressure );
							}
						}
					}
				}
				else
				{
					buttonValue = currentTouch.NormalizedPressure;
				}

				ButtonState = buttonValue > 0.0f;
				SubmitButtonValue( target, buttonValue, updateTick, deltaTime );
				return;
			}
			#endif

			if (currentTouch == null && allowSlideToggle)
			{
				ButtonState = false;
				var touchCount = TouchManager.TouchCount;
				for (var i = 0; i < touchCount; i++)
				{
					ButtonState = ButtonState || button.Contains( TouchManager.GetTouch( i ) );
				}
			}

			SubmitButtonState( target, ButtonState, updateTick, deltaTime );
		}


		public override void CommitControlState( ulong updateTick, float deltaTime )
		{
			CommitButton( target );
		}


		public override void TouchBegan( Touch touch )
		{
			if (currentTouch != null)
			{
				return;
			}

			if (button.Contains( touch ))
			{
				ButtonState = true;
				currentTouch = touch;
			}
		}


		public override void TouchMoved( Touch touch )
		{
			if (currentTouch != touch)
			{
				return;
			}

			if (toggleOnLeave && !button.Contains( touch ))
			{
				ButtonState = false;
				currentTouch = null;
			}
		}


		public override void TouchEnded( Touch touch )
		{
			if (currentTouch != touch)
			{
				return;
			}

			ButtonState = false;
			currentTouch = null;
		}


		bool ButtonState
		{
			get
			{
				return buttonState;
			}

			set
			{
				if (buttonState != value)
				{
					buttonState = value;
					button.State = value;
				}
			}
		}


		public Vector3 ButtonPosition
		{
			get
			{
				return button.Ready ? button.Position : transform.position;
			}

			set
			{
				if (button.Ready)
				{
					button.Position = value;
				}
			}
		}


		public TouchControlAnchor Anchor
		{
			get
			{
				return anchor;
			}

			set
			{
				if (anchor != value)
				{
					anchor = value;
					dirty = true;
				}
			}
		}


		public Vector2 Offset
		{
			get
			{
				return offset;
			}

			set
			{
				if (offset != value)
				{
					offset = value;
					dirty = true;
				}
			}
		}


		public TouchUnitType OffsetUnitType
		{
			get
			{
				return offsetUnitType;
			}

			set
			{
				if (offsetUnitType != value)
				{
					offsetUnitType = value;
					dirty = true;
				}
			}
		}
	}
}
