namespace InControl
{
	using System;
	using System.Collections.Generic;


	// using UnityEngine;


	public static class Reflector
	{
		static readonly string[] ignoreAssemblies =
		{
			"Unity",
			"UnityEngine",
			"UnityEditor",
			"mscorlib",
			"Microsoft",
			"System",
			"Mono",
			"JetBrains",
			"nunit",
			"ExCSS",
			"ICSharpCode",
			"AssetStoreTools",
		};


		static IEnumerable<Type> assemblyTypes;

		public static IEnumerable<Type> AllAssemblyTypes
		{
			get
			{
				return assemblyTypes ?? (assemblyTypes = GetAllAssemblyTypes());
			}
		}


		static bool IgnoreAssemblyWithName( string assemblyName )
		{
			foreach (var ignoreAssembly in ignoreAssemblies)
			{
				if (assemblyName.StartsWith( ignoreAssembly )) return true;
			}

			return false;
		}


		// TODO: Overall this is much better but see if it can be optimized further so
		// this doesn't cause a lot of GC and unnecessary work particularly in large
		// projects. Profile the Unity editor on script reloads.
		static IEnumerable<Type> GetAllAssemblyTypes()
		{
			var types = new List<Type>();
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (var assembly in assemblies)
			{
				var name = assembly.GetName().Name;
				if (IgnoreAssemblyWithName( name )) continue;

				// Logger.Log( "GetAllAssemblyTypes() considering: " + name );

				// Ugly hack to handle misversioned DLLs.
				Type[] innerTypes = null;
				try
				{
					innerTypes = assembly.GetTypes();
				}
				// ReSharper disable once EmptyGeneralCatchClause
				catch {}

				if (innerTypes != null)
				{
					types.AddRange( innerTypes );
				}
			}

			// Logger.Log( "GetAllAssemblyTypes().Count = " + types.Count );

			return types;
		}
	}
}
