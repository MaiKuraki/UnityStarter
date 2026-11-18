using System;

namespace CycloneGames.InputSystem.Runtime.Integrations.VContainer
{
    /// <summary>
    /// Immutable composition-root policy for the VContainer integration.
    /// The integration never discovers configuration sources, storage roots, startup
    /// timing, or failure handling on its own; every decision is supplied here.
    /// </summary>
    public sealed class InputSystemVContainerOptions
    {
        public InputSystemBootstrapOptions Bootstrap { get; }
        public bool RegisterEntryPoint { get; }
        public bool ThrowOnStartupFailure { get; }
        public bool AllowRuntimeConfigurationUpdates { get; }

        public InputSystemVContainerOptions(
            InputSystemBootstrapOptions bootstrap,
            bool registerEntryPoint = false,
            bool throwOnStartupFailure = false,
            bool allowRuntimeConfigurationUpdates = true)
        {
            if (bootstrap == null)
            {
                throw new ArgumentNullException(nameof(bootstrap));
            }

            if (throwOnStartupFailure && !registerEntryPoint)
            {
                throw new ArgumentException(
                    "Startup failure escalation requires an explicitly registered entry point.",
                    nameof(throwOnStartupFailure));
            }

            Bootstrap = bootstrap;
            RegisterEntryPoint = registerEntryPoint;
            ThrowOnStartupFailure = throwOnStartupFailure;
            AllowRuntimeConfigurationUpdates = allowRuntimeConfigurationUpdates;
        }

        /// <summary>
        /// Pure decision: an entry point escalates a failed bootstrap only when the
        /// composition root explicitly opted in. All other failures stay observable
        /// through the diagnostics contract.
        /// </summary>
        public bool ShouldEscalateStartupFailure(InputSystemLoadResult result)
        {
            return RegisterEntryPoint
                   && ThrowOnStartupFailure
                   && !result.IsBootstrapComplete;
        }

        /// <summary>
        /// Pure registration plan. Kept framework-free so it can be validated outside Unity.
        /// </summary>
        public InputSystemRegistrationPlan GetRegistrationPlan()
        {
            return new InputSystemRegistrationPlan(
                registerEntryPoint: RegisterEntryPoint,
                usesProvidedUserStore: Bootstrap.UserStore != null,
                mode: Bootstrap.Mode,
                persistDefaultToUser: Bootstrap.PersistDefaultToUser,
                allowRuntimeConfigurationUpdates: AllowRuntimeConfigurationUpdates,
                implicitFileIo: false,
                implicitUnityGlobalReads: false);
        }
    }

    /// <summary>
    /// Snapshot of what the installer will register and which side effects are possible.
    /// </summary>
    public readonly struct InputSystemRegistrationPlan
    {
        public bool RegisterEntryPoint { get; }
        public bool UsesProvidedUserStore { get; }
        public InputSystemBootstrapMode Mode { get; }
        public bool PersistDefaultToUser { get; }
        public bool AllowRuntimeConfigurationUpdates { get; }
        public bool ImplicitFileIo { get; }
        public bool ImplicitUnityGlobalReads { get; }

        public InputSystemRegistrationPlan(
            bool registerEntryPoint,
            bool usesProvidedUserStore,
            InputSystemBootstrapMode mode,
            bool persistDefaultToUser,
            bool allowRuntimeConfigurationUpdates,
            bool implicitFileIo,
            bool implicitUnityGlobalReads)
        {
            RegisterEntryPoint = registerEntryPoint;
            UsesProvidedUserStore = usesProvidedUserStore;
            Mode = mode;
            PersistDefaultToUser = persistDefaultToUser;
            AllowRuntimeConfigurationUpdates = allowRuntimeConfigurationUpdates;
            ImplicitFileIo = implicitFileIo;
            ImplicitUnityGlobalReads = implicitUnityGlobalReads;
        }

        public bool HasImplicitSideEffects => ImplicitFileIo || ImplicitUnityGlobalReads;
    }
}
