using UnityEngine;
using CycloneGames.Logging;

namespace CycloneGames.InputSystem.Sample
{
    /// <summary>
    /// Example component demonstrating input event handling via commands.
    /// </summary>
    public class SimplePlayerController : MonoBehaviour
    {
        private static readonly LogChannel Log = InputSystemSampleLog.Channel;

        private int _playerId;
        private Color _playerColor;
        private Renderer _renderer;
        private Material _runtimeMaterial;

        private const float MoveSpeed = 5f;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        public void Initialize(int playerId, Color color)
        {
            _playerId = playerId;
            _playerColor = color;
            if (_renderer)
            {
                if (_runtimeMaterial == null)
                {
                    _runtimeMaterial = _renderer.material;
                }

                if (_runtimeMaterial != null)
                {
                    _runtimeMaterial.color = _playerColor;
                }
            }
            gameObject.name = $"Player_{_playerId}";
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial != null)
            {
                Destroy(_runtimeMaterial);
                _runtimeMaterial = null;
            }
        }

        public void OnMove(Vector2 direction)
        {
            transform.Translate(new Vector3(direction.x, 0, direction.y) * (MoveSpeed * Time.deltaTime));
        }

        public void OnConfirm()
        {
            Log.Info(
                _playerId,
                static (playerId, builder) => builder
                    .Append("Player ")
                    .Append(playerId)
                    .Append(": Confirm triggered!"));
            transform.position += Vector3.up;
        }

        public void OnConfirmLongPress()
        {
            Log.Info(
                _playerId,
                static (playerId, builder) => builder
                    .Append("Player ")
                    .Append(playerId)
                    .Append(": Confirm LONG-PRESSED!"));
            transform.localScale *= 1.1f;
        }
    }
}
