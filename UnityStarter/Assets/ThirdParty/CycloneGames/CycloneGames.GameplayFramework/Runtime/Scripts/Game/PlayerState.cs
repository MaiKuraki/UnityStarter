using System.Runtime.Serialization;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Plain-data transfer object for PlayerState.
    /// Decoupled from MonoBehaviour so external serializers (protobuf-net, MessagePack,
    /// FlatBuffers, System.Text.Json, etc.) can freely instantiate and annotate it.
    ///
    /// [DataContract] / [DataMember(Order=N)] are used instead of library-specific attributes
    /// so that both MessagePack array-mode and protobuf-net recognise the base fields
    /// without any additional annotations in the framework layer:
    ///   - MessagePack:    [DataMember(Order=N)] acts as [Key(N)]
    ///   - ProtoBuf-net:   [DataMember(Order=N)] acts as [ProtoMember(N)]
    ///   - Newtonsoft.Json / System.Text.Json: recognised natively, no conflict
    ///
    /// Extend this class in your game to carry additional fields and override
    /// ToDataObject / FromDataObject:
    ///
    ///   [DataContract]
    ///   public class RPGPlayerStateData : PlayerStateData
    ///   {
    ///       [DataMember(Order = 10)] public float Score  { get; set; }
    ///       [DataMember(Order = 11)] public int   TeamId { get; set; }
    ///   }
    /// </summary>
    [DataContract]
    public class PlayerStateData
    {
        [DataMember(Order = 1)] public string PlayerName  { get; set; }
        [DataMember(Order = 2)] public int    PlayerId    { get; set; }
        [DataMember(Order = 3)] public bool   IsSpectator { get; set; }
    }

    public class PlayerState : Actor, IGameplayFrameworkSerializable
    {
        /// <summary>
        /// Fired when the pawn changes. Args: (PlayerState, NewPawn, OldPawn)
        /// </summary>
        public event System.Action<PlayerState, Pawn, Pawn> OnPawnSetEvent;

        [SerializeField] private string playerName;
        private int playerId;
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
        /// <summary>Display name of the player. Persists across respawns.</summary>
        public string GetPlayerName() => playerName;
        public void SetPlayerName(string NewName) => playerName = NewName;

        /// <summary>Unique session identifier assigned by GameMode/GameSession.</summary>
        public int GetPlayerId() => playerId;
        public void SetPlayerId(int NewId) => playerId = NewId;

        /// <summary>
        /// Whether this player is in spectator mode.
        /// GameMode checks this in RestartPlayer to decide whether to spawn an active pawn.
        /// To check if a controller is AI-driven, use <c>controller is AIController</c> instead.
        /// </summary>
        public bool IsSpectator() => bIsSpectator;
        public void SetIsSpectator(bool bNewIsSpectator) => bIsSpectator = bNewIsSpectator;
        #endregion

        #region Copy
        /// <summary>
        /// Copies framework-level identity properties from another PlayerState.
        /// Called during seamless level travel to preserve player identity across scenes.
        /// Override in subclasses to also copy game-specific fields (score, inventory, etc.)
        /// </summary>
        public virtual void CopyProperties(PlayerState other)
        {
            if (other == null) return;
            playerName   = other.playerName;
            playerId     = other.playerId;
            bIsSpectator = other.bIsSpectator;
        }
        #endregion

        #region Serialization
        /// <summary>
        /// Write player state to data writer (save, network snapshot, etc.)
        /// Override in subclasses to serialize additional game-specific fields.
        /// </summary>
        public virtual void Serialize(IDataWriter writer)
        {
            if (writer == null) return;
            writer.WriteString("PlayerName", playerName);
            writer.WriteInt("PlayerId", playerId);
            writer.WriteBool("IsSpectator", bIsSpectator);
        }

        /// <summary>
        /// Read player state from data reader (load, network snapshot, etc.)
        /// Override in subclasses to deserialize additional game-specific fields.
        /// </summary>
        public virtual void Deserialize(IDataReader reader)
        {
            if (reader == null) return;
            playerName   = reader.ReadString("PlayerName");
            playerId     = reader.ReadInt("PlayerId");
            bIsSpectator = reader.ReadBool("IsSpectator");
        }

        /// <summary>
        /// Export current state as a plain-data DTO.
        /// Use this when the serialization library (protobuf-net, MessagePack, FlatBuffers,
        /// System.Text.Json attribute-based serialization, etc.) needs a POCO it can freely
        /// instantiate, annotate, and round-trip without touching MonoBehaviour.
        /// Override in subclasses and return a derived <see cref="PlayerStateData"/> carrying
        /// additional game-specific fields (score, inventory, team, etc.)
        /// </summary>
        public virtual PlayerStateData ToDataObject()
        {
            return new PlayerStateData
            {
                PlayerName  = playerName,
                PlayerId    = playerId,
                IsSpectator = bIsSpectator,
            };
        }

        /// <summary>
        /// Apply state from a plain-data DTO back onto this PlayerState.
        /// Complement of <see cref="ToDataObject"/>.
        /// </summary>
        public virtual void FromDataObject(PlayerStateData data)
        {
            if (data == null) return;
            playerName   = data.PlayerName;
            playerId     = data.PlayerId;
            bIsSpectator = data.IsSpectator;
        }
        #endregion

        protected override void OnDestroy()
        {
            OnPawnSetEvent = null;
            base.OnDestroy();
        }
    }
}