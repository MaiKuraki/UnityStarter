namespace InControl
{
	using System;


	public class PlayerOneAxisAction : OneAxisInputControl
	{
		PlayerAction negativeAction;
		PlayerAction positiveAction;

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


		internal PlayerOneAxisAction( PlayerAction negativeAction, PlayerAction positiveAction )
		{
			this.negativeAction = negativeAction;
			this.positiveAction = positiveAction;
			Raw = true;
		}


		internal void Update( ulong updateTick, float deltaTime )
		{
			ProcessActionUpdate( negativeAction );
			ProcessActionUpdate( positiveAction );

			var value = Utility.ValueFromSides( negativeAction, positiveAction );
			CommitWithValue( value, updateTick, deltaTime );
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
