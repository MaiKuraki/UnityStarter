namespace InControl
{
	public class InputControl : OneAxisInputControl
	{
		public static readonly InputControl Null = new InputControl { isNullControl = true };

		// TODO: Deprecate and replace with "Name"
		public string Handle { get; protected set; }

		// TODO: Deprecate and replace with "ControlType"
		public InputControlType Target { get; protected set; }

		/// <summary>
		/// When a control is passive, it will not cause a device to be considered active.
		/// This is useful for certain controls that spam data, like gyro or
		/// accelerometer input.
		/// Defaults to <code>false</code>.
		/// </summary>
		public bool Passive;

		// TODO: This meaningless distinction should probably be removed entirely.
		public bool IsButton { get; protected set; }
		public bool IsAnalog { get; protected set; }

		ulong zeroTick;


		InputControl()
		{
			Handle = "None";
			Target = InputControlType.None;
			Passive = false;
			IsButton = false;
			IsAnalog = false;
		}


		public InputControl( string handle, InputControlType target )
		{
			Handle = handle;
			Target = target;
			Passive = false;
			IsButton = Utility.TargetIsButton( target );
			IsAnalog = !IsButton;
		}


		public InputControl( string handle, InputControlType target, bool passive )
			: this( handle, target )
		{
			Passive = passive;
		}


		internal void SetZeroTick()
		{
			zeroTick = UpdateTick;
		}


		internal bool IsOnZeroTick
		{
			get
			{
				return UpdateTick == zeroTick;
			}
		}


		public bool IsStandard
		{
			get
			{
				return Utility.TargetIsStandard( Target );
			}
		}
	}
}
