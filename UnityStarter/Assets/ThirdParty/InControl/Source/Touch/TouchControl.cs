namespace InControl
{
	using UnityEngine;


	public abstract class TouchControl : MonoBehaviour
	{
		public enum ButtonTarget : int
		{
			None = InputControlType.None,

			DPadDown = InputControlType.DPadDown,
			DPadLeft = InputControlType.DPadLeft,
			DPadRight = InputControlType.DPadRight,
			DPadUp = InputControlType.DPadUp,

			LeftTrigger = InputControlType.LeftTrigger,
			RightTrigger = InputControlType.RightTrigger,

			LeftBumper = InputControlType.LeftBumper,
			RightBumper = InputControlType.RightBumper,

			Action1 = InputControlType.Action1,
			Action2 = InputControlType.Action2,
			Action3 = InputControlType.Action3,
			Action4 = InputControlType.Action4,
			Action5 = InputControlType.Action5,
			Action6 = InputControlType.Action6,
			Action7 = InputControlType.Action7,
			Action8 = InputControlType.Action8,
			Action9 = InputControlType.Action9,
			Action10 = InputControlType.Action10,
			Action11 = InputControlType.Action11,
			Action12 = InputControlType.Action12,

			Menu = InputControlType.Menu,

			Button0 = InputControlType.Button0,
			Button1 = InputControlType.Button1,
			Button2 = InputControlType.Button2,
			Button3 = InputControlType.Button3,
			Button4 = InputControlType.Button4,
			Button5 = InputControlType.Button5,
			Button6 = InputControlType.Button6,
			Button7 = InputControlType.Button7,
			Button8 = InputControlType.Button8,
			Button9 = InputControlType.Button9,
			Button10 = InputControlType.Button10,
			Button11 = InputControlType.Button11,
			Button12 = InputControlType.Button12,
			Button13 = InputControlType.Button13,
			Button14 = InputControlType.Button14,
			Button15 = InputControlType.Button15,
			Button16 = InputControlType.Button16,
			Button17 = InputControlType.Button17,
			Button18 = InputControlType.Button18,
			Button19 = InputControlType.Button19
		}


		public enum AnalogTarget : int
		{
			None,
			LeftStick,
			RightStick,
			Both
		}


		public enum SnapAngles : int
		{
			None = 0,
			Four = 4,
			Eight = 8,
			Sixteen = 16
		}


		public abstract void CreateControl();
		public abstract void DestroyControl();
		public abstract void ConfigureControl();
		public abstract void SubmitControlState( ulong updateTick, float deltaTime );
		public abstract void CommitControlState( ulong updateTick, float deltaTime );
		public abstract void TouchBegan( Touch touch );
		public abstract void TouchMoved( Touch touch );
		public abstract void TouchEnded( Touch touch );
		public abstract void DrawGizmos();


		void OnEnable()
		{
			TouchManager.OnSetup += Setup;
		}


		void OnDisable()
		{
			DestroyControl();
			Resources.UnloadUnusedAssets();
		}


		void Setup()
		{
			if (!enabled)
			{
				return;
			}

			CreateControl();
			ConfigureControl();
		}


		protected Vector3 OffsetToWorldPosition( TouchControlAnchor anchor, Vector2 offset, TouchUnitType offsetUnitType, bool lockAspectRatio )
		{
			Vector3 worldOffset;
			if (offsetUnitType == TouchUnitType.Pixels)
			{
				worldOffset = TouchUtility.RoundVector( offset ) * TouchManager.PixelToWorld;
			}
			else
			{
				if (lockAspectRatio)
				{
					worldOffset = (Vector3) offset * TouchManager.PercentToWorld;
				}
				else
				{
					worldOffset = Vector3.Scale( offset, TouchManager.ViewSize );
				}
			}

			return TouchManager.ViewToWorldPoint( TouchUtility.AnchorToViewPoint( anchor ) ) + worldOffset;
		}


		protected void SubmitButtonState( ButtonTarget target, bool state, ulong updateTick, float deltaTime )
		{
			if (TouchManager.Device == null || target == ButtonTarget.None)
			{
				return;
			}

			var control = TouchManager.Device.GetControl( (InputControlType) target );
			if (control != null && control != InputControl.Null)
			{
				control.UpdateWithState( state, updateTick, deltaTime );
			}
		}


		protected void SubmitButtonValue( ButtonTarget target, float value, ulong updateTick, float deltaTime )
		{
			if (TouchManager.Device == null || target == ButtonTarget.None)
			{
				return;
			}

			var control = TouchManager.Device.GetControl( (InputControlType) target );
			if (control != null && control != InputControl.Null)
			{
				control.UpdateWithValue( value, updateTick, deltaTime );
			}
		}


		protected void CommitButton( ButtonTarget target )
		{
			if (TouchManager.Device == null || target == ButtonTarget.None)
			{
				return;
			}

			var control = TouchManager.Device.GetControl( (InputControlType) target );
			if (control != null && control != InputControl.Null)
			{
				control.Commit();
			}
		}


		protected void SubmitAnalogValue( AnalogTarget target, Vector2 value, float lowerDeadZone, float upperDeadZone, ulong updateTick, float deltaTime )
		{
			if (TouchManager.Device == null || target == AnalogTarget.None)
			{
				return;
			}

			var v = DeadZone.Circular( value.x, value.y, lowerDeadZone, upperDeadZone );

			if (target == AnalogTarget.LeftStick || target == AnalogTarget.Both)
			{
				TouchManager.Device.UpdateLeftStickWithValue( v, updateTick, deltaTime );
			}

			if (target == AnalogTarget.RightStick || target == AnalogTarget.Both)
			{
				TouchManager.Device.UpdateRightStickWithValue( v, updateTick, deltaTime );
			}
		}


		protected void CommitAnalog( AnalogTarget target )
		{
			if (TouchManager.Device == null || target == AnalogTarget.None)
			{
				return;
			}

			if (target == AnalogTarget.LeftStick || target == AnalogTarget.Both)
			{
				TouchManager.Device.CommitLeftStick();
			}

			if (target == AnalogTarget.RightStick || target == AnalogTarget.Both)
			{
				TouchManager.Device.CommitRightStick();
			}
		}


		protected void SubmitRawAnalogValue( AnalogTarget target, Vector2 rawValue, ulong updateTick, float deltaTime )
		{
			if (TouchManager.Device == null || target == AnalogTarget.None)
			{
				return;
			}

			if (target == AnalogTarget.LeftStick || target == AnalogTarget.Both)
			{
				TouchManager.Device.UpdateLeftStickWithRawValue( rawValue, updateTick, deltaTime );
			}

			if (target == AnalogTarget.RightStick || target == AnalogTarget.Both)
			{
				TouchManager.Device.UpdateRightStickWithRawValue( rawValue, updateTick, deltaTime );
			}
		}


		protected static Vector3 SnapTo( Vector2 vector, SnapAngles snapAngles )
		{
			if (snapAngles == SnapAngles.None)
			{
				return vector;
			}

			var snapAngle = 360.0f / ((int) snapAngles);

			return SnapTo( vector, snapAngle );
		}


		protected static Vector3 SnapTo( Vector2 vector, float snapAngle )
		{
			var angle = Vector2.Angle( vector, Vector2.up );

			if (angle < snapAngle / 2.0f)
			{
				return Vector2.up * vector.magnitude;
			}

			if (angle > 180.0f - snapAngle / 2.0f)
			{
				return -Vector2.up * vector.magnitude;
			}

			var t = Mathf.Round( angle / snapAngle );
			var deltaAngle = (t * snapAngle) - angle;
			var axis = Vector3.Cross( Vector2.up, vector );
			var q = Quaternion.AngleAxis( deltaAngle, axis );

			return q * vector;
		}


		void OnDrawGizmosSelected()
		{
			if (!enabled)
			{
				return;
			}

			if (TouchManager.ControlsShowGizmos != TouchManager.GizmoShowOption.WhenSelected)
			{
				return;
			}

			if (Utility.GameObjectIsCulledOnCurrentCamera( gameObject ))
			{
				return;
			}

			if (!Application.isPlaying)
			{
				ConfigureControl();
			}

			DrawGizmos();
		}


		void OnDrawGizmos()
		{
			if (!enabled)
			{
				return;
			}

			if (TouchManager.ControlsShowGizmos == TouchManager.GizmoShowOption.UnlessPlaying)
			{
				if (Application.isPlaying)
				{
					return;
				}
			}
			else if (TouchManager.ControlsShowGizmos != TouchManager.GizmoShowOption.Always)
			{
				return;
			}

			if (Utility.GameObjectIsCulledOnCurrentCamera( gameObject ))
			{
				return;
			}

			if (!Application.isPlaying)
			{
				ConfigureControl();
			}

			DrawGizmos();
		}
	}
}
