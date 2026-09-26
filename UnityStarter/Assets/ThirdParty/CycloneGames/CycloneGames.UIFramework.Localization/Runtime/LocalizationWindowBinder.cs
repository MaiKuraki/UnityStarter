using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Runtime;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization
{
    /// <summary>
    /// Creates transactional, window-scoped localization component bindings.
    /// </summary>
    /// <remarks>
    /// Construction, binding, and disposal are confined to the Unity main thread. Component
    /// discovery is performed once for each window instance; locale changes do not rescan the
    /// hierarchy.
    /// </remarks>
    public sealed class LocalizationWindowBinder : IUIWindowBinder
    {
        private const int InitialBehaviourCapacity = 16;
        private const int InitialTargetCapacity = 8;
        private const int MaxRetainedBehaviourCapacity = 256;

        private readonly LocalizationBindingContext _localizationContext;
        private List<MonoBehaviour> _behaviourScratch;
        private readonly int _ownerThreadId;

        public LocalizationWindowBinder(
            ILocalizationService service,
            IAssetPackage assetPackage = null)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    "LocalizationWindowBinder must be created on the Unity main thread.");
            }

            _localizationContext = new LocalizationBindingContext(
                service ?? throw new ArgumentNullException(nameof(service)),
                assetPackage);
            _behaviourScratch = new List<MonoBehaviour>(InitialBehaviourCapacity);
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public IUIWindowBinding Bind(UIWindowBindingContext context)
        {
            EnsureOwnerThread();
            if (!_localizationContext.Localization.IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the localization service before binding UI windows.");
            }

            List<MonoBehaviour> behaviours = _behaviourScratch;
            behaviours.Clear();
            try
            {
                context.Window.GetComponentsInChildren(true, behaviours);
                return new LocalizationWindowBinding(
                    in _localizationContext,
                    behaviours,
                    _ownerThreadId);
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
            private readonly int _ownerThreadId;
            private bool _isDisposed;

            public LocalizationWindowBinding(
                in LocalizationBindingContext localizationContext,
                List<MonoBehaviour> behaviours,
                int ownerThreadId)
            {
                _ownerThreadId = ownerThreadId;
                _targets = new List<ILocalizationBindingTarget>(InitialTargetCapacity);

                for (int i = 0; i < behaviours.Count; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour is ILocalizationBindingTarget target)
                    {
                        _targets.Add(target);
                    }
                }

                int attemptedCount = 0;
                try
                {
                    for (; attemptedCount < _targets.Count; attemptedCount++)
                    {
                        _targets[attemptedCount].Bind(in localizationContext);
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

                    if (rollbackException != null)
                    {
                        throw new AggregateException(
                            "Localization component binding and rollback both failed.",
                            bindingException,
                            rollbackException);
                    }

                    throw;
                }
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
                Exception failure = UnbindReverse(_targets.Count - 1);
                _targets.Clear();
                if (failure != null)
                {
                    throw failure;
                }
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
