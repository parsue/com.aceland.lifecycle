using System.Collections.Generic;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// An immutable snapshot of a full initialization run: per-phase and per-module timings,
    /// produced by <see cref="LifecycleProfiler"/> and consumed by the Editor timeline window.
    /// All times are in milliseconds relative to the run origin (when the first phase started).
    /// </summary>
    public sealed class LifecycleTimeline
    {
        /// <summary>Per-phase timing summaries, in phase order.</summary>
        public IReadOnlyList<PhaseTimingInfo> Phases { get; }

        /// <summary>Per-module timing snapshots, in execution order (phase, then level, then sort index).</summary>
        public IReadOnlyList<ModuleTimingInfo> Modules { get; }

        /// <summary>Total wall-clock time of the run, in milliseconds (max module end time, or the
        /// initialization result time when available).</summary>
        public double TotalMs { get; }

        /// <summary>Number of modules that failed or were skipped.</summary>
        public int ProblemCount { get; }

        /// <summary>True when the run captured no module timings (profiler was disabled or nothing ran).</summary>
        public bool IsEmpty => Modules.Count == 0;

        internal LifecycleTimeline(IReadOnlyList<PhaseTimingInfo> phases,
                                   IReadOnlyList<ModuleTimingInfo> modules,
                                   double totalMs)
        {
            Phases = phases ?? System.Array.Empty<PhaseTimingInfo>();
            Modules = modules ?? System.Array.Empty<ModuleTimingInfo>();
            TotalMs = totalMs;

            var problems = 0;
            for (var i = 0; i < Modules.Count; i++)
                if (Modules[i].IsProblem) problems++;
            ProblemCount = problems;
        }

        /// <summary>An empty timeline (profiler disabled / no run yet).</summary>
        public static readonly LifecycleTimeline Empty = new(
            System.Array.Empty<PhaseTimingInfo>(),
            System.Array.Empty<ModuleTimingInfo>(),
            0d);
    }
}
