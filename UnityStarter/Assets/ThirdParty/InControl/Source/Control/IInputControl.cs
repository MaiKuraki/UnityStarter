namespace InControl
{
	// TODO: This interface is probably not necessary and can be removed at some point.
	public interface IInputControl
	{
		// TODO: Maybe add HasInput?
		bool HasChanged { get; }
		bool IsPressed { get; }
		bool WasPressed { get; }
		bool WasReleased { get; }
		void ClearInputState();
	}
}
