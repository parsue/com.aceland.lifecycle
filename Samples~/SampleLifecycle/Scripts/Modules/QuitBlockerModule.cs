using System;
using System.Threading;
using AceLand.Lifecycle;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts.Modules
{
    /// <summary>
    /// This is a sample Quit Blocker module.
    /// Quit Blocker will block the application quit pipeline,
    ///     until all jobs in blocker in completed.
    /// </summary>
    [LifecycleModule(ModulePhase.Core)]
    public sealed class QuitBlockerModule : ModuleBase, IQuitBlocker
    {
        private IDisposable _handle;
        private int _pending;

        public bool IsBusy => Volatile.Read(ref _pending) > 0;

        public string BusyReason =>
            $"Processing {_pending} job(s).\n" +
            "Do not force-quit — data may be irrecoverably damaged.";

        public override void Initialize()
        {
            _handle = ApplicationQuitPipeline.AddBlocker(this);
            
            Debug.Log($"Module Initialized: {nameof(QuitBlockerModule)} is Add to {nameof(ApplicationQuitPipeline)}");
        }

        public override void Shutdown() => _handle?.Dispose();

        /// <summary>
        /// Wrap real work in this. Blocks quit while any job is in flight.
        /// </summary>
        public IDisposable BeginJob()
        {
            Interlocked.Increment(ref _pending);
            return new JobScope(this);
        }

        private sealed class JobScope : IDisposable
        {
            private QuitBlockerModule _owner;
            public JobScope(QuitBlockerModule owner) => _owner = owner;

            public void Dispose()
            {
                if (_owner == null) return;
                Interlocked.Decrement(ref _owner._pending);
                _owner = null;
            }
        }
    }
}