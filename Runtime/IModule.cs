using System.Threading;
using System.Threading.Tasks;

namespace AceLand.Lifecycle
{
    public interface IModule
    {
        /// <summary>Synchronous initialization. Should be fast; usually only registers services and sets up fields.</summary>
        void Initialize();

        /// <summary>Reverse cleanup. Called in the reverse order of initialization, and must be re-entrant (calling it multiple times must not break).</summary>
        void Shutdown();
    }

    /// <summary>
    /// A module that needs asynchronous initialization (loading assets, opening connections, …).
    /// <para>Execution order: <see cref="IModule.Initialize"/> first (lightweight, registers services),
    /// then await <see cref="InitializeAsync"/>; the module counts as Ready only once both have completed.</para>
    /// <para><b>Restriction:</b> a synchronous module must not depend on an asynchronous module (the Validator will report an error).
    /// Anyone who needs to wait for an async module must be an <see cref="IAsyncModule"/> as well.</para>
    /// </summary>
    public interface IAsyncModule : IModule
    {
        Task InitializeAsync(CancellationToken cancellationToken);
    }

    public abstract class ModuleBase : IModule
    {
        public virtual void Initialize() { }
        public virtual void Shutdown() { }
    }

    public abstract class AsyncModuleBase : ModuleBase, IAsyncModule
    {
        public abstract Task InitializeAsync(CancellationToken cancellationToken);
    }
}