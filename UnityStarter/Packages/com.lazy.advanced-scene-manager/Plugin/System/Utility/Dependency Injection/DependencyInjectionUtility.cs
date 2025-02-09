using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AdvancedSceneManager.DependencyInjection
{

    /// <summary>Contains utility methods for depdency injection.</summary>
    public static partial class DependencyInjectionUtility
    {

        /// <summary>Base interface for all injectable services.</summary>
        /// <remarks>Injectable classes implementing an interface based on <see cref="IInjectable"/> is automatically registered in <see cref="DependencyInjectionUtility"/> <see langword="static"/> constructor.</remarks>
        public interface IInjectable
        { }

        readonly static List<(Type interfaceT, IInjectable implementation)> services = new()
        {

            { (typeof(IApp), SceneManager.app) },
            { (typeof(IAssetsProvider), SceneManager.assets) },
            { (typeof(IProfileManager), ProfileManagerService.instance) },
            { (typeof(IProjectSettings), SceneManager.settings.project) },
            { (typeof(ISceneManager), SceneManager.runtime) },

        #if UNITY_EDITOR
            { (typeof(Editor.IPackage), SceneManager.package) },
            { (typeof(Editor.IBuildManager), BuildService.instance) },
            { (typeof(Editor.IHierarchyGUI), HierarchyGUIService.instance) },
            { (typeof(Editor.IUserSettings), SceneManager.settings.user) },
            { (typeof(Editor.ISceneManagerWindow), SceneManagerWindowService.instance) },
        #endif

        };

        #region Enumerate

        public static IEnumerable<(Type interfaceT, IInjectable implementation)> EnumerateServices() =>
            services;

        #endregion
        #region Get

        public static T GetService<T>() where T : IInjectable =>
            (T)GetService(typeof(T));

        public static IInjectable GetService(Type type) =>
            services.LastOrDefault(s => s.interfaceT == type || s.implementation.GetType() == type).implementation;

        public static IEnumerable<T> GetServices<T>() where T : IInjectable =>
            services.Where(s => s.interfaceT == typeof(T)).Select(s => (T)s.implementation);

        #endregion
        #region Add

        internal static void Add<TInterface, TImplementation>(TImplementation obj) where TInterface : IInjectable where TImplementation : TInterface =>
            services.Add((typeof(TInterface), obj));

        internal static void Add<TInterface>(TInterface obj) where TInterface : IInjectable =>
            services.Add((typeof(TInterface), obj));

        internal static void Add<TInterface, TImplementation>() where TInterface : IInjectable where TImplementation : TInterface =>
            services.Add((typeof(TInterface), Construct<TImplementation>()));

        internal static void Add(Type interfaceT, IInjectable obj) =>
            services.Add((interfaceT, obj));

        #endregion
        #region Remove

        public static void Remove<T>(Type type, T service) where T : IInjectable =>
            services.Remove((type, service));

        #endregion
        #region Construct

        /// <summary>Constructs <typeparamref name="T"/>, injecting services as necessary.</summary>
        /// <remarks>Returns <see langword="false"/> if not all constructor parameters could be injected.</remarks>
        internal static T Construct<T>()
        {

            try
            {

                var l = new List<IInjectable>();

                var constructor = typeof(T).GetConstructors().FirstOrDefault();
                if (constructor is not null)
                    foreach (var param in constructor.GetParameters())
                        l.Add(GetService(param.ParameterType) ?? throw new ArgumentException($"Cannot inject '{param.ParameterType.Name}' into '{typeof(T).Name}'."));

                return (T)Activator.CreateInstance(typeof(T), l.ToArray());

            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                return default;
            }

        }
    }

    #endregion

}
