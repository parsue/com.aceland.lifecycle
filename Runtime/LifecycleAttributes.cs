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

        public LifecycleModuleAttribute(ModulePhase phase) => Phase = phase;
    }

    /// <summary>
    /// Applied to an assembly to make the auto-scanner willing to scan it.
    /// Unmarked assemblies are never reflected over, which avoids scanning the whole project at startup.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
    public sealed class LifecycleAssemblyAttribute : Attribute { }
}