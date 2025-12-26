using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class Pawn : Actor
    {
        private PlayerState playerState;
        private Controller controller;
        public Controller Controller => controller;
        private readonly List<MonoBehaviour> _cachedComponents = new List<MonoBehaviour>(16);

        /// <summary>
        /// Notifies components about the initial rotation when a Pawn is spawned.
        /// Components that implement IInitialRotationSettable will automatically synchronize their rotation.
        /// 
        /// IMPORTANT FOR DEVELOPERS:
        /// If GameplayFramework is not installed via Package Manager (e.g., placed directly in Assets folder),
        /// and you are using MovementComponent from RPGFoundation, you must manually set the define symbol
        /// GAMEPLAY_FRAMEWORK_PRESENT in PlayerSettings > Scripting Define Symbols for automatic rotation
        /// synchronization to work. Otherwise, you will need to manually set the Pawn's rotation after spawning:
        /// 
        /// Example manual setup:
        /// ```csharp
        /// Pawn pawn = SpawnDefaultPawnAtTransform(...);
        /// var movement = pawn.GetComponent<MovementComponent>();
        /// if (movement != null)
        /// {
        ///     movement.SetRotation(spawnTransform.rotation, immediate: true);
        /// }
        /// ```
        /// </summary>
        public void NotifyInitialRotation(Quaternion rotation)
        {
            _cachedComponents.Clear();
            GetComponents(_cachedComponents);
            for (int i = 0; i < _cachedComponents.Count; i++)
            {
                if (_cachedComponents[i] is IInitialRotationSettable settable)
                {
                    settable.SetInitialRotation(rotation, immediate: true);
                }
            }
        }

        public void DispatchRestart()
        {
            Restart();
        }
        private void Restart()
        {
            //  TODO: MAYBE BLOCK MOVEMENT
        }
        public virtual void PossessedBy(Controller NewController)
        {
            SetOwner(NewController);
            controller = NewController;

            if (Controller.GetPlayerState() != null)
            {
                SetPlayerState(Controller.GetPlayerState());
            }
        }

        public virtual void UnPossessed()
        {
            SetPlayerState(null);
            SetOwner(null);
            controller = null;
        }

        //  TODO: SetPawnPrivate should not called from pawn.
        private void SetPlayerState(PlayerState NewPlayerState)
        {
            if (playerState && playerState.GetPawn() == this)
            {
                playerState.SetPawnPrivate(null);
            }
            playerState = NewPlayerState;

            if (playerState)
            {
                playerState.SetPawnPrivate(this);
            }
            //  OnPlayerStateChangedEvent
        }

        Quaternion GetControlRotation()
        {
            return Controller ? Controller.ControlRotation() : UnityEngine.Quaternion.identity;
        }

        bool IsControlled()
        {
            return (PlayerController)Controller != null;
        }
    }
}