// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable ConvertToConstant.Global
// ReSharper disable RedundantDefaultMemberInitializer
namespace InControl
{
	using System;
	using UnityEngine;


	[Serializable]
	public class InputControlMapping
	{
		#region Fields

		[SerializeField]
		string name = "";

		// TODO: It seems like this can just be replaced with an inverted target range.
		// Invert the final mapped value.
		[SerializeField]
		bool invert = false;

		// Analog values will be multiplied by this number before processing.
		[SerializeField]
		float scale = 1.0f;

		// Raw inputs won't be processed except for scaling.
		[SerializeField]
		bool raw = false;

		// Changes won't trigger changes in active device or update tick.
		[SerializeField]
		bool passive = false;

		// This is primarily to fix an issue with the wired Xbox controller on Mac.
		[SerializeField]
		bool ignoreInitialZeroValue = false;

		[SerializeField]
		float sensitivity = 1.0f;

		[SerializeField]
		float lowerDeadZone = 0.0f;

		[SerializeField]
		float upperDeadZone = 1.0f;

		[SerializeField]
		InputControlSource source;

		[SerializeField]
		InputControlType target = InputControlType.None;

		[SerializeField]
		InputRangeType sourceRange = InputRangeType.MinusOneToOne;

		[SerializeField]
		InputRangeType targetRange = InputRangeType.MinusOneToOne;

		#endregion


		#region Properties

		public string Name { get { return string.IsNullOrEmpty( name ) ? Target.ToString() : name; } set { name = value; } }
		public bool Invert { get { return invert; } set { invert = value; } }
		public float Scale { get { return scale; } set { scale = value; } }
		public bool Raw { get { return raw; } set { raw = value; } }
		public bool Passive { get { return passive; } set { passive = value; } }
		public bool IgnoreInitialZeroValue { get { return ignoreInitialZeroValue; } set { ignoreInitialZeroValue = value; } }
		public float Sensitivity { get { return sensitivity; } set { sensitivity = Mathf.Clamp01( value ); } }
		public float LowerDeadZone { get { return lowerDeadZone; } set { lowerDeadZone = Mathf.Clamp01( value ); } }
		public float UpperDeadZone { get { return upperDeadZone; } set { upperDeadZone = Mathf.Clamp01( value ); } }
		public InputControlSource Source { get { return source; } set { source = value; } }
		public InputControlType Target { get { return target; } set { target = value; } }
		public InputRangeType SourceRange { get { return sourceRange; } set { sourceRange = value; } }
		public InputRangeType TargetRange { get { return targetRange; } set { targetRange = value; } }

		#endregion


		public float ApplyToValue( float value )
		{
			if (Raw)
			{
				value *= Scale;
				value = InputRange.Excludes( sourceRange, value ) ? 0.0f : value;
			}
			else
			{
				// Scale value and clamp to a legal range.
				value = Mathf.Clamp( value * Scale, -1.0f, 1.0f );

				// Remap from source range to target range.
				value = InputRange.Remap( value, sourceRange, targetRange );
			}

			if (Invert)
			{
				value = -value;
			}

			return value;
		}
	}
}
