using System;
using AceLand.Lifecycle;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts.Modules
{
    /// <summary>
    /// This is a sample initialization module.
    /// There is no dependency on any services of Unity, run in Core phase.
    /// Confirming first run, module order set to -5000
    /// </summary>
    [LifecycleModule(ModulePhase.Core, Order = -5000)]
    internal sealed class GameSettings : ModuleBase
    {
        public string GameId { get; private set; }
        public string GameName { get; private set; }
        public string GameVersion { get; private set; }
        public string GameDescription{ get; private set; }
        public string GameAuthor { get; private set; }
        
        public override void Initialize()
        {
            GameId = Guid.NewGuid().ToString();
            GameName = "AceLand Lifecycle Sample";
            GameVersion = "0.1.0";
            GameDescription = "This is a sample data of AceLand Lifecycle";
            GameAuthor = "Parsue Choi";
            
            ModuleRegistry.ModuleStateChanged += OnStateChanged;
            
            Debug.Log($"Module Initialized: {nameof(GameSettings)}");
        }

        public override void Shutdown()
        {
            GameName = null;
            GameVersion = null;
            GameDescription = null;
            GameAuthor = null;
            
            Debug.Log($"Module Shutdown: {nameof(GameSettings)}");
        }

        private static void OnStateChanged(ModuleEntry entry)
        {
            Debug.Log($"Module State: {entry.DisplayName} - {entry.State}");
        }
    }
}