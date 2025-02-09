using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Utility
{

    /// <summary>Provides utility functions for searching ASM assets.</summary>
    public static class AssetSearchUtility
    {

        #region Auto list

        /// <summary>Finds the <typeparamref name="T"/> with the specified name.</summary>
        public static T Find<T>(string q) where T : ASMModel =>
            Find(SceneManager.assets.Enumerate<T>(), q);

        /// <summary>Finds the <typeparamref name="T"/> with the specified name.</summary>
        public static bool TryFind<T>(string q, out T result) where T : ASMModel =>
            TryFind(SceneManager.assets.Enumerate<T>(), q, out result);

        #endregion
        #region Array

        /// <inheritdoc cref="Find{T}(string)"/>
        public static T Find<T>(this T[] list, string q) where T : ASMModel =>
            Find((IEnumerable<T>)list, q);

        /// <inheritdoc cref="Find{T}(string)"/>
        public static bool TryFind<T>(this T[] list, string q, out T result) where T : ASMModel =>
            TryFind((IEnumerable<T>)list, q, out result);

        #endregion
        #region Enumerable

        /// <inheritdoc cref="Find{T}(string)"/>
        public static T Find<T>(this IEnumerable<T> list, string q) where T : ASMModel => 
            list?.FirstOrDefault(o => o != null && o.IsMatch(q));

        /// <inheritdoc cref="Find{T}(string)"/>      
        public static bool TryFind<T>(this IEnumerable<T> list, string q, out T result) where T : ASMModel
        {
            result = default;

            if (list == null)
                return false;

            result = list.FirstOrDefault(o => o != null && o.IsMatch(q));
            return result != null;
        }
        
        #endregion

    }

}
