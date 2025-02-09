using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AdvancedSceneManager.Models
{

    /// <summary>Represents the core variables of what makes up a scene collection.</summary>
    public interface ISceneCollection : IEnumerable<Scene>, IEnumerable, INotifyPropertyChanged
    {

        /// <summary>Gets the scenes of this collection.</summary>
        public IEnumerable<Scene> scenes { get; }

        /// <summary>Gets the scenes of this collection.</summary>
        public IEnumerable<string> scenePaths { get; }

        /// <summary>Gets the title of this collection.</summary>
        public string title { get; }

        /// <summary>Gets the description of this collection.</summary>
        public string description { get; }

        /// <summary>Gets the scene count of this collection.</summary>
        public int count { get; }

        /// <summary>Gets the id of this collection.</summary>
        public string id { get; }

        /// <summary>Gets the scene at the specified index.</summary>
        public Scene this[int index] { get; }

        public void OnPropertyChanged([CallerMemberName] string propertyName = null);

        public interface IEditable : ISceneCollection
        {
            public List<Scene> sceneList { get; }
        }

        public interface IOpenable
        { }

    }

}
