namespace InControl
{
	public interface BindingSourceListener
	{
		void Reset();
		BindingSource Listen( BindingListenOptions listenOptions, InputDevice device );
	}
}
