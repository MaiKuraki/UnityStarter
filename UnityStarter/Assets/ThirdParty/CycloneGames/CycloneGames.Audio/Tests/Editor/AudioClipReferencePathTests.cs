using System.IO;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioClipReferencePathTests
    {
        [Test]
        public void ResolveLocation_StreamingAssetsRelativePath_StaysInsideRoot()
        {
            AudioClipReference reference = CreateReference(AudioLocationKind.StreamingAssetsPath, "Audio/Clip.wav");

            try
            {
                string expected = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Audio/Clip.wav"));

                Assert.AreEqual(expected, reference.ResolveLocation());
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void ResolveLocation_StreamingAssetsParentTraversal_ReturnsEmpty()
        {
            AudioClipReference reference = CreateReference(AudioLocationKind.StreamingAssetsPath, "../Outside.wav");

            try
            {
                Assert.AreEqual(string.Empty, reference.ResolveLocation());
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void ResolveLocation_PersistentDataAbsoluteEscape_ReturnsEmpty()
        {
            string outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CycloneGamesAudioOutside.wav"));
            AudioClipReference reference = CreateReference(AudioLocationKind.PersistentDataPath, outsidePath);

            try
            {
                Assert.AreEqual(string.Empty, reference.ResolveLocation());
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        private static AudioClipReference CreateReference(AudioLocationKind locationKind, string location)
        {
            AudioClipReference reference = ScriptableObject.CreateInstance<AudioClipReference>();
            SerializedObject serializedObject = new SerializedObject(reference);

            serializedObject.FindProperty("locationKind").enumValueIndex = (int)locationKind;
            serializedObject.FindProperty("m_Location").stringValue = location;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            return reference;
        }
    }
}
