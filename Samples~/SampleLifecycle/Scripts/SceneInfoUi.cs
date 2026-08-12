using System;
using AceLand.Lifecycle;
using AceLand.Sample.LifeCycle.Scripts.Modules;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class SceneInfoUi : UIBehaviour
    {
        [Header("Remote Config")]
        [SerializeField] private TextMeshProUGUI accessTokenText;
        [SerializeField] private TextMeshProUGUI serverStateText;
        [SerializeField] private TextMeshProUGUI gatewayIdText;
        
        [Header("Game Settings")]
        [SerializeField] private TextMeshProUGUI gameNameText;
        [SerializeField] private TextMeshProUGUI gameVersionText;
        [SerializeField] private TextMeshProUGUI gameDescriptionText;
        [SerializeField] private TextMeshProUGUI gameAuthorText;
        
        [Header("Player Data")]
        [SerializeField] private TextMeshProUGUI playerNameText;
        [SerializeField] private TextMeshProUGUI playerLevelText;
        [SerializeField] private TextMeshProUGUI playerLifeText;
        [SerializeField] private TextMeshProUGUI playerLifeMaxText;

        protected override void Start()
        {
            var remoteData = ModuleRegistry.Get<RemoteConfigModule>().Data;
            var gameSettings = ModuleRegistry.Get<GameSettings>();
            var playerData = ModuleRegistry.Get<PlayerSystemModule>().PlayerData;

            if (remoteData == null || gameSettings == null || playerData == null)
                throw new Exception("Data is not ready.");
            
            accessTokenText?.SetText(remoteData.AccessToken);
            serverStateText?.SetText(remoteData.ServerState);
            gatewayIdText?.SetText(remoteData.GatewayId.ToString());
            
            gameNameText?.SetText(gameSettings.GameName);
            gameVersionText?.SetText(gameSettings.GameVersion);
            gameDescriptionText?.SetText(gameSettings.GameDescription);
            gameAuthorText?.SetText(gameSettings.GameAuthor);
            
            playerNameText?.SetText(playerData.PlayerName);
            playerLevelText?.SetText(playerData.PlayerLevel.ToString());
            playerLifeText?.SetText(playerData.PlayerLife.ToString());
            playerLifeMaxText?.SetText(playerData.PlayerLifeMax.ToString());
        }
    }
}