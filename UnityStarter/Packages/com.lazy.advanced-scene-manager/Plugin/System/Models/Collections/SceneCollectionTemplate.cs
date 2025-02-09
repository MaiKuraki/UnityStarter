using AdvancedSceneManager.Models.Internal;
using UnityEngine;

namespace AdvancedSceneManager.Models.Utility
{

    /// <summary>Represents a template for a <see cref="SceneCollection"/>.</summary>
    public class SceneCollectionTemplate : SceneCollection
    {

        /// <inheritdoc cref="SceneCollection.name"/>
        public new string name => base.name;

#if UNITY_EDITOR

        protected override bool UsePrefix { get; } = false;

        /// <summary>Creates a <see cref="SceneCollection"/> from this <see cref="SceneCollectionTemplate"/>.</summary>
        /// <remarks>Only available in editor.</remarks>
        public SceneCollection CreateCollection(Profile profile) =>
            profile.CreateCollection(this);

        /// <summary>Applies a <see cref="SceneCollectionTemplate"/> on this <see cref="SceneCollection"/>.</summary>
        /// <remarks>Only available in editor. Not reversible.</remarks>
        public void Apply(SceneCollection collection)
        {
            var id = collection.id;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(this), collection);
            collection.m_id = id;
            collection.Save();
        }

        /// <summary>Creates <see cref="SceneCollectionTemplate"/> from the specified <see cref="SceneCollection"/>.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static SceneCollectionTemplate CreateTemplate(SceneCollection collection)
        {

            var template = CreateInternal<SceneCollectionTemplate>(collection.title);
            var id = template.id;

            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(collection), template);
            ((ScriptableObject)template).name = template.title;

            //Reassign id
            template.m_id = id;

            Assets.Add(template);

            return template;

        }

        /// <summary>Creates <see cref="SceneCollectionTemplate"/> using default properties.</summary>
        /// <remarks>Only available in editor.</remarks>
        public static SceneCollectionTemplate CreateTemplate(string title)
        {

            var template = CreateInternal<SceneCollectionTemplate>(title);
            template.SetTitleAfterCreation("", title);
            ((ScriptableObject)template).name = title;

            Assets.Add(template);

            return template;

        }

#endif

    }

}
