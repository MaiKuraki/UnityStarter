namespace InControl
{
	#pragma warning disable 0660, 0661

	public struct InputControlState
	{
		public bool State;
		public float Value;
		public float RawValue;


		public void Reset()
		{
			State = false;
			Value = 0.0f;
			RawValue = 0.0f;
		}


		public void Set( float value )
		{
			Value = value;
			State = Utility.IsNotZero( value );
		}


		public void Set( float value, float threshold )
		{
			Value = value;
			State = Utility.AbsoluteIsOverThreshold( value, threshold );
		}


		public void Set( bool state )
		{
			State = state;
			Value = state ? 1.0f : 0.0f;
			RawValue = Value;
		}


		public static implicit operator bool( InputControlState state )
		{
			return state.State;
		}


		public static implicit operator float( InputControlState state )
		{
			return state.Value;
		}


		public static bool operator ==( InputControlState a, InputControlState b )
		{
			return a.State == b.State && Utility.Approximately( a.Value, b.Value );
		}


		public static bool operator !=( InputControlState a, InputControlState b )
		{
			return a.State != b.State || !Utility.Approximately( a.Value, b.Value );
		}
	}
}
