using System;
using AceLand.Lifecycle;
using AceLand.Sample.LifeCycle.Scripts.Modules;
using AceLand.Sample.LifeCycle.Scripts.Profiles;
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

        /// <summary>
        /// Populates the UI from lifecycle modules once they are ready.
        /// <para>
        /// Scene MonoBehaviours are not part of the lifecycle dependency graph, so their
        /// <c>Start()</c> can run while async modules (e.g. <see cref="RemoteConfigModule"/>)
        /// are still initializing. Calling <c>ModuleRegistry.Get&lt;T&gt;()</c> here would throw,
        /// because the module is not yet in the Ready state.
        /// </para>
        /// <para>
        /// Instead, we use <c>WhenReady&lt;T&gt;</c>, which invokes the callback immediately when the
        /// module is already ready, or defers it until then. Nesting the calls guarantees every
        /// dependency is ready before <see cref="Init"/> reads their data, avoiding the race.
        /// </para>
        /// </summary>
        protected override void Start()
        {
            ModuleRegistry.WhenReady<GameSettings>(settings =>
                ModuleRegistry.WhenReady<RemoteConfigModule>(remote =>
                    ModuleRegistry.WhenReady<PlayerSystemModule>(player =>
                    {
                        Init(remote.Data, settings, player.PlayerData);
                    })
                )
            );
        }

        private void Init(
            RemoteData remoteData,
            GameSettings gameSettings,
            PlayerData playerData
        )
        {
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