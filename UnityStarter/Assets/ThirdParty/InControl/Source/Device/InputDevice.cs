namespace InControl
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using UnityEngine;


	public class InputDevice
	{
		public static readonly InputDevice Null = new InputDevice( "None" );

		public string Name { get; protected set; }
		public string Meta { get; protected set; }
		public int SortOrder { get; protected set; }

		public InputDeviceClass DeviceClass { get; protected set; }
		public InputDeviceStyle DeviceStyle { get; protected set; }

		public Guid GUID { get; private set; }
		public ulong LastInputTick { get; private set; }
		public bool IsActive { get; private set; }
		public bool IsAttached { get; private set; }

		protected bool RawSticks { get; private set; }

		readonly List<InputControl> controls;
		public ReadOnlyCollection<InputControl> Controls { get; protected set; }
		protected InputControl[] ControlsByTarget { get; private set; }

		public TwoAxisInputControl LeftStick { get; private set; }
		public TwoAxisInputControl RightStick { get; private set; }
		public TwoAxisInputControl DPad { get; private set; }

		bool hasLeftCommandControl;
		InputControl leftCommandSource;
		public InputControlType LeftCommandControl { get; private set; }

		bool hasRightCommandControl;
		InputControl rightCommandSource;
		public InputControlType RightCommandControl { get; private set; }

		/// <summary>
		/// When a device is passive, it will never be considered an active device.
		/// This may be useful if you want a device to be accessible, but not
		/// show up in places where active devices are used.
		/// Defaults to <code>false</code>.
		/// </summary>
		public bool Passive;


		protected struct AnalogSnapshotEntry
		{
			public float value;
			public float maxValue;
			public float minValue;


			public void TrackMinMaxValue( float currentValue )
			{
				maxValue = Mathf.Max( maxValue, currentValue );
				minValue = Mathf.Min( minValue, currentValue );
			}
		}

		protected AnalogSnapshotEntry[] AnalogSnapshot { get; set; }


		public InputDevice()
			: this( "" ) {}


		public InputDevice( string name )
			: this( name, false ) {}


		public InputDevice( string name, bool rawSticks )
		{
			Name = name;
			RawSticks = rawSticks;

			Meta = "";
			GUID = Guid.NewGuid();
			LastInputTick = 0;
			SortOrder = int.MaxValue;

			DeviceClass = InputDeviceClass.Unknown;
			DeviceStyle = InputDeviceStyle.Unknown;

			Passive = false;

			const int numInputControlTypes = (int) InputControlType.Count + 1;
			ControlsByTarget = new InputControl[numInputControlTypes];
			controls = new List<InputControl>( 32 );
			Controls = new ReadOnlyCollection<InputControl>( controls );

			RemoveAliasControls();
		}


		internal void OnAttached()
		{
			IsAttached = true;
			AddAliasControls();
		}


		internal void OnDetached()
		{
			IsAttached = false;
			StopVibration();
			RemoveAliasControls();
		}


		void AddAliasControls()
		{
			RemoveAliasControls();

			if (IsKnown)
			{
				LeftStick = new TwoAxisInputControl();
				RightStick = new TwoAxisInputControl();

				DPad = new TwoAxisInputControl();
				DPad.DeadZoneFunc = DeadZone.Separate;

				AddControl( InputControlType.LeftStickX, "Left Stick X" );
				AddControl( InputControlType.LeftStickY, "Left Stick Y" );
				AddControl( InputControlType.RightStickX, "Right Stick X" );
				AddControl( InputControlType.RightStickY, "Right Stick Y" );
				AddControl( InputControlType.DPadX, "DPad X" );
				AddControl( InputControlType.DPadY, "DPad Y" );

				#if UNITY_PS4
				AddControl( InputControlType.Command, "OPTIONS button" );
				#else
				AddControl( InputControlType.Command, "Command" );
				#endif

				LeftCommandControl = DeviceStyle.LeftCommandControl();
				leftCommandSource = GetControl( LeftCommandControl );
				hasLeftCommandControl = !leftCommandSource.IsNullControl;
				if (hasLeftCommandControl)
				{
					AddControl( InputControlType.LeftCommand, leftCommandSource.Handle );
				}

				RightCommandControl = DeviceStyle.RightCommandControl();
				rightCommandSource = GetControl( RightCommandControl );
				hasRightCommandControl = !rightCommandSource.IsNullControl;
				if (hasRightCommandControl)
				{
					AddControl( InputControlType.RightCommand, rightCommandSource.Handle );
				}

				ExpireControlCache();
			}
		}


		void RemoveAliasControls()
		{
			LeftStick = TwoAxisInputControl.Null;
			RightStick = TwoAxisInputControl.Null;
			DPad = TwoAxisInputControl.Null;

			RemoveControl( InputControlType.LeftStickX );
			RemoveControl( InputControlType.LeftStickY );
			RemoveControl( InputControlType.RightStickX );
			RemoveControl( InputControlType.RightStickY );
			RemoveControl( InputControlType.DPadX );
			RemoveControl( InputControlType.DPadY );
			RemoveControl( InputControlType.Command );
			RemoveControl( InputControlType.LeftCommand );
			RemoveControl( InputControlType.RightCommand );

			leftCommandSource = null;
			hasLeftCommandControl = false;

			rightCommandSource = null;
			hasRightCommandControl = false;

			ExpireControlCache();
		}


		protected void ClearControls()
		{
			Array.Clear( ControlsByTarget, 0, ControlsByTarget.Length );
			controls.Clear();
			ExpireControlCache();
		}


		public bool HasControl( InputControlType controlType )
		{
			return ControlsByTarget[(int) controlType] != null;
		}


		/// <summary>
		/// Gets the control with the specified control type. If the control does not exist, <c>InputControl.Null</c> is returned.
		/// </summary>
		/// <param name="controlType">The control type of the control to get.</param>
		public InputControl GetControl( InputControlType controlType )
		{
			var inputControl = ControlsByTarget[(int) controlType];
			return inputControl ?? InputControl.Null;
		}


		/// <summary>
		/// Gets the control with the specified control type. If the control does not exist, <c>InputControl.Null</c> is returned.
		/// </summary>
		/// <param name="controlType">The control type of the control to get.</param>
		public InputControl this[ InputControlType controlType ]
		{
			get { return GetControl( controlType ); }
		}


		// Warning: this is super inefficient. Don't use it unless you have to, m'kay?
		public static InputControlType GetInputControlTypeByName( string inputControlName )
		{
			return (InputControlType) Enum.Parse( typeof(InputControlType), inputControlName );
		}


		// Warning: this is super inefficient. Don't use it unless you have to, m'kay?
		public InputControl GetControlByName( string controlName )
		{
			var inputControlType = GetInputControlTypeByName( controlName );
			return GetControl( inputControlType );
		}


		public InputControl AddControl( InputControlType controlType, string handle )
		{
			var inputControl = ControlsByTarget[(int) controlType];
			if (inputControl == null)
			{
				inputControl = new InputControl( handle, controlType );
				ControlsByTarget[(int) controlType] = inputControl;
				controls.Add( inputControl );
				ExpireControlCache();
			}

			return inputControl;
		}


		public InputControl AddControl( InputControlType controlType, string handle, float lowerDeadZone, float upperDeadZone )
		{
			var inputControl = AddControl( controlType, handle );
			inputControl.LowerDeadZone = lowerDeadZone;
			inputControl.UpperDeadZone = upperDeadZone;
			return inputControl;
		}


		void RemoveControl( InputControlType controlType )
		{
			var inputControl = ControlsByTarget[(int) controlType];
			if (inputControl != null)
			{
				ControlsByTarget[(int) controlType] = null;
				controls.Remove( inputControl );
				ExpireControlCache();
			}
		}


		public void ClearInputState()
		{
			LeftStick.ClearInputState();
			RightStick.ClearInputState();
			DPad.ClearInputState();

			var controlsCount = Controls.Count;
			for (var i = 0; i < controlsCount; i++)
			{
				var control = Controls[i];
				if (control != null)
				{
					control.ClearInputState();
				}
			}
		}


		protected void UpdateWithState( InputControlType controlType, bool state, ulong updateTick, float deltaTime )
		{
			GetControl( controlType ).UpdateWithState( state, updateTick, deltaTime );
		}


		protected void UpdateWithValue( InputControlType controlType, float value, ulong updateTick, float deltaTime )
		{
			GetControl( controlType ).UpdateWithValue( value, updateTick, deltaTime );
		}
		
		
		protected void UpdateWithRawValue( InputControlType controlType, float value, ulong updateTick, float deltaTime )
		{
			GetControl( controlType ).UpdateWithRawValue( value, updateTick, deltaTime );
		}


		public void UpdateLeftStickWithValue( Vector2 value, ulong updateTick, float deltaTime )
		{
			LeftStickLeft.UpdateWithValue( Mathf.Max( 0.0f, -value.x ), updateTick, deltaTime );
			LeftStickRight.UpdateWithValue( Mathf.Max( 0.0f, value.x ), updateTick, deltaTime );

			if (InputManager.InvertYAxis)
			{
				LeftStickUp.UpdateWithValue( Mathf.Max( 0.0f, -value.y ), updateTick, deltaTime );
				LeftStickDown.UpdateWithValue( Mathf.Max( 0.0f, value.y ), updateTick, deltaTime );
			}
			else
			{
				LeftStickUp.UpdateWithValue( Mathf.Max( 0.0f, value.y ), updateTick, deltaTime );
				LeftStickDown.UpdateWithValue( Mathf.Max( 0.0f, -value.y ), updateTick, deltaTime );
			}
		}


		public void UpdateLeftStickWithRawValue( Vector2 value, ulong updateTick, float deltaTime )
		{
			LeftStickLeft.UpdateWithRawValue( Mathf.Max( 0.0f, -value.x ), updateTick, deltaTime );
			LeftStickRight.UpdateWithRawValue( Mathf.Max( 0.0f, value.x ), updateTick, deltaTime );

			if (InputManager.InvertYAxis)
			{
				LeftStickUp.UpdateWithRawValue( Mathf.Max( 0.0f, -value.y ), updateTick, deltaTime );
				LeftStickDown.UpdateWithRawValue( Mathf.Max( 0.0f, value.y ), updateTick, deltaTime );
			}
			else
			{
				LeftStickUp.UpdateWithRawValue( Mathf.Max( 0.0f, value.y ), updateTick, deltaTime );
				LeftStickDown.UpdateWithRawValue( Mathf.Max( 0.0f, -value.y ), updateTick, deltaTime );
			}
		}


		public void CommitLeftStick()
		{
			LeftStickUp.Commit();
			LeftStickDown.Commit();
			LeftStickLeft.Commit();
			LeftStickRight.Commit();
		}


		public void UpdateRightStickWithValue( Vector2 value, ulong updateTick, float deltaTime )
		{
			RightStickLeft.UpdateWithValue( Mathf.Max( 0.0f, -value.x ), updateTick, deltaTime );
			RightStickRight.UpdateWithValue( Mathf.Max( 0.0f, value.x ), updateTick, deltaTime );

			if (InputManager.InvertYAxis)
			{
				RightStickUp.UpdateWithValue( Mathf.Max( 0.0f, -value.y ), updateTick, deltaTime );
				RightStickDown.UpdateWithValue( Mathf.Max( 0.0f, value.y ), updateTick, deltaTime );
			}
			else
			{
				RightStickUp.UpdateWithValue( Mathf.Max( 0.0f, value.y ), updateTick, deltaTime );
				RightStickDown.UpdateWithValue( Mathf.Max( 0.0f, -value.y ), updateTick, deltaTime );
			}
		}


		public void UpdateRightStickWithRawValue( Vector2 value, ulong updateTick, float deltaTime )
		{
			RightStickLeft.UpdateWithRawValue( Mathf.Max( 0.0f, -value.x ), updateTick, deltaTime );
			RightStickRight.UpdateWithRawValue( Mathf.Max( 0.0f, value.x ), updateTick, deltaTime );

			if (InputManager.InvertYAxis)
			{
				RightStickUp.UpdateWithRawValue( Mathf.Max( 0.0f, -value.y ), updateTick, deltaTime );
				RightStickDown.UpdateWithRawValue( Mathf.Max( 0.0f, value.y ), updateTick, deltaTime );
			}
			else
			{
				RightStickUp.UpdateWithRawValue( Mathf.Max( 0.0f, value.y ), updateTick, deltaTime );
				RightStickDown.UpdateWithRawValue( Mathf.Max( 0.0f, -value.y ), updateTick, deltaTime );
			}
		}


		public void CommitRightStick()
		{
			RightStickUp.Commit();
			RightStickDown.Commit();
			RightStickLeft.Commit();
			RightStickRight.Commit();
		}


		public virtual void Update( ulong updateTick, float deltaTime )
		{
			// Implemented by subclasses, but does nothing by default.
		}


		void ProcessLeftStick( ulong updateTick, float deltaTime )
		{
			var x = Utility.ValueFromSides( LeftStickLeft.NextRawValue, LeftStickRight.NextRawValue );
			var y = Utility.ValueFromSides( LeftStickDown.NextRawValue, LeftStickUp.NextRawValue, InputManager.InvertYAxis );

			Vector2 v;
			if (RawSticks || LeftStickLeft.Raw || LeftStickRight.Raw || LeftStickUp.Raw || LeftStickDown.Raw)
			{
				v = new Vector2( x, y );
			}
			else
			{
				var lowerDeadZone = Utility.Max( LeftStickLeft.LowerDeadZone, LeftStickRight.LowerDeadZone, LeftStickUp.LowerDeadZone, LeftStickDown.LowerDeadZone );
				var upperDeadZone = Utility.Min( LeftStickLeft.UpperDeadZone, LeftStickRight.UpperDeadZone, LeftStickUp.UpperDeadZone, LeftStickDown.UpperDeadZone );
				v = LeftStick.DeadZoneFunc( x, y, lowerDeadZone, upperDeadZone );
			}

			LeftStick.Raw = true;
			LeftStick.UpdateWithAxes( v.x, v.y, updateTick, deltaTime );

			LeftStickX.Raw = true;
			LeftStickX.CommitWithValue( v.x, updateTick, deltaTime );

			LeftStickY.Raw = true;
			LeftStickY.CommitWithValue( v.y, updateTick, deltaTime );

			LeftStickLeft.SetValue( LeftStick.Left.Value, updateTick );
			LeftStickRight.SetValue( LeftStick.Right.Value, updateTick );
			LeftStickUp.SetValue( LeftStick.Up.Value, updateTick );
			LeftStickDown.SetValue( LeftStick.Down.Value, updateTick );
		}


		void ProcessRightStick( ulong updateTick, float deltaTime )
		{
			var x = Utility.ValueFromSides( RightStickLeft.NextRawValue, RightStickRight.NextRawValue );
			var y = Utility.ValueFromSides( RightStickDown.NextRawValue, RightStickUp.NextRawValue, InputManager.InvertYAxis );

			Vector2 v;
			if (RawSticks || RightStickLeft.Raw || RightStickRight.Raw || RightStickUp.Raw || RightStickDown.Raw)
			{
				v = new Vector2( x, y );
			}
			else
			{
				var lowerDeadZone = Utility.Max( RightStickLeft.LowerDeadZone, RightStickRight.LowerDeadZone, RightStickUp.LowerDeadZone, RightStickDown.LowerDeadZone );
				var upperDeadZone = Utility.Min( RightStickLeft.UpperDeadZone, RightStickRight.UpperDeadZone, RightStickUp.UpperDeadZone, RightStickDown.UpperDeadZone );
				v = RightStick.DeadZoneFunc( x, y, lowerDeadZone, upperDeadZone );
			}

			RightStick.Raw = true;
			RightStick.UpdateWithAxes( v.x, v.y, updateTick, deltaTime );

			RightStickX.Raw = true;
			RightStickX.CommitWithValue( v.x, updateTick, deltaTime );

			RightStickY.Raw = true;
			RightStickY.CommitWithValue( v.y, updateTick, deltaTime );

			RightStickLeft.SetValue( RightStick.Left.Value, updateTick );
			RightStickRight.SetValue( RightStick.Right.Value, updateTick );
			RightStickUp.SetValue( RightStick.Up.Value, updateTick );
			RightStickDown.SetValue( RightStick.Down.Value, updateTick );
		}


		void ProcessDPad( ulong updateTick, float deltaTime )
		{
			var x = Utility.ValueFromSides( DPadLeft.NextRawValue, DPadRight.NextRawValue );
			var y = Utility.ValueFromSides( DPadDown.NextRawValue, DPadUp.NextRawValue, InputManager.InvertYAxis );

			Vector2 v;
			if (RawSticks || DPadLeft.Raw || DPadRight.Raw || DPadUp.Raw || DPadDown.Raw)
			{
				v = new Vector2( x, y );
			}
			else
			{
				var lowerDeadZone = Utility.Max( DPadLeft.LowerDeadZone, DPadRight.LowerDeadZone, DPadUp.LowerDeadZone, DPadDown.LowerDeadZone );
				var upperDeadZone = Utility.Min( DPadLeft.UpperDeadZone, DPadRight.UpperDeadZone, DPadUp.UpperDeadZone, DPadDown.UpperDeadZone );
				v = DPad.DeadZoneFunc( x, y, lowerDeadZone, upperDeadZone );
			}

			DPad.Raw = true;
			DPad.UpdateWithAxes( v.x, v.y, updateTick, deltaTime );

			DPadX.Raw = true;
			DPadX.CommitWithValue( v.x, updateTick, deltaTime );

			DPadY.Raw = true;
			DPadY.CommitWithValue( v.y, updateTick, deltaTime );

			DPadLeft.SetValue( DPad.Left.Value, updateTick );
			DPadRight.SetValue( DPad.Right.Value, updateTick );
			DPadUp.SetValue( DPad.Up.Value, updateTick );
			DPadDown.SetValue( DPad.Down.Value, updateTick );
		}


		public void Commit( ulong updateTick, float deltaTime )
		{
			// We need to do some processing for known controllers to ensure all
			// the various control aliases holding directional values are calculated
			// optimally with circular deadzones and then set properly everywhere.
			if (IsKnown)
			{
				ProcessLeftStick( updateTick, deltaTime );
				ProcessRightStick( updateTick, deltaTime );
				ProcessDPad( updateTick, deltaTime );
			}

			// Commit all control values.
			var controlsCount = Controls.Count;
			for (var i = 0; i < controlsCount; i++)
			{
				var control = Controls[i];
				if (control != null)
				{
					control.Commit();
				}
			}

			// Calculate the Command alias state for known devices and commit it.
			if (IsKnown)
			{
				var passive = true;
				var pressed = false;
				for (var i = (int) InputControlType.Back; i <= (int) InputControlType.Mute; i++)
				{
					var control = ControlsByTarget[i];
					if (control != null && control.IsPressed)
					{
						pressed = true;
						if (!control.Passive)
						{
							passive = false;
						}
					}
				}

				Command.Passive = passive;
				Command.CommitWithState( pressed, updateTick, deltaTime );

				if (hasLeftCommandControl)
				{
					LeftCommand.Passive = leftCommandSource.Passive;
					LeftCommand.CommitWithState( leftCommandSource.IsPressed, updateTick, deltaTime );
				}

				if (hasRightCommandControl)
				{
					RightCommand.Passive = rightCommandSource.Passive;
					RightCommand.CommitWithState( rightCommandSource.IsPressed, updateTick, deltaTime );
				}
			}

			// If any non-passive controls provide input, flag the device active.
			IsActive = false;
			for (var i = 0; i < controlsCount; i++)
			{
				var control = Controls[i];
				if (control != null && control.HasInput && !control.Passive)
				{
					LastInputTick = updateTick;
					IsActive = true;
				}
			}
		}


		public bool LastInputAfter( InputDevice device )
		{
			return device == null || LastInputTick > device.LastInputTick;
		}


		public void RequestActivation()
		{
			LastInputTick = InputManager.CurrentTick;
			IsActive = true;
		}


		public virtual void Vibrate( float leftSpeed, float rightSpeed ) {}


		public void Vibrate( float intensity )
		{
			Vibrate( intensity, intensity );
		}


		public virtual void VibrateTriggers( float leftTriggerSpeed, float rightTriggerSpeed ) {}


		public void StopVibration()
		{
			Vibrate( 0.0f );
		}


		public virtual void SetLightColor( float red, float green, float blue ) {}


		public void SetLightColor( Color color )
		{
			SetLightColor( color.r * color.a, color.g * color.a, color.b * color.a );
		}


		public virtual void SetLightFlash( float flashOnDuration, float flashOffDuration ) {}


		public void StopLightFlash()
		{
			SetLightFlash( 1.0f, 0.0f );
		}


		public virtual bool IsSupportedOnThisPlatform
		{
			get { return true; }
		}


		public virtual bool IsKnown
		{
			get { return true; }
		}


		public bool IsUnknown
		{
			get { return !IsKnown; }
		}


		[Obsolete( "Use InputDevice.CommandIsPressed instead.", false )]
		public bool MenuIsPressed
		{
			get { return IsKnown && Command.IsPressed; }
		}


		[Obsolete( "Use InputDevice.CommandWasPressed instead.", false )]
		public bool MenuWasPressed
		{
			get { return IsKnown && Command.WasPressed; }
		}


		[Obsolete( "Use InputDevice.CommandWasReleased instead.", false )]
		public bool MenuWasReleased
		{
			get { return IsKnown && Command.WasReleased; }
		}


		public bool CommandIsPressed
		{
			get { return IsKnown && Command.IsPressed; }
		}


		public bool CommandWasPressed
		{
			get { return IsKnown && Command.WasPressed; }
		}


		public bool CommandWasReleased
		{
			get { return IsKnown && Command.WasReleased; }
		}


		public InputControl AnyButton
		{
			get
			{
				var controlsCount = Controls.Count;
				for (var i = 0; i < controlsCount; i++)
				{
					var control = Controls[i];
					if (control != null && control.IsButton && control.IsPressed)
					{
						return control;
					}
				}

				return InputControl.Null;
			}
		}


		public bool AnyButtonIsPressed
		{
			get
			{
				var controlCount = Controls.Count;
				for (var i = 0; i < controlCount; i++)
				{
					var control = Controls[i];
					if (control != null && control.IsButton && control.IsPressed)
					{
						return true;
					}
				}

				return false;
			}
		}


		public bool AnyButtonWasPressed
		{
			get
			{
				var controlCount = Controls.Count;
				for (var i = 0; i < controlCount; i++)
				{
					var control = Controls[i];
					if (control != null && control.IsButton && control.WasPressed)
					{
						return true;
					}
				}

				return false;
			}
		}


		public bool AnyButtonWasReleased
		{
			get
			{
				var controlCount = Controls.Count;
				for (var i = 0; i < controlCount; i++)
				{
					var control = Controls[i];
					if (control != null && control.IsButton && control.WasReleased)
					{
						return true;
					}
				}

				return false;
			}
		}


		public TwoAxisInputControl Direction
		{
			get { return DPad.UpdateTick > LeftStick.UpdateTick ? DPad : LeftStick; }
		}


		#region Cached Control Properties

		InputControl cachedLeftStickUp;
		InputControl cachedLeftStickDown;
		InputControl cachedLeftStickLeft;
		InputControl cachedLeftStickRight;
		InputControl cachedRightStickUp;
		InputControl cachedRightStickDown;
		InputControl cachedRightStickLeft;
		InputControl cachedRightStickRight;
		InputControl cachedDPadUp;
		InputControl cachedDPadDown;
		InputControl cachedDPadLeft;
		InputControl cachedDPadRight;
		InputControl cachedAction1;
		InputControl cachedAction2;
		InputControl cachedAction3;
		InputControl cachedAction4;
		InputControl cachedLeftTrigger;
		InputControl cachedRightTrigger;
		InputControl cachedLeftBumper;
		InputControl cachedRightBumper;
		InputControl cachedLeftStickButton;
		InputControl cachedRightStickButton;
		InputControl cachedLeftStickX;
		InputControl cachedLeftStickY;
		InputControl cachedRightStickX;
		InputControl cachedRightStickY;
		InputControl cachedDPadX;
		InputControl cachedDPadY;
		InputControl cachedCommand;
		InputControl cachedLeftCommand;
		InputControl cachedRightCommand;


		public InputControl LeftStickUp
		{
			get { return cachedLeftStickUp ?? (cachedLeftStickUp = GetControl( InputControlType.LeftStickUp )); }
		}

		public InputControl LeftStickDown
		{
			get { return cachedLeftStickDown ?? (cachedLeftStickDown = GetControl( InputControlType.LeftStickDown )); }
		}

		public InputControl LeftStickLeft
		{
			get { return cachedLeftStickLeft ?? (cachedLeftStickLeft = GetControl( InputControlType.LeftStickLeft )); }
		}

		public InputControl LeftStickRight
		{
			get { return cachedLeftStickRight ?? (cachedLeftStickRight = GetControl( InputControlType.LeftStickRight )); }
		}

		public InputControl RightStickUp
		{
			get { return cachedRightStickUp ?? (cachedRightStickUp = GetControl( InputControlType.RightStickUp )); }
		}

		public InputControl RightStickDown
		{
			get { return cachedRightStickDown ?? (cachedRightStickDown = GetControl( InputControlType.RightStickDown )); }
		}

		public InputControl RightStickLeft
		{
			get { return cachedRightStickLeft ?? (cachedRightStickLeft = GetControl( InputControlType.RightStickLeft )); }
		}

		public InputControl RightStickRight
		{
			get { return cachedRightStickRight ?? (cachedRightStickRight = GetControl( InputControlType.RightStickRight )); }
		}

		public InputControl DPadUp
		{
			get { return cachedDPadUp ?? (cachedDPadUp = GetControl( InputControlType.DPadUp )); }
		}

		public InputControl DPadDown
		{
			get { return cachedDPadDown ?? (cachedDPadDown = GetControl( InputControlType.DPadDown )); }
		}

		public InputControl DPadLeft
		{
			get { return cachedDPadLeft ?? (cachedDPadLeft = GetControl( InputControlType.DPadLeft )); }
		}

		public InputControl DPadRight
		{
			get { return cachedDPadRight ?? (cachedDPadRight = GetControl( InputControlType.DPadRight )); }
		}

		public InputControl Action1
		{
			get { return cachedAction1 ?? (cachedAction1 = GetControl( InputControlType.Action1 )); }
		}

		public InputControl Action2
		{
			get { return cachedAction2 ?? (cachedAction2 = GetControl( InputControlType.Action2 )); }
		}

		public InputControl Action3
		{
			get { return cachedAction3 ?? (cachedAction3 = GetControl( InputControlType.Action3 )); }
		}

		public InputControl Action4
		{
			get { return cachedAction4 ?? (cachedAction4 = GetControl( InputControlType.Action4 )); }
		}

		public InputControl LeftTrigger
		{
			get { return cachedLeftTrigger ?? (cachedLeftTrigger = GetControl( InputControlType.LeftTrigger )); }
		}

		public InputControl RightTrigger
		{
			get { return cachedRightTrigger ?? (cachedRightTrigger = GetControl( InputControlType.RightTrigger )); }
		}

		public InputControl LeftBumper
		{
			get { return cachedLeftBumper ?? (cachedLeftBumper = GetControl( InputControlType.LeftBumper )); }
		}

		public InputControl RightBumper
		{
			get { return cachedRightBumper ?? (cachedRightBumper = GetControl( InputControlType.RightBumper )); }
		}

		public InputControl LeftStickButton
		{
			get { return cachedLeftStickButton ?? (cachedLeftStickButton = GetControl( InputControlType.LeftStickButton )); }
		}

		public InputControl RightStickButton
		{
			get { return cachedRightStickButton ?? (cachedRightStickButton = GetControl( InputControlType.RightStickButton )); }
		}

		public InputControl LeftStickX
		{
			get { return cachedLeftStickX ?? (cachedLeftStickX = GetControl( InputControlType.LeftStickX )); }
		}

		public InputControl LeftStickY
		{
			get { return cachedLeftStickY ?? (cachedLeftStickY = GetControl( InputControlType.LeftStickY )); }
		}

		public InputControl RightStickX
		{
			get { return cachedRightStickX ?? (cachedRightStickX = GetControl( InputControlType.RightStickX )); }
		}

		public InputControl RightStickY
		{
			get { return cachedRightStickY ?? (cachedRightStickY = GetControl( InputControlType.RightStickY )); }
		}

		public InputControl DPadX
		{
			get { return cachedDPadX ?? (cachedDPadX = GetControl( InputControlType.DPadX )); }
		}

		public InputControl DPadY
		{
			get { return cachedDPadY ?? (cachedDPadY = GetControl( InputControlType.DPadY )); }
		}

		public InputControl Command
		{
			get { return cachedCommand ?? (cachedCommand = GetControl( InputControlType.Command )); }
		}


		public InputControl LeftCommand
		{
			get { return cachedLeftCommand ?? (cachedLeftCommand = GetControl( InputControlType.LeftCommand )); }
		}


		public InputControl RightCommand
		{
			get { return cachedRightCommand ?? (cachedRightCommand = GetControl( InputControlType.RightCommand )); }
		}


		void ExpireControlCache()
		{
			cachedLeftStickUp = null;
			cachedLeftStickDown = null;
			cachedLeftStickLeft = null;
			cachedLeftStickRight = null;
			cachedRightStickUp = null;
			cachedRightStickDown = null;
			cachedRightStickLeft = null;
			cachedRightStickRight = null;
			cachedDPadUp = null;
			cachedDPadDown = null;
			cachedDPadLeft = null;
			cachedDPadRight = null;
			cachedAction1 = null;
			cachedAction2 = null;
			cachedAction3 = null;
			cachedAction4 = null;
			cachedLeftTrigger = null;
			cachedRightTrigger = null;
			cachedLeftBumper = null;
			cachedRightBumper = null;
			cachedLeftStickButton = null;
			cachedRightStickButton = null;
			cachedLeftStickX = null;
			cachedLeftStickY = null;
			cachedRightStickX = null;
			cachedRightStickY = null;
			cachedDPadX = null;
			cachedDPadY = null;
			cachedCommand = null;
			cachedLeftCommand = null;
			cachedRightCommand = null;
		}

		#endregion


		#region Snapshots for Unknown Devices

		public virtual int NumUnknownAnalogs
		{
			get { return 0; }
		}


		public virtual int NumUnknownButtons
		{
			get { return 0; }
		}


		public virtual bool ReadRawButtonState( int index )
		{
			return false;
		}


		public virtual float ReadRawAnalogValue( int index )
		{
			return 0.0f;
		}


		public void TakeSnapshot()
		{
			if (AnalogSnapshot == null)
			{
				AnalogSnapshot = new AnalogSnapshotEntry[NumUnknownAnalogs];
			}

			for (var i = 0; i < NumUnknownAnalogs; i++)
			{
				var analogValue = Utility.ApplySnapping( ReadRawAnalogValue( i ), 0.5f );
				AnalogSnapshot[i].value = analogValue;
			}
		}


		public UnknownDeviceControl GetFirstPressedAnalog()
		{
			if (AnalogSnapshot != null)
			{
				for (var index = 0; index < NumUnknownAnalogs; index++)
				{
					var control = InputControlType.Analog0 + index;

					var analogValue = Utility.ApplySnapping( ReadRawAnalogValue( index ), 0.5f );
					var analogDelta = analogValue - AnalogSnapshot[index].value;

					AnalogSnapshot[index].TrackMinMaxValue( analogValue );

					if (analogDelta > +0.1f)
					{
						analogDelta = AnalogSnapshot[index].maxValue - AnalogSnapshot[index].value;
					}

					if (analogDelta < -0.1f)
					{
						analogDelta = AnalogSnapshot[index].minValue - AnalogSnapshot[index].value;
					}

					if (analogDelta > +1.9f)
					{
						return new UnknownDeviceControl( control, InputRangeType.MinusOneToOne );
					}

					if (analogDelta < -0.9f)
					{
						return new UnknownDeviceControl( control, InputRangeType.ZeroToMinusOne );
					}

					if (analogDelta > +0.9f)
					{
						return new UnknownDeviceControl( control, InputRangeType.ZeroToOne );
					}
				}
			}

			return UnknownDeviceControl.None;
		}


		public UnknownDeviceControl GetFirstPressedButton()
		{
			for (var index = 0; index < NumUnknownButtons; index++)
			{
				if (ReadRawButtonState( index ))
				{
					return new UnknownDeviceControl( InputControlType.Button0 + index, InputRangeType.ZeroToOne );
				}
			}

			return UnknownDeviceControl.None;
		}

		#endregion
	}
}
