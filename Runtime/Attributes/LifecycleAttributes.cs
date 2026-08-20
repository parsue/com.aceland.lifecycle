using System;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Declares a module's phase, order and dependencies.
    /// <para>It is both the source of runtime auto-registration and the data source of the editor dependency graph.</para>
    /// <para>Use <c>typeof(...)</c> for dependencies: a code reference → the asmdef must reference it → package.json must declare the dependency,
    /// so all three stay consistent automatically.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class LifecycleModuleAttribute : Attribute
    {
        public ModulePhase Phase { get; }

        /// <summary>Tie-break within the same dependency level; lower runs first. Defaults to 0.</summary>
        public int Order { get; set; }

        /// <summary>The Ids of the modules depended upon (usually the other module's implementation type, or the interface type used at registration).</summary>
        public Type[] DependsOn { get; set; } = Type.EmptyTypes;

        /// <summary>The registration Id. Leave empty to use the annotated type itself. Used when exposing the module through an interface.</summary>
        public Type Id { get; set; }

        /// <summary>
        /// Whether <see cref="ModuleAutoScanner"/> automatically news it up and registers it (requires a public parameterless constructor).
        /// When set to false, this attribute is metadata only and you still have to call <see cref="ModuleRegistry.Register"/> yourself.
        /// </summary>
        public bool AutoRegister { get; set; } = true;

        /// <summary>
        /// Opt-in flag: when <c>true</c> this async module may be initialized concurrently with other
        /// same-dependency-level modules that are also marked <see cref="AllowParallel"/>.
        /// <para>Defaults to <c>false</c> — a purely synchronous / unmarked project keeps its exact
        /// sequential behaviour. Synchronous modules always run in order regardless of this flag.</para>
        /// </summary>
        public bool AllowParallel { get; set; }

        /// <summary>
        /// Per-module async initialization timeout in milliseconds. <c>0</c> (default) means no per-module limit.
        /// On timeout the module is marked <see cref="ModuleState.Failed"/> without blocking the rest of the phase.
        /// Overrides the phase-level timeout from <see cref="LifecyclePhaseOptionsAttribute"/> when non-zero.
        /// </summary>
        public int TimeoutMs { get; set; }

        public LifecycleModuleAttribute(ModulePhase phase) => Phase = phase;
    }

    /// <summary>
    /// Applied to an assembly to make the auto-scanner willing to scan it.
    /// Unmarked assemblies are never reflected over, which avoids scanning the whole project at startup.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
    public sealed class LifecycleAssemblyAttribute : Attribute { }

    /// <summary>
    /// Assembly-level phase tuning for parallel initialization, e.g.
    /// <c>[assembly: LifecyclePhaseOptions(ModulePhase.Runtime, Parallel = true, TimeoutMs = 5000)]</c>.
    /// <para><see cref="Parallel"/> sets the default <see cref="LifecycleModuleAttribute.AllowParallel"/> for
    /// every module in that phase (a module's own explicit attribute still wins).
    /// <see cref="TimeoutMs"/> sets the whole-phase budget; when exceeded the phase is forced forward and an
    /// issue is recorded, honouring the "never deadlock" philosophy.</para>
    /// <para>Multiple assemblies may each declare options; per phase the strictest wins (Parallel OR-ed on,
    /// the smallest non-zero TimeoutMs).</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = true)]
    public sealed class LifecyclePhaseOptionsAttribute : Attribute
    {
        public ModulePhase Phase { get; }

        /// <summary>Default <see cref="LifecycleModuleAttribute.AllowParallel"/> for modules in this phase.</summary>
        public bool Parallel { get; set; }

        /// <summary>Whole-phase timeout in milliseconds. <c>0</c> (default) means unlimited.</summary>
        public int TimeoutMs { get; set; }

        public LifecyclePhaseOptionsAttribute(ModulePhase phase) => Phase = phase;
    }
}