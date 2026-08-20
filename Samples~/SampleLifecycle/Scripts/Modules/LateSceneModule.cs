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
    [LifecycleModule(ModulePhase.Late, Order = 5000)]
    internal sealed class LateSceneModule : AsyncModuleBase
    {
        public override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var counter = 3;

            while (counter > 0)
            {
                Debug.Log($"Late Scene Initializing ... {counter}");
                await Task.Delay(1000);
                counter--;
            }
        }
    }
}