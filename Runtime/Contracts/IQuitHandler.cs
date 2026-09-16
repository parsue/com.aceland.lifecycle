using System;
using System.Threading;
using System.Threading.Tasks;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// A module that needs asynchronous wrap-up work before the application really exits (or leaves Play Mode).
    /// <para>Any module implementing this interface is collected automatically and, by default, runs in the reverse order of initialization.</para>
    /// </summary>
    public interface IQuitHandler
    {
        Task OnBeforeQuitAsync(QuitContext context);
    }

    /// <summary>Adjusts the execution order of an <see cref="IQuitHandler"/>; lower runs first. Unmarked means 0 (keeps the reverse initialization order).</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class QuitOrderAttribute : Attribute
    {
        public int Order { get; }
        public QuitOrderAttribute(int order) => Order = order;
    }

    /// <summary>
    /// A "system is busy, don't quit yet" blocker. Before running any handler, the pipeline polls and waits until every blocker becomes idle.
    /// Equivalent to the original <c>Playground.IsFree</c>.
    /// </summary>
    public interface IQuitBlocker
    {
        bool IsBusy { get; }
        string BusyReason { get; }
    }

    /// <summary>The context passed to a quit handler.</summary>
    public sealed class QuitContext
    {
        /// <summary>The ApplicationAlive token — still alive during the shutdown pipeline, so it is safe to use with await.</summary>
        public CancellationToken Token { get; internal set; }

        public DateTime StartedAtUtc { get; internal set; }

        /// <summary>Overall timeout in seconds; &lt;= 0 means infinite.</summary>
        public float TimeoutSeconds { get; internal set; }

        public bool HasTimeout => TimeoutSeconds > 0f;
        public TimeSpan Elapsed => DateTime.UtcNow - StartedAtUtc;

        public TimeSpan Remaining => HasTimeout
            ? TimeSpan.FromSeconds(TimeoutSeconds) - Elapsed
            : TimeSpan.MaxValue;

        public bool IsTimedOut => HasTimeout && Remaining <= TimeSpan.Zero;

        /// <summary>The current status text (for UI / toast).</summary>
        public string Status { get; private set; }

        /// <summary>Updates the status and broadcasts <see cref="ApplicationQuitPipeline.StatusChanged"/>.</summary>
        public void SetStatus(string status)
        {
            Status = status;
            ApplicationQuitPipeline.RaiseStatus(status);
        }
    }
}