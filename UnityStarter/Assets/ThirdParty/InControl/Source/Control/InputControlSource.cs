namespace InControl
{
	using System;
	using UnityEngine;


	public enum InputControlSourceType
	{
		None,
		Button,
		Analog,
		KeyCode,
	}


	[Serializable]
	public struct InputControlSource
	{
		[SerializeField]
		InputControlSourceType sourceType;

		[SerializeField]
		int index;

		public InputControlSourceType SourceType { get { return sourceType; } set { sourceType = value; } }
		public int Index { get { return index; } set { index = value; } }


		public InputControlSource( InputControlSourceType sourceType, int index )
		{
			this.sourceType = sourceType;
			this.index = index;
		}


		public InputControlSource( KeyCode keyCode )
			: this( InputControlSourceType.KeyCode, (int) keyCode ) {}


		public float GetValue( InputDevice inputDevice )
		{
			switch (SourceType)
			{
				case InputControlSourceType.None:
					return 0.0f;
				case InputControlSourceType.Button:
					return GetState( inputDevice ) ? 1.0f : 0.0f;
				case InputControlSourceType.Analog:
					return inputDevice.ReadRawAnalogValue( Index );
				case InputControlSourceType.KeyCode:
					return GetState( inputDevice ) ? 1.0f : 0.0f;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}


		public bool GetState( InputDevice inputDevice )
		{
			switch (SourceType)
			{
				case InputControlSourceType.None:
					return false;
				case InputControlSourceType.Button:
					return inputDevice.ReadRawButtonState( Index );
				case InputControlSourceType.Analog:
					return Utility.IsNotZero( GetValue( inputDevice ) );
				case InputControlSourceType.KeyCode:
					return Input.GetKey( (KeyCode) Index );
				default:
					throw new ArgumentOutOfRangeException();
			}
		}


		public string ToCode()
		{
			return "new InputControlSource( InputControlSourceType." + SourceType + ", " + Index + " )";
		}
	}
}
