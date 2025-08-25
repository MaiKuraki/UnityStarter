using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using CycloneGames.InputSystem.Runtime;
using CycloneGames.Utility.Runtime;

namespace CycloneGames.InputSystem.Editor
{
    [System.Serializable]
    public class ActionBindingSO
    {
        public ActionValueType Type;
        public string ActionName;
        [StringAsConstSelector(typeof(InputBindingConstants), UseMenu = true)] // Custom drawer for this list!
        public List<string> DeviceBindings = new List<string>();
        [Tooltip("Long-press duration in milliseconds (Button or Float)")]
        [Min(0)]
        public int LongPressMs = 0;
        [Range(0f, 1f)]
        [Tooltip("For Float actions (e.g., Trigger): actuation threshold for long-press timing")] 
        public float LongPressValueThreshold = 0.5f;

        public void FromData(ActionBindingConfig data)
        {
            Type = data.Type;
            ActionName = data.ActionName;
            DeviceBindings = new List<string>(data.DeviceBindings);
            LongPressMs = data.LongPressMs;
            LongPressValueThreshold = data.LongPressValueThreshold;
        }

        public ActionBindingConfig ToData()
        {
            var sanitizedLongPress = ((this.Type == ActionValueType.Button || this.Type == ActionValueType.Float) && this.LongPressMs > 0) ? this.LongPressMs : 0;
            var sanitizedThreshold = (this.Type == ActionValueType.Float && sanitizedLongPress > 0) ? Mathf.Clamp01(this.LongPressValueThreshold) : 0.5f;
            return new ActionBindingConfig
            {
                Type = this.Type,
                ActionName = this.ActionName,
                DeviceBindings = new List<string>(this.DeviceBindings),
                LongPressMs = sanitizedLongPress,
                LongPressValueThreshold = sanitizedThreshold
            };
        }
    }
    [System.Serializable]
    public class ActionBindingDrawerData
    {
        public ActionValueType Type;
        public string ActionName;
        [StringAsConstSelector(typeof(InputBindingConstants), UseMenu = true)] // Uses the custom dropdown drawer
        public List<string> DeviceBindings = new List<string>();
        [Tooltip("Long-press duration in milliseconds (Button or Float)")]
        [Min(0)]
        public int LongPressMs = 0;
        [Range(0f, 1f)]
        [Tooltip("For Float actions (e.g., Trigger): actuation threshold for long-press timing")] 
        public float LongPressValueThreshold = 0.5f;

        public void FromData(ActionBindingConfig data)
        {
            Type = data.Type;
            ActionName = data.ActionName;
            DeviceBindings = new List<string>(data.DeviceBindings);
            LongPressMs = data.LongPressMs;
            LongPressValueThreshold = data.LongPressValueThreshold;
        }

        public ActionBindingConfig ToData()
        {
            var sanitizedLongPress = ((this.Type == ActionValueType.Button || this.Type == ActionValueType.Float) && this.LongPressMs > 0) ? this.LongPressMs : 0;
            var sanitizedThreshold = (this.Type == ActionValueType.Float && sanitizedLongPress > 0) ? Mathf.Clamp01(this.LongPressValueThreshold) : 0.5f;
            return new ActionBindingConfig
            {
                Type = this.Type,
                ActionName = this.ActionName,
                DeviceBindings = new List<string>(this.DeviceBindings),
                LongPressMs = sanitizedLongPress,
                LongPressValueThreshold = sanitizedThreshold
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
            Bindings = data.Bindings.Select(b =>
            {
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

    [System.Serializable]
    public class ContextDefinitionDrawerData
    {
        public string Name;
        public string ActionMap;
        public List<ActionBindingDrawerData> Bindings = new List<ActionBindingDrawerData>();

        public void FromData(ContextDefinitionConfig data)
        {
            Name = data.Name;
            ActionMap = data.ActionMap;
            Bindings = data.Bindings.Select(b =>
            {
                var drawerData = new ActionBindingDrawerData();
                drawerData.FromData(b);
                return drawerData;
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

    [System.Serializable]
    public class PlayerSlotSO : ScriptableObject
    {
        public int PlayerId;
        public List<ContextDefinitionSO> Contexts = new List<ContextDefinitionSO>();

        public void FromData(PlayerSlotConfig data)
        {
            PlayerId = data.PlayerId;
            Contexts = data.Contexts.Select(c =>
            {
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

    [System.Serializable]
    public class PlayerSlotDrawerData
    {
        public int PlayerId;
        public List<ContextDefinitionDrawerData> Contexts = new List<ContextDefinitionDrawerData>();

        public void FromData(PlayerSlotConfig data)
        {
            PlayerId = data.PlayerId;
            Contexts = data.Contexts.Select(c =>
            {
                var drawerData = new ContextDefinitionDrawerData();
                drawerData.FromData(c);
                return drawerData;
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
        [SerializeField] private ActionBindingDrawerData _joinAction = new ActionBindingDrawerData();

        [Header("Configuration templates for each player slot")]
        [SerializeField] private List<PlayerSlotDrawerData> _playerSlots = new List<PlayerSlotDrawerData>();

        public void FromData(InputConfiguration data)
        {
            // Now correctly creates simple class instances, NOT ScriptableObjects.
            _joinAction.FromData(data.JoinAction);
            _playerSlots = data.PlayerSlots.Select(p =>
            {
                var drawerData = new PlayerSlotDrawerData();
                drawerData.FromData(p);
                return drawerData;
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