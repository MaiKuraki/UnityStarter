namespace InControl
{
	using System;
	using UnityEngine;


	public class OneAxisInputControl : IInputControl
	{
		public ulong UpdateTick { get; protected set; }

		float sensitivity = 1.0f;
		float lowerDeadZone = 0.0f;
		float upperDeadZone = 1.0f;
		float stateThreshold = 0.0f;

		// ReSharper disable once InconsistentNaming
		protected bool isNullControl = false;

		// ReSharper disable once FieldCanBeMadeReadOnly.Global
		// ReSharper disable once ConvertToConstant.Global
		public float FirstRepeatDelay = 0.8f;

		// ReSharper disable once FieldCanBeMadeReadOnly.Global
		// ReSharper disable once ConvertToConstant.Global
		public float RepeatDelay = 0.1f;

		public bool Raw;

		private bool enabled = true;

		// ReSharper disable once InconsistentNaming
		protected bool ownerEnabled = true;

		ulong pendingTick;
		bool pendingCommit;

		float nextRepeatTime;
		bool wasRepeated;

		bool clearInputState;

		InputControlState lastState;
		InputControlState nextState;
		InputControlState thisState;


		void PrepareForUpdate( ulong updateTick )
		{
			if (isNullControl)
			{
				return;
			}

			if (updateTick < pendingTick)
			{
				throw new InvalidOperationException( "Cannot be updated with an earlier tick." );
			}

			if (pendingCommit && updateTick != pendingTick)
			{
				throw new InvalidOperationException( "Cannot be updated for a new tick until pending tick is committed." );
			}

			if (updateTick > pendingTick)
			{
				lastState = thisState;
				nextState.Reset();
				pendingTick = updateTick;
				pendingCommit = true;
			}
		}


		public bool UpdateWithState( bool state, ulong updateTick, float deltaTime )
		{
			if (isNullControl)
			{
				return false;
			}

			PrepareForUpdate( updateTick );

			nextState.Set( state || nextState.State );

			return state;
		}


		public bool UpdateWithValue( float value, ulong updateTick, float deltaTime )
		{
			if (isNullControl)
			{
				return false;
			}

			PrepareForUpdate( updateTick );

			if (Utility.Abs( value ) > Utility.Abs( nextState.RawValue ))
			{
				nextState.RawValue = value;

				if (!Raw)
				{
					value = Utility.ApplyDeadZone( value, lowerDeadZone, upperDeadZone );
					//value = Utility.ApplySmoothing( value, lastState.Value, deltaTime, sensitivity );
				}

				nextState.Set( value, stateThreshold );

				return true;
			}

			return false;
		}


		internal bool UpdateWithRawValue( float value, ulong updateTick, float deltaTime )
		{
			if (isNullControl)
			{
				return false;
			}

			Raw = true;

			PrepareForUpdate( updateTick );

			if (Utility.Abs( value ) > Utility.Abs( nextState.RawValue ))
			{
				nextState.RawValue = value;
				nextState.Set( value, stateThreshold );
				return true;
			}

			return false;
		}


		internal void SetValue( float value, ulong updateTick )
		{
			if (isNullControl)
			{
				return;
			}

			if (updateTick > pendingTick)
			{
				lastState = thisState;
				nextState.Reset();
				pendingTick = updateTick;
				pendingCommit = true;
			}

			nextState.RawValue = value;
			nextState.Set( value, StateThreshold );
		}


		public void ClearInputState()
		{
			lastState.Reset();
			thisState.Reset();
			nextState.Reset();
			wasRepeated = false;
			clearInputState = true;
		}


		public void Commit()
		{
			if (isNullControl)
			{
				return;
			}

			pendingCommit = false;
			// nextState.Set( Utility.ApplySmoothing( nextState.Value, lastState.Value, Time.deltaTime, sensitivity ), stateThreshold );
			thisState = nextState;

			if (clearInputState)
			{
				// The net result here should be that the entire state will return zero/false
				// from when ResetState() is called until the next call to Commit(), which is
				// the next update tick, and WasPressed, WasReleased and WasRepeated will then
				// return false during this following tick.
				lastState = nextState;
				UpdateTick = pendingTick;
				clearInputState = false;
				return;
			}

			var lastPressed = lastState.State;
			var thisPressed = thisState.State;

			wasRepeated = false;
			if (lastPressed && !thisPressed) // if was released...
			{
				nextRepeatTime = 0.0f;
			}
			else if (thisPressed) // if is pressed...
			{
				var realtimeSinceStartup = Time.realtimeSinceStartup;
				if (!lastPressed) // if was pressed
				{
					nextRepeatTime = realtimeSinceStartup + FirstRepeatDelay;
				}
				else if (realtimeSinceStartup >= nextRepeatTime)
				{
					wasRepeated = true;
					nextRepeatTime = realtimeSinceStartup + RepeatDelay;
				}
			}

			if (thisState != lastState)
			{
				UpdateTick = pendingTick;
			}
		}


		public void CommitWithState( bool state, ulong updateTick, float deltaTime )
		{
			UpdateWithState( state, updateTick, deltaTime );
			Commit();
		}


		public void CommitWithValue( float value, ulong updateTick, float deltaTime )
		{
			UpdateWithValue( value, updateTick, deltaTime );
			Commit();
		}


		internal void CommitWithSides( InputControl negativeSide, InputControl positiveSide, ulong updateTick, float deltaTime )
		{
			LowerDeadZone = Mathf.Max( negativeSide.LowerDeadZone, positiveSide.LowerDeadZone );
			UpperDeadZone = Mathf.Min( negativeSide.UpperDeadZone, positiveSide.UpperDeadZone );
			Raw = negativeSide.Raw || positiveSide.Raw;
			var value = Utility.ValueFromSides( negativeSide.RawValue, positiveSide.RawValue );
			CommitWithValue( value, updateTick, deltaTime );
		}


		public bool State
		{
			get { return EnabledInHierarchy && thisState.State; }
		}


		public bool LastState
		{
			get { return EnabledInHierarchy && lastState.State; }
		}


		public float Value
		{
			get { return EnabledInHierarchy ? thisState.Value : 0.0f; }
		}


		public float LastValue
		{
			get { return EnabledInHierarchy ? lastState.Value : 0.0f; }
		}


		public float RawValue
		{
			get { return EnabledInHierarchy ? thisState.RawValue : 0.0f; }
		}


		internal float NextRawValue
		{
			get { return EnabledInHierarchy ? nextState.RawValue : 0.0f; }
		}


		/// <summary>
		/// This differs from IsPressed in that it just means the control has a nonzero value
		/// whereas IsPressed means the absolute value is over StateThreshold.
		/// </summary>
		internal bool HasInput
		{
			get { return EnabledInHierarchy && Utility.IsNotZero( thisState.Value ); }
		}


		public bool HasChanged
		{
			get { return EnabledInHierarchy && thisState != lastState; }
		}


		public bool IsPressed
		{
			get { return EnabledInHierarchy && thisState.State; }
		}


		public bool WasPressed
		{
			get { return EnabledInHierarchy && thisState && !lastState; }
		}


		public bool WasReleased
		{
			get { return EnabledInHierarchy && !thisState && lastState; }
		}


		public bool WasRepeated
		{
			get { return EnabledInHierarchy && wasRepeated; }
		}


		public float Sensitivity
		{
			get { return sensitivity; }
			set { sensitivity = Mathf.Clamp01( value ); }
		}


		public float LowerDeadZone
		{
			get { return lowerDeadZone; }
			set { lowerDeadZone = Mathf.Clamp01( value ); }
		}


		public float UpperDeadZone
		{
			get { return upperDeadZone; }
			set { upperDeadZone = Mathf.Clamp01( value ); }
		}


		public float StateThreshold
		{
			get { return stateThreshold; }
			set { stateThreshold = Mathf.Clamp01( value ); }
		}


		public bool IsNullControl
		{
			get { return isNullControl; }
		}


		public bool Enabled
		{
			get { return enabled; }
			set { enabled = value; }
		}


		public bool EnabledInHierarchy
		{
			get { return enabled && ownerEnabled; }
		}


		public static implicit operator bool( OneAxisInputControl instance )
		{
			return instance.State;
		}


		public static implicit operator float( OneAxisInputControl instance )
		{
			return instance.Value;
		}
	}
}
