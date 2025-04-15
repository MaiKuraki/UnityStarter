namespace InControl
{
	using System;
	using UnityEngine;


	public delegate Vector2 DeadZoneFunc( float x, float y, float lowerDeadZone, float upperDeadZone );


	// All calculations (other than Sqrt) is inlined as much as possible in these functions for max performance.
	// These functions tend to be called a lot, so it needs to be fast.
	//
	public static class DeadZone
	{
		/// <summary>
		/// Apply deadzones to a vector, but processing the x and y components separately.
		/// The resulting vector is not normalized, so the result falls within a rectangle.
		/// This is not usually the kind of deadzone you want and may give undesired results.
		/// </summary>
		/// <param name="x">The x component of the vector to process.</param>
		/// <param name="y">The y component of the vector to process.</param>
		/// <param name="lowerDeadZone">The lower dead zone to apply (e.g. 0.2f)</param>
		/// <param name="upperDeadZone">The upper dead zone to apply (e.g. 0.9f)</param>
		public static Vector2 SeparateNotNormalized( float x, float y, float lowerDeadZone, float upperDeadZone )
		{
			var deltaDeadZone = upperDeadZone - lowerDeadZone;

			float vx;
			if (x < 0.0f)
			{
				if (x > -lowerDeadZone) vx = 0.0f;
				else if (x < -upperDeadZone) vx = -1.0f;
				else vx = (x + lowerDeadZone) / deltaDeadZone;
			}
			else
			{
				if (x < lowerDeadZone) vx = 0.0f;
				else if (x > upperDeadZone) vx = 1.0f;
				else vx = (x - lowerDeadZone) / deltaDeadZone;
			}

			float vy;
			if (y < 0.0f)
			{
				if (y > -lowerDeadZone) vy = 0.0f;
				else if (y < -upperDeadZone) vy = -1.0f;
				else vy = (y + lowerDeadZone) / deltaDeadZone;
			}
			else
			{
				if (y < lowerDeadZone) vy = 0.0f;
				else if (y > upperDeadZone) vy = 1.0f;
				else vy = (y - lowerDeadZone) / deltaDeadZone;
			}

			return new Vector2( vx, vy );
		}


		/// <summary>
		/// Same as SeparateNotNormalized, but the resulting vector is normalized so the result lands on 
		/// the edge of a circle with a radius of 1.0f.
		/// This is useful for d-pad controls where the input snaps to 8 directions, making the result 
		/// interchangable with the output for analog sticks.
		/// </summary>
		/// <param name="x">The x component of the vector to process.</param>
		/// <param name="y">The y component of the vector to process.</param>
		/// <param name="lowerDeadZone">The lower dead zone to apply (e.g. 0.2f)</param>
		/// <param name="upperDeadZone">The upper dead zone to apply (e.g. 0.9f)</param>
		public static Vector2 Separate( float x, float y, float lowerDeadZone, float upperDeadZone )
		{
			var deltaDeadZone = upperDeadZone - lowerDeadZone;

			float vx;
			if (x < 0.0f)
			{
				if (x > -lowerDeadZone) vx = 0.0f;
				else if (x < -upperDeadZone) vx = -1.0f;
				else vx = (x + lowerDeadZone) / deltaDeadZone;
			}
			else
			{
				if (x < lowerDeadZone) vx = 0.0f;
				else if (x > upperDeadZone) vx = 1.0f;
				else vx = (x - lowerDeadZone) / deltaDeadZone;
			}

			float vy;
			if (y < 0.0f)
			{
				if (y > -lowerDeadZone) vy = 0.0f;
				else if (y < -upperDeadZone) vy = -1.0f;
				else vy = (y + lowerDeadZone) / deltaDeadZone;
			}
			else
			{
				if (y < lowerDeadZone) vy = 0.0f;
				else if (y > upperDeadZone) vy = 1.0f;
				else vy = (y - lowerDeadZone) / deltaDeadZone;
			}

			var magnitude = (float) Math.Sqrt( vx * vx + vy * vy );
			if (magnitude < 1E-05f)
			{
				return Vector2.zero;
			}

			return new Vector2( vx / magnitude, vy / magnitude );
		}


		/// <summary>
		/// Apply deadzones to a vector, but processes the deadzones on the vector as radii on a circle.
		/// This works well for analog sticks as they need to operate within a minimum and maximum radius.
		/// </summary>
		/// <param name="x">The x component of the vector to process.</param>
		/// <param name="y">The y component of the vector to process.</param>
		/// <param name="lowerDeadZone">The lower dead zone to apply (e.g. 0.2f)</param>
		/// <param name="upperDeadZone">The upper dead zone to apply (e.g. 0.9f)</param>
		public static Vector2 Circular( float x, float y, float lowerDeadZone, float upperDeadZone )
		{
			var magnitude = (float) Math.Sqrt( x * x + y * y );
			if (magnitude < lowerDeadZone || magnitude < 1E-05f)
			{
				return Vector2.zero;
			}

			var normal = new Vector2( x / magnitude, y / magnitude );
			if (magnitude > upperDeadZone)
			{
				return normal;
			}

			return normal * ((magnitude - lowerDeadZone) / (upperDeadZone - lowerDeadZone));
		}
	}
}
