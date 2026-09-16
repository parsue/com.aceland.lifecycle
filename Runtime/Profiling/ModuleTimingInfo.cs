using System;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// An immutable per-module timing snapshot captured by <see cref="LifecycleProfiler"/>.
    /// Unlike <see cref="ModuleEntry"/> (which the registry keeps mutating), this is a frozen copy
    /// safe to hand to Editor tooling. All times are in milliseconds relative to the run origin.
    /// </summary>
    public readonly struct ModuleTimingInfo
    {
        /// <summary>The module's registration id.</summary>
        public readonly Type Id;

        /// <summary>Friendly name (the id's type name).</summary>
        public readonly string DisplayName;

        public readonly ModulePhase Phase;

        /// <summary>Final settled state at snapshot time.</summary>
        public readonly ModuleState State;

        /// <summary>Dependency level (concurrency batch) within the phase; -1 if it never ran.</summary>
        public readonly int Level;

        /// <summary>Execution index within the phase; -1 if it never ran.</summary>
        public readonly int SortIndex;

        public readonly bool IsAsync;
        public readonly bool AllowParallel;

        /// <summary>Start time relative to the run origin; -1 if it never ran.</summary>
        public readonly double StartedAtMs;

        /// <summary>End time relative to the run origin; -1 if it never ran.</summary>
        public readonly double EndedAtMs;

        /// <summary>Time in the synchronous Initialize() call.</summary>
        public readonly double SyncMs;

        /// <summary>Time awaiting InitializeAsync(); 0 for sync modules.</summary>
        public readonly double AsyncMs;

        /// <summary>Total wall-clock init time (sync + async).</summary>
        public readonly double TotalMs;

        /// <summary>Error message if the module failed or was skipped; null otherwise.</summary>
        public readonly string Error;

        /// <summary>True when the module did not reach <see cref="ModuleState.Ready"/>.</summary>
        public bool IsProblem => State == ModuleState.Failed || State == ModuleState.Skipped;

        /// <summary>True when the module actually executed (has a valid start time).</summary>
        public bool DidRun => StartedAtMs >= 0;

        internal ModuleTimingInfo(ModuleEntry e)
        {
            Id = e.Id;
            DisplayName = e.DisplayName;
            Phase = e.Phase;
            State = e.State;
            Level = e.Level;
            SortIndex = e.SortIndex;
            IsAsync = e.IsAsync;
            AllowParallel = e.AllowParallel;
            StartedAtMs = e.StartedAtMs;
            EndedAtMs = e.EndedAtMs;
            SyncMs = e.SyncMilliseconds;
            AsyncMs = e.AsyncMilliseconds;
            TotalMs = e.InitMilliseconds;
            Error = e.Error;
        }

        public override string ToString() =>
            $"{DisplayName} ({Phase}, {State}) {TotalMs:0.0} ms @ {StartedAtMs:0.0}";
    }
}
