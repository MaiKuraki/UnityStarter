using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class PlayerState : Actor
    {
        /// <summary>
        /// Fired when the pawn changes. Args: (PlayerState, NewPawn, OldPawn)
        /// </summary>
        public event System.Action<PlayerState, Pawn, Pawn> OnPawnSetEvent;

        [SerializeField] private string playerName;
        private int playerId;
        private float score;
        private bool bIsABot;
        private bool bIsSpectator;
        private Pawn pawnPrivate;

        #region Pawn
        public Pawn GetPawn() => pawnPrivate;
        public T GetPawn<T>() where T : Pawn => pawnPrivate as T;

        public void SetPawnPrivate(Pawn InPawn)
        {
            if (ReferenceEquals(InPawn, pawnPrivate)) return;
            Pawn oldPawn = pawnPrivate;
            pawnPrivate = InPawn;
            OnPawnSet(this, InPawn, oldPawn);
        }

        private void OnPawnSet(PlayerState ps, Pawn NewPawn, Pawn OldPawn)
        {
            OnPawnSetEvent?.Invoke(ps, NewPawn, OldPawn);
        }
        #endregion

        #region Player Info
        public string GetPlayerName() => playerName;
        public void SetPlayerName(string NewName) => playerName = NewName;

        public int GetPlayerId() => playerId;
        public void SetPlayerId(int NewId) => playerId = NewId;

        public float GetScore() => score;
        public void SetScore(float NewScore) => score = NewScore;
        public float AddScore(float DeltaScore)
        {
            score += DeltaScore;
            return score;
        }

        public bool IsABot() => bIsABot;
        public void SetIsABot(bool bNewIsABot) => bIsABot = bNewIsABot;

        public bool IsSpectator() => bIsSpectator;
        public void SetIsSpectator(bool bNewIsSpectator) => bIsSpectator = bNewIsSpectator;
        #endregion

        #region Copy
        /// <summary>
        /// Copies essential properties from another PlayerState (useful for seamless travel / respawn).
        /// </summary>
        public virtual void CopyProperties(PlayerState other)
        {
            if (other == null) return;
            playerName = other.playerName;
            playerId = other.playerId;
            score = other.score;
            bIsABot = other.bIsABot;
        }
        #endregion

        protected override void OnDestroy()
        {
            OnPawnSetEvent = null;
            base.OnDestroy();
        }
    }
}