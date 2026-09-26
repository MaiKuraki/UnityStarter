using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.Localization.Core;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization
{
    /// <summary>
    /// Creates transactional, window-scoped localization component bindings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Construction, binding, and disposal are confined to the Unity main thread. Component
    /// discovery is performed once for each window instance; locale changes do not rescan the
    /// hierarchy.
    /// </para>
    /// <para>
    /// The binder does not require the localization service to be initialized. A window opened
    /// before initialization completes is bound as soon as the service reports
    /// <see cref="LocalizationChangeReason.Initialized"/>, which lets composition roots register
    /// the binder unconditionally instead of sequencing it behind catalog loading.
    /// </para>
    /// </remarks>
    public sealed class LocalizationWindowBinder : IUIWindowBinder
    {
        private const int InitialBehaviourCapacity = 16;
        private const int InitialTargetCapacity = 8;
        private const int MaxRetainedBehaviourCapacity = 256;

        private readonly LocalizationBindingContext _localizationContext;
        private List<MonoBehaviour> _behaviourScratch;
        private readonly int _ownerThreadId;

        public LocalizationWindowBinder(ILocalizationProvider service)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    "LocalizationWindowBinder must be created on the Unity main thread.");
            }

            _localizationContext = new LocalizationBindingContext(
                service ?? throw new ArgumentNullException(nameof(service)));
            _behaviourScratch = new List<MonoBehaviour>(InitialBehaviourCapacity);
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public IUIWindowBinding Bind(UIWindowBindingContext context)
        {
            EnsureOwnerThread();

            List<MonoBehaviour> behaviours = _behaviourScratch;
            behaviours.Clear();
            try
            {
                context.Window.GetComponentsInChildren(true, behaviours);
                return new LocalizationWindowBinding(
                    in _localizationContext,
                    behaviours,
                    _ownerThreadId,
                    context.Window.name);
            }
            finally
            {
                behaviours.Clear();
                if (ReferenceEquals(_behaviourScratch, behaviours) &&
                    behaviours.Capacity > MaxRetainedBehaviourCapacity)
                {
                    // Do not let one atypically large hierarchy pin its scan buffer for the
                    // composition root's full lifetime.
                    _behaviourScratch = new List<MonoBehaviour>(InitialBehaviourCapacity);
                }
            }
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "LocalizationWindowBinder is confined to its Unity main-thread owner.");
            }
        }

        private sealed class LocalizationWindowBinding : IUIWindowBinding
        {
            private readonly List<ILocalizationBindingTarget> _targets;
            private readonly LocalizationBindingContext _localizationContext;
            private readonly int _ownerThreadId;
            private readonly string _windowName;
            private bool _isDisposed;
            private bool _isBound;
            private bool _isSubscribed;

            public LocalizationWindowBinding(
                in LocalizationBindingContext localizationContext,
                List<MonoBehaviour> behaviours,
                int ownerThreadId,
                string windowName)
            {
                _ownerThreadId = ownerThreadId;
                _localizationContext = localizationContext;
                _windowName = windowName;
                _targets = new List<ILocalizationBindingTarget>(InitialTargetCapacity);

                for (int i = 0; i < behaviours.Count; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour is ILocalizationBindingTarget target)
                    {
                        _targets.Add(target);
                    }
                }

                if (localizationContext.Localization.IsInitialized)
                {
                    BindTargets();
                    return;
                }

                // The window still opens; it simply shows authored text until the service
                // finishes loading. Failing the open here would make window availability depend
                // on catalog load order, and skipping targets outright would leave the window
                // permanently untranslated with no later hook to correct it.
                UIFrameworkLocalizationLog.Channel.Warning(
                    "Localization service is not initialized. Window '" + _windowName
                    + "' defers localization binding until initialization completes.");

                localizationContext.Localization.Changed += OnLocalizationChanged;
                _isSubscribed = true;
            }

            public void OnWindowStateChanged(WindowStateCallbackType state)
            {
            }

            public void Dispose()
            {
                EnsureOwnerThread();
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                Unsubscribe();

                Exception failure = _isBound ? UnbindReverse(_targets.Count - 1) : null;
                _isBound = false;
                _targets.Clear();
                if (failure != null)
                {
                    throw failure;
                }
            }

            private void OnLocalizationChanged(LocalizationChange change)
            {
                if (_isDisposed || _isBound)
                {
                    return;
                }

                if (change.Reason != LocalizationChangeReason.Initialized &&
                    change.Reason != LocalizationChangeReason.LocaleChanged &&
                    change.Reason != LocalizationChangeReason.ContentChanged)
                {
                    return;
                }

                if (!_localizationContext.Localization.IsInitialized)
                {
                    return;
                }

                // Binding mutates Unity components, so it stays on the thread that owns this
                // binding. A service initialized elsewhere leaves the window pending rather than
                // corrupting component state from a foreign thread.
                if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
                {
                    UIFrameworkLocalizationLog.Channel.Error(
                        "Localization service was initialized off the binding owner thread. Window '"
                        + _windowName + "' stays unlocalized.");
                    return;
                }

                Unsubscribe();
                BindTargets();
            }

            private void BindTargets()
            {
                if (_isBound)
                {
                    return;
                }

                int attemptedCount = 0;
                try
                {
                    for (; attemptedCount < _targets.Count; attemptedCount++)
                    {
                        _targets[attemptedCount].Bind(in _localizationContext);
                    }
                }
                catch (Exception bindingException)
                {
                    // Include the target whose Bind call failed so partially acquired state can
                    // still be released. Unbind implementations are required to be idempotent.
                    int rollbackStart = Math.Min(attemptedCount, _targets.Count - 1);
                    Exception rollbackException = UnbindReverse(rollbackStart);
                    _targets.Clear();
                    _isDisposed = true;
                    Unsubscribe();

                    if (rollbackException != null)
                    {
                        throw new AggregateException(
                            "Localization component binding and rollback both failed.",
                            bindingException,
                            rollbackException);
                    }

                    throw;
                }

                _isBound = true;
            }

            private void Unsubscribe()
            {
                if (!_isSubscribed)
                {
                    return;
                }

                _localizationContext.Localization.Changed -= OnLocalizationChanged;
                _isSubscribed = false;
            }

            private Exception UnbindReverse(int startIndex)
            {
                List<Exception> failures = null;
                for (int i = startIndex; i >= 0; i--)
                {
                    ILocalizationBindingTarget target = _targets[i];
                    if (target == null ||
                        (target is UnityEngine.Object unityObject && unityObject == null))
                    {
                        continue;
                    }

                    try
                    {
                        target.Unbind();
                    }
                    catch (Exception exception)
                    {
                        failures ??= new List<Exception>(2);
                        failures.Add(exception);
                    }
                }

                if (failures == null)
                {
                    return null;
                }

                return failures.Count == 1
                    ? failures[0]
                    : new AggregateException(
                        "Multiple localization binding targets failed to unbind.",
                        failures);
            }

            private void EnsureOwnerThread()
            {
                if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
                {
                    throw new InvalidOperationException(
                        "Localization window bindings are confined to the Unity main thread.");
                }
            }
        }
    }
}
