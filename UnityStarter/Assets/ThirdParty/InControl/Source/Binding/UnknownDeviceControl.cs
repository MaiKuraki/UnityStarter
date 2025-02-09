namespace InControl
{
	using System;
	using System.IO;


	public struct UnknownDeviceControl : IEquatable<UnknownDeviceControl>
	{
		public static readonly UnknownDeviceControl None = new UnknownDeviceControl( InputControlType.None, InputRangeType.None );

		public InputControlType Control;
		public InputRangeType SourceRange;

		// TODO: This meaningless distinction should probably be removed entirely.
		public bool IsButton;
		public bool IsAnalog;


		public UnknownDeviceControl( InputControlType control, InputRangeType sourceRange )
		{
			Control = control;
			SourceRange = sourceRange;
			IsButton = Utility.TargetIsButton( control );
			IsAnalog = !IsButton;
		}


		internal float GetValue( InputDevice device )
		{
			if (device == null)
			{
				return 0.0f;
			}

			var value = device.GetControl( Control ).Value;
			return InputRange.Remap( value, SourceRange, InputRangeType.ZeroToOne );
		}


		public int Index
		{
			get
			{
				return (int) (Control - (IsButton ? InputControlType.Button0 : InputControlType.Analog0));
			}
		}


		/// <summary>
		/// Determines whether the specified objects are equal.
		/// </summary>
		/// <param name="a">The first object to compare.</param>
		/// <param name="b">The second object to compare.</param>
		/// <returns><c>true</c> if the specified objects are equal; otherwise, <c>false</c>.</returns>
		public static bool operator ==( UnknownDeviceControl a, UnknownDeviceControl b )
		{
			if (ReferenceEquals( null, a ))
			{
				return ReferenceEquals( null, b );
			}

			return a.Equals( b );
		}


		/// <summary>
		/// Determines whether the specified objects are not equal.
		/// </summary>
		/// <param name="a">The first object to compare.</param>
		/// <param name="b">The second object to compare.</param>
		/// <returns><c>true</c> if the specified objects are not equal; otherwise, <c>false</c>.</returns>
		public static bool operator !=( UnknownDeviceControl a, UnknownDeviceControl b )
		{
			return !(a == b);
		}


		/// <summary>
		/// Determines whether the specified object is equal to the current object.
		/// </summary>
		/// <param name="other">The object to compare with the current object.</param>
		/// <returns><c>true</c> if the specified object is equal to the current
		/// object; otherwise, <c>false</c>.</returns>
		public bool Equals( UnknownDeviceControl other )
		{
			return Control == other.Control && SourceRange == other.SourceRange;
		}


		/// <summary>
		/// Determines whether the specified object is equal to the current object.
		/// </summary>
		/// <param name="other">The object to compare with the current object.</param>
		/// <returns><c>true</c> if the specified object is equal to the current
		/// object; otherwise, <c>false</c>.</returns>
		public override bool Equals( object other )
		{
			return Equals( (UnknownDeviceControl) other );
		}


		/// <summary>
		/// Serves as a hash function for this object.
		/// </summary>
		/// <returns>A hash code for this instance that is suitable for use in 
		/// hashing algorithms and data structures such as a hash table.</returns>
		public override int GetHashCode()
		{
			return Control.GetHashCode() ^ SourceRange.GetHashCode();
		}


		/// <summary>
		/// Returns true if the Control property is not InputControlType.None
		/// </summary>
		public static implicit operator bool( UnknownDeviceControl control )
		{
			return control.Control != InputControlType.None;
		}


		override public string ToString()
		{
			return string.Format( "UnknownDeviceControl( {0}, {1} )", Control.ToString(), SourceRange.ToString() );
		}


		internal void Save( BinaryWriter writer )
		{
			writer.Write( (Int32) Control );
			writer.Write( (Int32) SourceRange );
		}


		internal void Load( BinaryReader reader )
		{
			Control = (InputControlType) reader.ReadInt32();
			SourceRange = (InputRangeType) reader.ReadInt32();
			IsButton = Utility.TargetIsButton( Control );
			IsAnalog = !IsButton;
		}
	}
}
