using System;
using AceLand.Lifecycle;
using AceLand.Sample.LifeCycle.Scripts.Profiles;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts.Modules
{
    /// <summary>
    /// This is a sample initialization module.
    /// Here will load data by UnityEngine.Resources, run in Runtime phase.
    /// </summary>
    [LifecycleModule(ModulePhase.Runtime, DependsOn = new[] { typeof(GameSettings) })]
    internal sealed class PlayerSystemModule : ModuleBase
    {
        public PlayerData PlayerData { get; private set; }

        private bool initializing;
        
        public override void Initialize()
        {
            if (initializing || PlayerData != null) return;
            
            initializing = true;

            PlayerData ??= Resources.Load<PlayerData>(nameof(PlayerData));
            
            if (PlayerData == null)
                throw new InvalidOperationException(
                    $"'{nameof(PlayerData)}' not found in any Resources folder.");

            initializing = false;
            
            Debug.Log($"Module Initialized: {nameof(PlayerSystemModule)}");
        }

        public override void Shutdown()
        {
            PlayerData = null;
            
            Debug.Log($"Module Shutdown: {nameof(PlayerSystemModule)}");
        }
    }
}