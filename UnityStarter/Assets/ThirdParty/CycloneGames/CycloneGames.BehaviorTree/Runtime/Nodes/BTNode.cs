using System;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes
{
    public abstract class BTNode : ScriptableObject
    {
        [HideInInspector] public string GUID;

        public Vector2 Position
        {
            get => _position;
            set
            {
                _position = value;
                if (Tree != null)
                {
#if UNITY_EDITOR
                    Tree.OnValidate();
#endif
                }
            }
        }
        public BehaviorTree Tree
        {
            get => _tree;
            set => _tree = value;
        }

        [HideInInspector][SerializeField] private BehaviorTree _tree;
        [HideInInspector][SerializeField] private Vector2 _position;

        public void OnValidate()
        {
            try
            {
                CheckIntegrity();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        protected virtual void CheckIntegrity() { }
        public virtual BTNode Clone()
        {
            BTNode clone;
            if (Application.isPlaying)
            {
                clone = Instantiate(this);
                clone.name = GetType().Name;
                clone._position = _position;
                clone.GUID = GUID;
            }
            else
            {
                clone = CreateInstance(GetType()) as BTNode;
                clone.name = GetType().Name;
                clone._tree = _tree;
            }
            return clone;
        }

        public virtual void OnDrawGizmos() { }

    }
}
