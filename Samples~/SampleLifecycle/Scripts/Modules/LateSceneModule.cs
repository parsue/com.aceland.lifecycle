using System.Threading;
using System.Threading.Tasks;
using AceLand.Lifecycle;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts.Modules
{
    /// <summary>
    /// This is a sample initialization module.
    /// The first scene is already started and player will see the scene.
    ///
    /// If await process is too long on phase before Late,
    ///     player will only see a black screen.
    /// Good design is remain must-run-before-scene-tasks in phase before,
    ///     and run other tasks in this phase.
    /// See InitialFilterUi.cs for handling first scene.
    /// </summary>
    [LifecycleModule(ModulePhase.Late)]
    internal sealed class LateSceneModule : ModuleBase
    {
        public override void Initialize()
        {
            Debug.Log($"Module Initialized: {nameof(LateSceneModule)}: do anything when scene objects awake.");
        }

        public override void Shutdown()
        {
            Debug.Log($"Module Shutdown: {nameof(LateSceneModule)}");
        }
    }
}