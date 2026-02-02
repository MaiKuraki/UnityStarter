using System;
using Cysharp.Threading.Tasks;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public interface IInteractionSystem : IDisposable
    {
        void Initialize();
        UniTask ProcessInteractionAsync(IInteractable target);
    }
}