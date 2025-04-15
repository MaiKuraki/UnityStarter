namespace InControl
{
	using UnityEngine;


	public interface IKeyboardProvider
	{
		void Setup();
		void Reset();
		void Update();
		bool AnyKeyIsPressed();
		bool GetKeyIsPressed( Key control );
		string GetNameForKey( Key control );
	}
}
