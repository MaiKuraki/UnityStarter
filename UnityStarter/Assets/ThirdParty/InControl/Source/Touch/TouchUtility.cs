namespace InControl
{
	using UnityEngine;


	public static class TouchUtility
	{
		public static Vector2 AnchorToViewPoint( TouchControlAnchor touchControlAnchor )
		{
			switch (touchControlAnchor)
			{
				case TouchControlAnchor.TopLeft:
					return new Vector2( 0.0f, 1.0f );
				case TouchControlAnchor.CenterLeft:
					return new Vector2( 0.0f, 0.5f );
				case TouchControlAnchor.BottomLeft:
					return new Vector2( 0.0f, 0.0f );

				case TouchControlAnchor.TopCenter:
					return new Vector2( 0.5f, 1.0f );
				case TouchControlAnchor.Center:
					return new Vector2( 0.5f, 0.5f );
				case TouchControlAnchor.BottomCenter:
					return new Vector2( 0.5f, 0.0f );

				case TouchControlAnchor.TopRight:
					return new Vector2( 1.0f, 1.0f );
				case TouchControlAnchor.CenterRight:
					return new Vector2( 1.0f, 0.5f );
				case TouchControlAnchor.BottomRight:
					return new Vector2( 1.0f, 0.0f );
			}

			return Vector2.zero;
		}


		public static Vector2 RoundVector( Vector2 vector )
		{
			return new Vector2( Mathf.Round( vector.x ), Mathf.Round( vector.y ) );
		}
	}
}
