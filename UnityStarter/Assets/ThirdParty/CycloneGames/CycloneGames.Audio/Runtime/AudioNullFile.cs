using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public class AudioNullFile : AudioNode
    {
#if UNITY_EDITOR
        private const float NodeHeight = 50;

        public override void InitializeNode(Vector2 position)
        {
            this.name = "Null File";
            this.nodeRect.position = position;
            this.nodeRect.height = NodeHeight;
            AddOutput();
            EditorUtility.SetDirty(this);
        }
#endif
    }
}