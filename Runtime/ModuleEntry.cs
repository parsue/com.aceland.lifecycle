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

        public ModuleState State { get; internal set; } = ModuleState.Registered;
        public string Error { get; internal set; }
        public double InitMilliseconds { get; internal set; }

        /// <summary>The actual execution index (within the phase); -1 if it has not run.</summary>
        public int SortIndex { get; internal set; } = -1;

        public string DisplayName => Id != null ? Id.Name : "<null>";

        public override string ToString() => $"{DisplayName} ({Phase}, {State})";
    }
}