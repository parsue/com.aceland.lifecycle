using System.Threading.Tasks;
using AceLand.Lifecycle;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts.Modules
{
    /// <summary>
    /// This is a sample Quit Handler module.
    /// Quit Handler will run after Quit Blocker.
    /// When all Quit Handler is completed, application will quit.
    ///
    /// In editor mode, event system in scene may stop on quit,
    ///     but alive in build mode. 
    /// See SafeQuitFilterUi.cs for handling first scene.
    /// </summary>
    [LifecycleModule(ModulePhase.Runtime, AllowParallel = true)]
    [QuitOrder(-100)]
    public sealed class SafeQuitTestModule : ModuleBase, IQuitHandler
    {
        private const int COUNTDOWN = 3;

        public override void Initialize()
        {
            Debug.Log($"Module Initialized: {nameof(SafeQuitTestModule)}");
        }

        public async Task OnBeforeQuitAsync(QuitContext ctx)
        {
            ctx.SetStatus("Testing Safe Quit ...");

            var c = COUNTDOWN;

            while (c > 0)
            {
                Debug.Log($"Safe Quit ... {c}");
                await Task.Delay(1000, ctx.Token);
                c--;
            }
        }
    }
}