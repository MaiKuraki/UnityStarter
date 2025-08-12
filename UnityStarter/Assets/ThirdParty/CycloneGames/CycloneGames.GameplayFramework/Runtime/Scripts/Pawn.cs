using UnityEngine;

namespace CycloneGames.GameplayFramework
{
    public class Pawn : Actor
    {
        private PlayerState playerState;
        private Controller controller;
        public Controller Controller => controller;

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