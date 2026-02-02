using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public interface IInteractable
    {
        string InteractionPrompt { get; }
        InteractionPromptData? PromptData { get; }
        bool IsInteractable { get; }
        bool AutoInteract { get; }
        bool IsInteracting { get; }
        int Priority { get; }
        Vector3 Position { get; }
        float InteractionDistance { get; }
        InteractionStateType CurrentState { get; }

        event Action<IInteractable, InteractionStateType> OnStateChanged;

        UniTask<bool> TryInteractAsync(CancellationToken cancellationToken = default);
        void OnFocus();
        void OnDefocus();
        void ForceEndInteraction();
    }
}