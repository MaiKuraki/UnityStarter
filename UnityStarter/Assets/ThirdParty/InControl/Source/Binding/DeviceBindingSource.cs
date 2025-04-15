namespace InControl
{
	using System;
	using System.IO;
	using UnityEngine;


	public class DeviceBindingSource : BindingSource
	{
		public InputControlType Control { get; protected set; }


		internal DeviceBindingSource()
		{
			Control = InputControlType.None;
		}


		public DeviceBindingSource( InputControlType control )
		{
			Control = control;
		}


		public override float GetValue( InputDevice inputDevice )
		{
			if (inputDevice == null)
			{
				return 0.0f;
			}

			return inputDevice.GetControl( Control ).Value;
		}


		public override bool GetState( InputDevice inputDevice )
		{
			if (inputDevice == null)
			{
				return false;
			}

			return inputDevice.GetControl( Control ).State;
		}


		public override string Name
		{
			get
			{
				if (BoundTo == null)
				{
					// Cannot query property 'Name' for unbound BindingSource.
					return "";
				}
				else
				{
					var inputDevice = BoundTo.Device;
					var inputControl = inputDevice.GetControl( Control );
					if (inputControl == InputControl.Null)
					{
						return Control.ToString();
					}

					return inputDevice.GetControl( Control ).Handle;
				}
			}
		}


		public override string DeviceName
		{
			get
			{
				if (BoundTo == null)
				{
					// Cannot query property 'DeviceName' for unbound BindingSource.
					return "";
				}
				else
				{
					var inputDevice = BoundTo.Device;
					if (inputDevice == InputDevice.Null)
					{
						return "Controller";
					}

					return inputDevice.Name;
				}
			}
		}


		public override InputDeviceClass DeviceClass
		{
			get
			{
				return BoundTo == null ? InputDeviceClass.Unknown : BoundTo.Device.DeviceClass;
			}
		}


		public override InputDeviceStyle DeviceStyle
		{
			get
			{
				return BoundTo == null ? InputDeviceStyle.Unknown : BoundTo.Device.DeviceStyle;
			}
		}


		public override bool Equals( BindingSource other )
		{
			if (other == null)
			{
				return false;
			}

			var bindingSource = other as DeviceBindingSource;
			if (bindingSource != null)
			{
				return Control == bindingSource.Control;
			}

			return false;
		}


		public override bool Equals( object other )
		{
			if (other == null)
			{
				return false;
			}

			var bindingSource = other as DeviceBindingSource;
			if (bindingSource != null)
			{
				return Control == bindingSource.Control;
			}

			return false;
		}


		public override int GetHashCode()
		{
			return Control.GetHashCode();
		}


		public override BindingSourceType BindingSourceType
		{
			get
			{
				return BindingSourceType.DeviceBindingSource;
			}
		}


		public override void Save( BinaryWriter writer )
		{
			writer.Write( (int) Control );
		}


		public override void Load( BinaryReader reader, UInt16 dataFormatVersion )
		{
			Control = (InputControlType) reader.ReadInt32();
		}


		internal override bool IsValid
		{
			get
			{
				if (BoundTo == null)
				{
					Logger.LogError( "Cannot query property 'IsValid' for unbound BindingSource." );
					return false;
				}
				else
				{
					return BoundTo.Device.HasControl( Control ) || Utility.TargetIsStandard( Control );
				}
			}
		}
	}
}
