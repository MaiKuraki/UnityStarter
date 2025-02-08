namespace CycloneGames.GameplayFramework
{
    public class PlayerState : Actor
    {
        /// <summary>
        /// First Pawn is NewPawn, Second Pawn is OldPawn
        /// </summary>
        public event System.Action<PlayerState, Pawn, Pawn> OnPawnSetEvent;
        private Pawn pawnPrivate;
        public Pawn GetPawn() => pawnPrivate;
        public T GetPawn<T>() where T : Pawn
        {
            return pawnPrivate is T p ? p : null;
        }

        public void SetPawnPrivate(Pawn InPawn)
        {
            if (InPawn != pawnPrivate)
            {
                Pawn oldPawn = pawnPrivate;
                pawnPrivate = InPawn;
                OnPawnSet(this, InPawn, oldPawn);
            }
        }

        private void OnPawnSet(PlayerState playerState, Pawn NewPawn, Pawn OldPawn = null)
        {
            OnPawnSetEvent?.Invoke(playerState, NewPawn, OldPawn);
        }
    }
}