// ReSharper disable UnusedMember.Global
namespace InControl
{
	using System;
	using System.Globalization;
	using UnityEngine;


	public class OptionalTypeHasNoValueException : SystemException
	{
		public OptionalTypeHasNoValueException( string message )
			: base( message ) {}
	}


	[Serializable]
	public struct OptionalFloat
	{
		[SerializeField]
		// ReSharper disable once InconsistentNaming
		bool hasValue;

		[SerializeField]
		// ReSharper disable once InconsistentNaming
		float value;


		public OptionalFloat( float value )
		{
			this.value = value;
			hasValue = true;
		}


		public bool HasValue
		{
			get
			{
				return hasValue;
			}
		}


		public bool HasNoValue
		{
			get
			{
				return !hasValue;
			}
		}


		public float Value
		{
			get
			{
				if (!hasValue)
				{
					throw new OptionalTypeHasNoValueException( "Trying to get a value from an OptionalFloat that has no value." );
				}

				return value;
			}

			set
			{
				this.value = value;
				hasValue = true;
			}
		}


		public void Clear()
		{
			value = 0.0f;
			hasValue = false;
		}


		public float GetValueOrDefault( float defaultValue )
		{
			return hasValue ? value : defaultValue;
		}


		public float GetValueOrZero()
		{
			return hasValue ? value : 0.0f;
		}


		public void SetValue( float value )
		{
			this.value = value;
			hasValue = true;
		}


		public override bool Equals( object other )
		{
			return ReferenceEquals( other, null ) && !hasValue ||
			       value.Equals( other );
		}


		public bool Equals( OptionalFloat other )
		{
			return hasValue &&
			       other.hasValue &&
			       IsApproximatelyEqual( value, other.value );
		}


		public bool Equals( float other )
		{
			return hasValue &&
			       IsApproximatelyEqual( value, other );
		}


		public static bool operator ==( OptionalFloat a, OptionalFloat b )
		{
			return a.hasValue &&
			       b.hasValue &&
			       IsApproximatelyEqual( a.value, b.value );
		}


		public static bool operator !=( OptionalFloat a, OptionalFloat b )
		{
			return !(a == b);
		}


		public static bool operator ==( OptionalFloat a, float b )
		{
			return a.hasValue && IsApproximatelyEqual( a.value, b );
		}


		public static bool operator !=( OptionalFloat a, float b )
		{
			return !(a.hasValue && IsApproximatelyEqual( a.value, b ));
		}


		static int CombineHashCodes( int h1, int h2 )
		{
			unchecked
			{
				return ((h1 << 5) + h1) ^ h2;
			}
		}


		public override int GetHashCode()
		{
			// ReSharper disable NonReadonlyMemberInGetHashCode
			return CombineHashCodes( hasValue.GetHashCode(), value.GetHashCode() );
			// ReSharper restore NonReadonlyMemberInGetHashCode
		}


		public override string ToString()
		{
			return hasValue ? value.ToString( CultureInfo.InvariantCulture ) : "";
		}


		public static implicit operator OptionalFloat( float value )
		{
			return new OptionalFloat( value );
		}


		public static explicit operator float( OptionalFloat optional )
		{
			return optional.Value;
		}


		const float epsilon = 1.0e-7f;


		static bool IsApproximatelyEqual( float a, float b )
		{
			var delta = a - b;
			return delta >= -epsilon &&
			       delta <= +epsilon;
		}


		public bool ApproximatelyEquals( float other )
		{
			if (!hasValue)
			{
				return false;
			}

			var delta = value - other;
			return delta >= -epsilon &&
			       delta <= +epsilon;
		}
	}
}
