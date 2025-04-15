namespace InControl
{
	using System;


	public class PlayerTwoAxisAction : TwoAxisInputControl
	{
		PlayerAction negativeXAction;
		PlayerAction positiveXAction;
		PlayerAction negativeYAction;
		PlayerAction positiveYAction;

		/// <summary>
		/// Gets or sets a value indicating whether the X axis should be inverted for
		/// this action. When false (default), the X axis will be positive up,
		/// the same as Unity.
		/// </summary>
		public bool InvertXAxis { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the Y axis should be inverted for
		/// this action. When false (default), the Y axis will be positive up,
		/// the same as Unity.
		/// </summary>
		public bool InvertYAxis { get; set; }

		/// <summary>
		/// The binding source type that last provided input to this action set.
		/// </summary>
		public BindingSourceType LastInputType = BindingSourceType.None;

		/// <summary>
		/// Occurs when the binding source type that last provided input to this action set changes.
		/// </summary>
		public event Action<BindingSourceType> OnLastInputTypeChanged;

		/// <summary>
		/// This property can be used to store whatever arbitrary game data you want on this action.
		/// </summary>
		public object UserData { get; set; }


		internal PlayerTwoAxisAction( PlayerAction negativeXAction, PlayerAction positiveXAction, PlayerAction negativeYAction, PlayerAction positiveYAction )
		{
			this.negativeXAction = negativeXAction;
			this.positiveXAction = positiveXAction;
			this.negativeYAction = negativeYAction;
			this.positiveYAction = positiveYAction;

			InvertXAxis = false;
			InvertYAxis = false;
			Raw = true;
		}


		internal void Update( ulong updateTick, float deltaTime )
		{
			ProcessActionUpdate( negativeXAction );
			ProcessActionUpdate( positiveXAction );
			ProcessActionUpdate( negativeYAction );
			ProcessActionUpdate( positiveYAction );

			var x = Utility.ValueFromSides( negativeXAction, positiveXAction, InvertXAxis );
			var y = Utility.ValueFromSides( negativeYAction, positiveYAction, InputManager.InvertYAxis || InvertYAxis );
			UpdateWithAxes( x, y, updateTick, deltaTime );
		}


		void ProcessActionUpdate( PlayerAction action )
		{
			var lastInputType = LastInputType;

			if (action.UpdateTick > UpdateTick)
			{
				UpdateTick = action.UpdateTick;
				lastInputType = action.LastInputType;
			}

			if (LastInputType != lastInputType)
			{
				LastInputType = lastInputType;
				if (OnLastInputTypeChanged != null)
				{
					OnLastInputTypeChanged.Invoke( lastInputType );
				}
			}
		}


		[Obsolete( "Please set this property on device controls directly. It does nothing here." )]
		public new float LowerDeadZone
		{
			get
			{
				return 0.0f;
			}

			set
			{
				#pragma warning disable 0168, 0219
				var dummy = value;
				#pragma warning restore 0168, 0219
			}
		}


		[Obsolete( "Please set this property on device controls directly. It does nothing here." )]
		public new float UpperDeadZone
		{
			get
			{
				return 0.0f;
			}

			set
			{
				#pragma warning disable 0168, 0219
				var dummy = value;
				#pragma warning restore 0168, 0219
			}
		}
	}
}
