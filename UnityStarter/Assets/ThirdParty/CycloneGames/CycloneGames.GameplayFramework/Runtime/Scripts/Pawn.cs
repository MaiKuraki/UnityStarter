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
        public void PossessedBy(Controller NewController)
        {
            SetOwner(NewController);
            
            controller = NewController;

            if (Controller.GetPlayerState() != null)
            {
                SetPlayerState(Controller.GetPlayerState());
            }
        }

        public void UnPossessed()
        {
            SetPlayerState(null);
            SetOwner(null);
            controller = null;
        }

        //  TODO: SetPawnPrivate should not called from pawn.
        private void SetPlayerState(PlayerState NewPlayerState)
        {
            PlayerState OldPlayerState = playerState;
            if (playerState && playerState.GetPawn() == this)
            {
                Pawn oldPawn = playerState.GetPawn();
                playerState.SetPawnPrivate(null);
            }
            
            playerState = NewPlayerState;

            if (playerState)
            {
                Pawn oldPawn = playerState.GetPawn();
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