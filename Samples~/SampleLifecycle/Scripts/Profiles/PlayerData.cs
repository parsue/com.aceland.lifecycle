using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts.Profiles
{
    [CreateAssetMenu(fileName = "PlayerData", menuName = "AceLand/Sample/LifeCycle/PlayerData")]
    public sealed class PlayerData : ScriptableObject
    {
        [SerializeField] private string playerName;
        [SerializeField, Range(1, 20)] private int playerLevel;
        [SerializeField, Range(5, 80)] private int playerLife;
        [SerializeField, Range(5, 80)] private int playerLifeMax;
        
        public string PlayerName => playerName;
        public int PlayerLevel => playerLevel;
        public int PlayerLife => playerLife;
        public int PlayerLifeMax => playerLifeMax;

        private void OnValidate()
        {
            if (playerLife > playerLifeMax)
                playerLife = playerLifeMax;
        }
    }
}