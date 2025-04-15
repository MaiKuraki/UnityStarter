namespace InControl
{
	using System;
	using UnityEngine;


	public enum TouchSpriteShape : int
	{
		Oval,
		Rectangle
	}


	[Serializable]
	public class TouchSprite
	{
		[SerializeField]
		Sprite idleSprite;

		[SerializeField]
		Sprite busySprite;

		[SerializeField]
		Color idleColor = new Color( 1.0f, 1.0f, 1.0f, 0.5f );

		[SerializeField]
		Color busyColor = new Color( 1.0f, 1.0f, 1.0f, 1.0f );

		[SerializeField]
		TouchSpriteShape shape = TouchSpriteShape.Oval;

		[SerializeField]
		TouchUnitType sizeUnitType = TouchUnitType.Percent;

		[SerializeField]
		Vector2 size = new Vector2( 10.0f, 10.0f );

		[SerializeField]
		bool lockAspectRatio = true;

		[SerializeField, HideInInspector]
		Vector2 worldSize;

		GameObject spriteGameObject;
		SpriteRenderer spriteRenderer;
		bool state;

		public bool Dirty { get; set; }
		public bool Ready { get; set; }


		public TouchSprite() {}


		public TouchSprite( float size )
		{
			this.size = Vector2.one * size;
		}


		public void Create( string gameObjectName, Transform parentTransform, int sortingOrder )
		{
			spriteGameObject = CreateSpriteGameObject( gameObjectName, parentTransform );
			spriteRenderer = CreateSpriteRenderer( spriteGameObject, idleSprite, sortingOrder );
			spriteRenderer.color = idleColor;
			Ready = true;
		}


		public void Delete()
		{
			Ready = false;
			GameObject.Destroy( spriteGameObject );
		}


		public void Update()
		{
			Update( false );
		}


		public void Update( bool forceUpdate )
		{
			if (Dirty || forceUpdate)
			{
				if (spriteRenderer != null)
				{
					spriteRenderer.sprite = State ? busySprite : idleSprite;
				}

				if (sizeUnitType == TouchUnitType.Pixels)
				{
					var pixelSize = TouchUtility.RoundVector( size );
					ScaleSpriteInPixels( spriteGameObject, spriteRenderer, pixelSize );
					worldSize = pixelSize * TouchManager.PixelToWorld;
				}
				else
				{
					ScaleSpriteInPercent( spriteGameObject, spriteRenderer, size );
					if (lockAspectRatio)
					{
						worldSize = size * TouchManager.PercentToWorld;
					}
					else
					{
						worldSize = Vector2.Scale( size, TouchManager.ViewSize );
					}
				}

				Dirty = false;
			}

			if (spriteRenderer != null)
			{
				var color = State ? busyColor : idleColor;
				if (spriteRenderer.color != color)
				{
					spriteRenderer.color = Utility.MoveColorTowards( spriteRenderer.color, color, 5.0f * Time.unscaledDeltaTime );
				}
			}
		}


		GameObject CreateSpriteGameObject( string name, Transform parentTransform )
		{
			var spriteGameObject = new GameObject( name );
			spriteGameObject.transform.parent = parentTransform;
			spriteGameObject.transform.localPosition = Vector3.zero;
			spriteGameObject.transform.localScale = Vector3.one;
			spriteGameObject.layer = parentTransform.gameObject.layer;
			return spriteGameObject;
		}


		static Shader spriteRendererShader;
		static Material spriteRendererMaterial;
		static int spriteRendererPixelSnapId;


		SpriteRenderer CreateSpriteRenderer( GameObject spriteGameObject, Sprite sprite, int sortingOrder )
		{
			if (!spriteRendererMaterial)
			{
				spriteRendererShader = Shader.Find( "Sprites/Default" );
				spriteRendererMaterial = new Material( spriteRendererShader );
				spriteRendererPixelSnapId = Shader.PropertyToID( "PixelSnap" );
			}

			var spriteRenderer = spriteGameObject.AddComponent<SpriteRenderer>();
			spriteRenderer.sprite = sprite;
			spriteRenderer.sortingOrder = sortingOrder;
			spriteRenderer.sharedMaterial = spriteRendererMaterial;
			spriteRenderer.sharedMaterial.SetFloat( spriteRendererPixelSnapId, 1.0f );
			return spriteRenderer;
		}


		void ScaleSpriteInPixels( GameObject spriteGameObject, SpriteRenderer spriteRenderer, Vector2 size )
		{
			if (spriteGameObject == null || spriteRenderer == null || spriteRenderer.sprite == null)
			{
				return;
			}

			var pixelsToUnits = spriteRenderer.sprite.rect.width / spriteRenderer.sprite.bounds.size.x;
			var scale = TouchManager.PixelToWorld * pixelsToUnits;
			var scaleX = scale * size.x / spriteRenderer.sprite.rect.width;
			var scaleY = scale * size.y / spriteRenderer.sprite.rect.height;

			spriteGameObject.transform.localScale = new Vector3( scaleX, scaleY );
		}


		void ScaleSpriteInPercent( GameObject spriteGameObject, SpriteRenderer spriteRenderer, Vector2 size )
		{
			if (spriteGameObject == null || spriteRenderer == null || spriteRenderer.sprite == null)
			{
				return;
			}

			if (lockAspectRatio)
			{
				var scale = Mathf.Min( TouchManager.ViewSize.x, TouchManager.ViewSize.y );
				var scaleX = scale * size.x / spriteRenderer.sprite.bounds.size.x;
				var scaleY = scale * size.y / spriteRenderer.sprite.bounds.size.y;
				spriteGameObject.transform.localScale = new Vector3( scaleX, scaleY );
			}
			else
			{
				var scaleX = TouchManager.ViewSize.x * size.x / spriteRenderer.sprite.bounds.size.x;
				var scaleY = TouchManager.ViewSize.y * size.y / spriteRenderer.sprite.bounds.size.y;
				spriteGameObject.transform.localScale = new Vector3( scaleX, scaleY );
			}
		}


		public bool Contains( Vector2 testWorldPoint )
		{
			if (shape == TouchSpriteShape.Oval)
			{
				var dx = (testWorldPoint.x - Position.x) / worldSize.x;
				var dy = (testWorldPoint.y - Position.y) / worldSize.y;
				return (dx * dx) + (dy * dy) < 0.25f;
			}
			else
			{
				var dx = Utility.Abs( testWorldPoint.x - Position.x ) * 2.0f;
				var dy = Utility.Abs( testWorldPoint.y - Position.y ) * 2.0f;
				return dx <= worldSize.x && dy <= worldSize.y;
			}
		}


		public bool Contains( Touch touch )
		{
			return Contains( TouchManager.ScreenToWorldPoint( touch.position ) );
		}


		public void DrawGizmos( Vector3 position, Color color )
		{
			if (shape == TouchSpriteShape.Oval)
			{
				Utility.DrawOvalGizmo( position, WorldSize, color );
			}
			else
			{
				Utility.DrawRectGizmo( position, WorldSize, color );
			}
		}


		public bool State
		{
			get
			{
				return state;
			}

			set
			{
				if (state != value)
				{
					state = value;
					Dirty = true;
				}
			}
		}


		public Sprite BusySprite
		{
			get
			{
				return busySprite;
			}

			set
			{
				if (busySprite != value)
				{
					busySprite = value;
					Dirty = true;
				}
			}
		}


		public Sprite IdleSprite
		{
			get
			{
				return idleSprite;
			}

			set
			{
				if (idleSprite != value)
				{
					idleSprite = value;
					Dirty = true;
				}
			}
		}


		public Sprite Sprite
		{
			set
			{
				if (idleSprite != value)
				{
					idleSprite = value;
					Dirty = true;
				}

				if (busySprite != value)
				{
					busySprite = value;
					Dirty = true;
				}
			}
		}


		public Color BusyColor
		{
			get
			{
				return busyColor;
			}

			set
			{
				if (busyColor != value)
				{
					busyColor = value;
					Dirty = true;
				}
			}
		}


		public Color IdleColor
		{
			get
			{
				return idleColor;
			}

			set
			{
				if (idleColor != value)
				{
					idleColor = value;
					Dirty = true;
				}
			}
		}


		public TouchSpriteShape Shape
		{
			get
			{
				return shape;
			}

			set
			{
				if (shape != value)
				{
					shape = value;
					Dirty = true;
				}
			}
		}


		public TouchUnitType SizeUnitType
		{
			get
			{
				return sizeUnitType;
			}

			set
			{
				if (sizeUnitType != value)
				{
					sizeUnitType = value;
					Dirty = true;
				}
			}
		}


		public Vector2 Size
		{
			get
			{
				return size;
			}

			set
			{
				if (size != value)
				{
					size = value;
					Dirty = true;
				}
			}
		}


		public Vector2 WorldSize
		{
			get
			{
				return worldSize;
			}
		}


		public Vector3 Position
		{
			get
			{
				return spriteGameObject ? spriteGameObject.transform.position : Vector3.zero;
			}

			set
			{
				if (spriteGameObject)
				{
					spriteGameObject.transform.position = value;
				}
			}
		}
	}
}
