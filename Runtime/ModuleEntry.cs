using System;

namespace AceLand.Lifecycle
{
    /// <summary>A single runtime module registration. Read-only to the outside; only the registry can modify it.</summary>
    public sealed class ModuleEntry
    {
        public Type Id { get; internal set; }
        public IModule Module { get; internal set; }
        public ModulePhase Phase { get; internal set; }
        public int Order { get; internal set; }
        public Type[] DependsOn { get; internal set; } = Type.EmptyTypes;
        public bool IsAsync { get; internal set; }
        public bool AutoRegistered { get; internal set; }

        /// <summary>Opt-in flag from <see cref="LifecycleModuleAttribute.AllowParallel"/> (or the phase default). Only meaningful for async modules.</summary>
        public bool AllowParallel { get; internal set; }

        /// <summary>Per-module async timeout in milliseconds; <c>0</c> means no per-module limit.</summary>
        public int TimeoutMs { get; internal set; }

        public ModuleState State { get; internal set; } = ModuleState.Registered;
        public string Error { get; internal set; }

        /// <summary>Total wall-clock time spent initializing this module (sync + async), in milliseconds.</summary>
        public double InitMilliseconds { get; internal set; }

        /// <summary>Time spent in the synchronous <see cref="IModule.Initialize"/> call, in milliseconds. Part of <see cref="InitMilliseconds"/>.</summary>
        public double SyncMilliseconds { get; internal set; }

        /// <summary>Time spent awaiting <see cref="IAsyncModule.InitializeAsync"/>, in milliseconds; <c>0</c> for pure sync modules. Part of <see cref="InitMilliseconds"/>.</summary>
        public double AsyncMilliseconds { get; internal set; }

        /// <summary>When this module started initializing, in milliseconds relative to the run origin (see <see cref="LifecycleProfiler"/>); <c>-1</c> if it never ran.</summary>
        public double StartedAtMs { get; internal set; } = -1;

        /// <summary>When this module finished settling (Ready / Failed / Skipped), in milliseconds relative to the run origin; <c>-1</c> if it never ran.</summary>
        public double EndedAtMs { get; internal set; } = -1;

        /// <summary>The actual execution index (within the phase); -1 if it has not run.</summary>
        public int SortIndex { get; internal set; } = -1;

        /// <summary>The dependency level (layer) within the phase after topological sorting; -1 if not yet assigned. Modules in the same level have no ordering dependency between them.</summary>
        public int Level { get; internal set; } = -1;

        public string DisplayName => Id != null ? Id.Name : "<null>";

        public override string ToString() => $"{DisplayName} ({Phase}, {State})";
    }
}