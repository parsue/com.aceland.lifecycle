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
        public SceneData SceneData { get; private set; }

        private bool initializing;

        public override Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
        
        public override void Initialize()
        {
            if (initializing || SceneData != null) return;
            
            initializing = true;

            SceneData ??= Resources.Load<SceneData>(nameof(SceneData));
            
            if (SceneData == null)
                throw new InvalidOperationException(
                    $"'{nameof(SceneData)}' not found in any Resources folder.");

            initializing = false;
            
            Debug.Log($"Module Initialized: {nameof(SceneModule)}");
            
            CreateSceneObject();
        }

        public override void Shutdown()
        {
            SceneData = null;
            
            Debug.Log($"Module Shutdown: {nameof(SceneModule)}");
        }

        private void CreateSceneObject()
        {
            Debug.Log("Creating Scene Objects ...");

            foreach (var prefab in SceneData.Prefabs)
            {
                var ui = Object.Instantiate(prefab);
                var replace = ui.gameObject.name.Replace("(Clone)", "");
                var goName = $"{replace.Trim()} (created by {nameof(SceneModule)})";
                ui.gameObject.name = goName;
                
                Debug.Log($"Scene Object created: {goName}");
            }
        }
    }
}