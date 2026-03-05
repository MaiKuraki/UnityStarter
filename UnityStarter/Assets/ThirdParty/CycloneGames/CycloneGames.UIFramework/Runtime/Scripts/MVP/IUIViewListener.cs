namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Marker interface for zero-GC communication between View and Presenter.
    /// Implement this on your Presenter, and have your View maintain a reference to it.
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
