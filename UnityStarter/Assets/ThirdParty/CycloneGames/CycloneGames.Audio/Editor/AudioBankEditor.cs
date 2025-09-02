using UnityEngine;
using UnityEditor;
using CycloneGames.Audio.Runtime;

namespace CycloneGames.Audio.Editor
{
    /// <summary>
    /// Override inspector for quick editing in the graph
    /// </summary>
    [CustomEditor(typeof(AudioBank))]
    public class AudioBankEditor : UnityEditor.Editor
    {
        /// <summary>
        /// AudioBank to edit in the graph
        /// </summary>
        private AudioBank myTarget;

        /// <summary>
        /// Set reference for AudioBank to pass to graph window
        /// </summary>
        private void OnEnable()
        {
            this.myTarget = (AudioBank)target;
        }

        /// <summary>
        /// Display a button to open the bank in the graph
        /// </summary>
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Open in Graph"))
            {
                AudioGraph.OpenAudioGraph(this.myTarget);
            }
        }
    }
}