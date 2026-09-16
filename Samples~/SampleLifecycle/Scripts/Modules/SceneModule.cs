using System;
using System.Threading;
using System.Threading.Tasks;
using AceLand.Lifecycle;
using AceLand.Sample.LifeCycle.Scripts.Profiles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AceLand.Sample.LifeCycle.Scripts.Modules
{
    /// <summary>
    /// This is a sample initialization module.
    /// This will instantiate game object on first scene (current scene in editor),
    ///     phase Scene is running after scene load, but before components awake.
    /// As depending on an Async Module (RemoteConfigModule),
    ///     SceneModule must be an AsyncModule too.  
    /// </summary>
    [LifecycleModule(ModulePhase.Scene,
        DependsOn = new[] { typeof(RemoteConfigModule), typeof(GameSettings), typeof(PlayerSystemModule) })]
    internal sealed class SceneModule : AsyncModuleBase
    {
        private const int COUNT = 3;
        
        public override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            Debug.Log($"Module Initializing: {nameof(SceneModule)}: do anything before scene objects awake");

            var i = 0;
            while (i < COUNT)
            {
                Debug.Log($"Module Initializing: {COUNT - i}");
                await Task.Delay(1000, cancellationToken);
                i++;
            }
            
            Debug.Log($"Module Initialized: {nameof(SceneModule)}: do anything before scene objects awake");
        }

        public override void Shutdown()
        {
            Debug.Log($"Module Shutdown: {nameof(SceneModule)}");
        }
    }
}