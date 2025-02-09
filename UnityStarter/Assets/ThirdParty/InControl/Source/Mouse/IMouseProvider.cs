namespace InControl
{
	using UnityEngine;


	public interface IMouseProvider
	{
		void Setup();
		void Reset();
		void Update();
		Vector2 GetPosition();
		float GetDeltaX();
		float GetDeltaY();
		float GetDeltaScroll();
		bool GetButtonIsPressed( Mouse control );
		bool GetButtonWasPressed( Mouse control );
		bool GetButtonWasReleased( Mouse control );
		bool HasMousePresent();
	}
}
