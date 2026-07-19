namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Marker interface for direct, strongly typed communication from a view to its presenter.
    /// Implement this on a presenter and let the view retain the interface reference while bound.
    /// 
    /// Usage:
    /// public interface ILoginViewListener : IUIViewListener {
    ///     void OnClickLogin();
    /// }
    /// 
    /// public class LoginPresenter : UIPresenter<ILoginView>, ILoginViewListener {
    ///     protected override void OnViewBound() {
    ///         View.SetListener(this);
    ///     }
    ///     public void OnClickLogin() { /* handle click */ }
    /// }
    /// 
    /// public class LoginWindow : UIWindow, ILoginView {
    ///     private ILoginViewListener _listener;
    ///     public void SetListener(ILoginViewListener listener) => _listener = listener;
    ///     
    ///     // Bound directly in Inspector (UnityEvent without lambda)
    ///     public void UICmd_ClickLogin() => _listener?.OnClickLogin();
    /// }
    /// </summary>
    public interface IUIViewListener
    {
    }
}
