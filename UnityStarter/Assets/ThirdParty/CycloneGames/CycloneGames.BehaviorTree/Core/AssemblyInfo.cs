using System.Runtime.CompilerServices;

// The Unity-facing assembly hosts the ScriptableObject authoring layer and the
// MonoBehaviour drivers; both report through the core log facade and read the
// scheduling state the core owns.
[assembly: InternalsVisibleTo("CycloneGames.BehaviorTree.Runtime")]
