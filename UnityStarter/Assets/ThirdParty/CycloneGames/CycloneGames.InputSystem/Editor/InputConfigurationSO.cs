using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using CycloneGames.InputSystem.Runtime;

namespace CycloneGames.InputSystem.Editor
{
    // --- These classes are ScriptableObject mirrors of the pure C# data models ---
    // --- This makes them easy to work with in a custom Unity Editor window ---

    [System.Serializable]
    public class ActionBindingSO
    {
        public string ActionName;
        [StringAsConstSelector(typeof(InputBindingConstants))] // Custom drawer for this list!
        public List<string> DeviceBindings = new List<string>();

        public void FromData(ActionBindingConfig data)
        {
            ActionName = data.ActionName;
            DeviceBindings = new List<string>(data.DeviceBindings);
        }

        public ActionBindingConfig ToData()
        {
            return new ActionBindingConfig
            {
                ActionName = this.ActionName,
                DeviceBindings = new List<string>(this.DeviceBindings)
            };
        }
    }
    
    public class ContextDefinitionSO : ScriptableObject
    {
        public string Name;
        public string ActionMap;
        public List<ActionBindingSO> Bindings = new List<ActionBindingSO>();

        public void FromData(ContextDefinitionConfig data)
        {
            Name = data.Name;
            ActionMap = data.ActionMap;
            Bindings = data.Bindings.Select(b => {
                var so = new ActionBindingSO();
                so.FromData(b);
                return so;
            }).ToList();
        }
        
        public ContextDefinitionConfig ToData()
        {
            return new ContextDefinitionConfig
            {
                Name = this.Name,
                ActionMap = this.ActionMap,
                Bindings = this.Bindings.Select(b => b.ToData()).ToList()
            };
        }
    }
    
    public class PlayerSlotSO : ScriptableObject
    {
        public int PlayerId;
        public List<ContextDefinitionSO> Contexts = new List<ContextDefinitionSO>();
        
        public void FromData(PlayerSlotConfig data)
        {
            PlayerId = data.PlayerId;
            Contexts = data.Contexts.Select(c => {
                var so = CreateInstance<ContextDefinitionSO>();
                so.FromData(c);
                return so;
            }).ToList();
        }
        
        public PlayerSlotConfig ToData()
        {
            return new PlayerSlotConfig
            {
                PlayerId = this.PlayerId,
                Contexts = this.Contexts.Select(c => c.ToData()).ToList()
            };
        }
    }

    public class InputConfigurationSO : ScriptableObject
    {
        [Header("Action triggered by any device to join the game")]
        [SerializeField] private ActionBindingSO _joinAction = new ActionBindingSO();
        
        [Header("Configuration templates for each player slot")]
        [SerializeField] private List<PlayerSlotSO> _playerSlots = new List<PlayerSlotSO>();

        public void FromData(InputConfiguration data)
        {
            _joinAction.FromData(data.JoinAction);
            _playerSlots = data.PlayerSlots.Select(p => {
                var so = CreateInstance<PlayerSlotSO>();
                so.FromData(p);
                return so;
            }).ToList();
        }
        
        public InputConfiguration ToData()
        {
            return new InputConfiguration
            {
                JoinAction = _joinAction.ToData(),
                PlayerSlots = _playerSlots.Select(p => p.ToData()).ToList()
            };
        }
    }
}