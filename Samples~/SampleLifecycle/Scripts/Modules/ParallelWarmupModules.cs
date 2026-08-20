using System.Threading;
using System.Threading.Tasks;
using AceLand.Lifecycle;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts.Modules
{
    /// <summary>
    /// Phase 2 sample — parallel initialization.
    ///
    /// These two modules live on the SAME dependency level (neither depends on the
    /// other) and both opt into <see cref="LifecycleModuleAttribute.AllowParallel"/>.
    /// The registry runs their <see cref="AsyncModuleBase.InitializeAsync"/> together
    /// via Task.WhenAll, so the phase spends ~800ms instead of ~1600ms.
    ///
    /// Parallelism is strictly opt-in: remove AllowParallel and they run in order.
    /// </summary>
    [LifecycleModule(ModulePhase.Runtime, AllowParallel = true)]
    internal sealed class AudioWarmupModule : AsyncModuleBase
    {
        public bool Ready { get; private set; }

        public override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            Debug.Log($"[Parallel] {nameof(AudioWarmupModule)} warming up ...");
            await Task.Delay(800, cancellationToken);
            Ready = true;
            Debug.Log($"[Parallel] {nameof(AudioWarmupModule)} ready.");
        }

        public override void Shutdown()
        {
            Ready = false;
            Debug.Log($"Module Shutdown: {nameof(AudioWarmupModule)}");
        }
    }

    /// <summary>
    /// Phase 2 sample — the parallel sibling of <see cref="AudioWarmupModule"/>.
    /// Same level, same AllowParallel flag, so both overlap.
    /// </summary>
    [LifecycleModule(ModulePhase.Runtime, AllowParallel = true)]
    internal sealed class AssetWarmupModule : AsyncModuleBase
    {
        public bool Ready { get; private set; }

        public override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            Debug.Log($"[Parallel] {nameof(AssetWarmupModule)} warming up ...");
            await Task.Delay(800, cancellationToken);
            Ready = true;
            Debug.Log($"[Parallel] {nameof(AssetWarmupModule)} ready.");
        }

        public override void Shutdown()
        {
            Ready = false;
            Debug.Log($"Module Shutdown: {nameof(AssetWarmupModule)}");
        }
    }

    /// <summary>
    /// Phase 2 sample — per-module timeout.
    ///
    /// This module intentionally takes far longer than its 500ms budget.
    /// The registry marks it Failed (never blocking the rest of the phase) and
    /// records the reason on <see cref="ModuleEntry.Error"/>, embodying the
    /// "never deadlock" philosophy.
    /// </summary>
    [LifecycleModule(ModulePhase.Runtime, AllowParallel = true, TimeoutMs = 500)]
    internal sealed class SlowNetworkProbeModule : AsyncModuleBase
    {
        public override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            Debug.Log($"[Timeout] {nameof(SlowNetworkProbeModule)} probing (will exceed 500ms budget) ...");
            // 5s work vs. 500ms budget → this will time out and be marked Failed.
            await Task.Delay(5000, cancellationToken);
            Debug.Log($"[Timeout] {nameof(SlowNetworkProbeModule)} finished (not expected within budget).");
        }

        public override void Shutdown()
        {
            Debug.Log($"Module Shutdown: {nameof(SlowNetworkProbeModule)}");
        }
    }
}
