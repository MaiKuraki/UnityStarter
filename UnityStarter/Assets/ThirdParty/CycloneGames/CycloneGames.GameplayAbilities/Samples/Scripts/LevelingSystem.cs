using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct LevelData
{
    public int XpToNextLevel;
    public float HealthGain;
    public float ManaGain;
    public float AttackGain;
    public float DefenseGain;
}

[CreateAssetMenu(fileName = "LevelUpData", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Level Up Data")]
public class LevelingSystem : ScriptableObject
{
    public List<LevelData> Levels;
}