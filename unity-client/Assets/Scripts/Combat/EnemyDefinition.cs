using System;
using UnityEngine;

namespace KnowledgeWarrior.Combat
{
    public enum AttackGestureType
    {
        HeroBladeSlash = 0,
        HeroArcaneBurst = 1,
        MonsterClawSwipe = 2,
        MonsterVenomBolt = 3,
        MonsterCrushSlam = 4,
        BossInfernoBreath = 5
    }

    /// <summary>Data for one monster or stage boss encounter in a subject biome.</summary>
    [Serializable]
    public class EnemyDefinition
    {
        public string displayName = "Syntax Slime";
        public Sprite sprite;
        public Color tint = Color.white;
        [Tooltip("Height of the sprite in world units.")]
        public float worldHeight = 2.6f;
        public AttackGestureType attackGesture = AttackGestureType.MonsterClawSwipe;

        [Header("Stats")]
        public int maxHp = 60;
        public int attack = 12;
        public int xpReward = 30;
        public bool isBoss;

        [Header("Questions")]
        [Tooltip("Aligned question difficulty tier (1 = Beginner, 2 = Intermediate, 3 = Advanced, 4 = Expert).")]
        [Range(1, 4)] public int maxDifficulty = 1;
        [Tooltip("Subject filter such as Programming or Databases. Leave empty for all subjects.")]
        public string subject;
    }
}
