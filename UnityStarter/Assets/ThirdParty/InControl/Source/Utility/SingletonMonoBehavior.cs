namespace InControl
{
	#if UNITY_EDITOR
	using UnityEditor;
	#endif
	using System;
	using System.Linq;
	using UnityEngine;


	// ReSharper disable StaticMemberInGenericType
	public abstract class SingletonMonoBehavior<TComponent> : MonoBehaviour
		where TComponent : MonoBehaviour
	{
		static TComponent instance;
		static bool hasInstance;
		static int instanceId;
		static readonly object lockObject = new object();


		public static TComponent Instance
		{
			get
			{
				lock (lockObject)
				{
					if (hasInstance)
					{
						return instance;
					}

					instance = FindFirstInstance();
					if (instance == null)
					{
						throw new Exception( "The instance of singleton component " + typeof(TComponent) + " was requested, but it doesn't appear to exist in the scene." );
					}

					hasInstance = true;
					instanceId = instance.GetInstanceID();
					return instance;
				}
			}
		}


		/// <summary>
		/// Returns true if the object is NOT the singleton instance and should exit early from doing any redundant work.
		/// It will also log a warning if called from another instance in the editor during play mode.
		/// </summary>
		protected bool EnforceSingleton
		{
			get
			{
				if (GetInstanceID() == Instance.GetInstanceID())
				{
					return false;
				}

				if (Application.isPlaying)
				{
					enabled = false;
				}

				return true;
			}
		}


		/// <summary>
		/// Returns true if the object is the singleton instance.
		/// </summary>
		protected bool IsTheSingleton
		{
			get
			{
				lock (lockObject)
				{
					// We compare against the last known instance ID because Unity destroys objects
					// in random order and this may get called during teardown when the instance is
					// already gone.
					return GetInstanceID() == instanceId;
				}
			}
		}


		/// <summary>
		/// Returns true if the object is not the singleton instance.
		/// </summary>
		protected bool IsNotTheSingleton
		{
			get
			{
				lock (lockObject)
				{
					// We compare against the last known instance ID because Unity destroys objects
					// in random order and this may get called during teardown when the instance is
					// already gone.
					return GetInstanceID() != instanceId;
				}
			}
		}


		static TComponent[] FindInstances()
		{
			var objects = FindObjectsByType<TComponent>( FindObjectsSortMode.None );
			Array.Sort( objects, ( a, b ) => a.transform.GetSiblingIndex().CompareTo( b.transform.GetSiblingIndex() ) );
			return objects;
		}


		static TComponent FindFirstInstance()
		{
			var objects = FindInstances();
			return objects.Length > 0 ? objects[0] : null;
		}


		// ReSharper disable once VirtualMemberNeverOverridden.Global
		protected virtual void Awake()
		{
			if (Application.isPlaying && Instance)
			{
				if (GetInstanceID() != instanceId)
				{
					#if UNITY_EDITOR
					Debug.LogWarning( "A redundant instance (" + name + ") of singleton " + typeof(TComponent) + " is present in the scene.", this );
					EditorGUIUtility.PingObject( this );
					#endif
					enabled = false;
				}

				// This might be unnecessary, but just to be safe we do it anyway.
				foreach (var redundantInstance in FindInstances().Where( o => o.GetInstanceID() != instanceId ))
				{
					redundantInstance.enabled = false;
				}
			}
		}


		// ReSharper disable once VirtualMemberNeverOverridden.Global
		protected virtual void OnDestroy()
		{
			lock (lockObject)
			{
				if (GetInstanceID() == instanceId)
				{
					hasInstance = false;
				}
			}
		}
	}
}
