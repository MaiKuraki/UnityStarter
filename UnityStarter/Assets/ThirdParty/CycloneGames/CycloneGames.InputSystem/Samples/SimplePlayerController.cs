using UnityEngine;

namespace CycloneGames.InputSystem.Sample
{
    /// <summary>
    /// A simple example component to be placed on a player prefab.
    /// It demonstrates how to receive input events from the commands.
    /// </summary>
    public class SimplePlayerController : MonoBehaviour
    {
        private int _playerId;
        private Color _playerColor;
        private Renderer _renderer;

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
                _renderer.material.color = _playerColor;
            }
            gameObject.name = $"Player_{_playerId}";
        }

        public void OnMove(Vector2 direction)
        {
            // Simple movement logic
            transform.Translate(new Vector3(direction.x, 0, direction.y) * (MoveSpeed * Time.deltaTime));
        }

        public void OnConfirm()
        {
            Debug.Log($"Player {_playerId}: Confirm action triggered!");
            // Example: Make the player jump or interact
            transform.position += Vector3.up;
        }
        
        public void OnConfirmLongPress()
        {
            Debug.Log($"Player {_playerId}: Confirm action LONG-PRESSED!");
            // Example: Make the player do a special move
            transform.localScale *= 1.1f;
        }
    }
}
