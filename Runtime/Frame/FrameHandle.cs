using System;
using System.Threading;
using UnityEngine;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Diagnostic state of a scheduled frame process, as surfaced by the PlayerLoop editor window.
    /// </summary>
    internal enum FrameProcessState
    {
        /// <summary>Scheduled but not yet executed (AfterFrames counting down, When predicate unmet, Delayed counting down).</summary>
        Waiting,
        /// <summary>Executing per-frame (EveryFrame, at least one execution).</summary>
        Running,
        /// <summary>An exception was thrown during Advance / action / predicate. Retained briefly, then removed.</summary>
        Error,
        /// <summary>Finished normally. Removed from the scheduler immediately.</summary>
        Completed,
    }

    /// <summary>
    /// Concrete, scheduler-owned implementation of <see cref="IFrameHandle"/>.
    /// <para>
    /// A handle encapsulates one unit of frame-scheduled work plus its evaluation strategy
    /// (<see cref="Kind"/>). The owning <see cref="FrameScheduler"/> calls <see cref="Advance"/>
    /// once per frame at the handle's <see cref="Point"/>; the handle decides when to run its action
    /// and reports back whether it is finished (and should be removed).
    /// </para>
    /// <para>
    /// <b>Thread affinity / insurance:</b> all state transitions that invoke user code or continuations
    /// happen on the Unity main thread inside <see cref="Advance"/> / <see cref="ForceCancel"/>.
    /// Cancellation coming from a <see cref="CancellationToken"/> or <see cref="Dispose"/> only flips a
    /// flag; the actual completion (and continuation resume) is deferred to the next main-thread pump,
    /// so awaited code always resumes on the Unity thread. The scheduler force-cancels every live handle
    /// on reset (quit / play-mode exit / assembly reload), so no awaiter is ever left dangling.
    /// </para>
    /// <para>
    /// <b>Diagnostics:</b> the handle exposes a coarse <see cref="State"/> (waiting / running / error /
    /// completed) plus <see cref="MethodLabel"/> / <see cref="OwnerType"/> so the PlayerLoop editor
    /// window can draw it as a node. A handle whose action threw is kept in its bucket for
    /// <see cref="ErrorRetainSeconds"/> so the error node is visible before it disappears; its awaiter
    /// is still resolved (cancelled) immediately.
    /// </para>
    /// </summary>
    internal sealed class FrameHandle : IFrameHandle
    {
        internal enum Kind
        {
            AfterFrames,
            When,
            EveryFrame,
            Delayed,
        }

        /// <summary>How long an errored handle stays visible in the scheduler before removal.</summary>
        private const float ErrorRetainSeconds = 5f;

        /// <summary>Monotonic source for <see cref="Id"/>; guarantees a stable unique identity per handle.</summary>
        private static long _idCounter;

        /// <summary>Stable, process-unique diagnostic id. Lets the editor windows track a specific
        /// handle across rebuilds even when several handles share the same method / owner.</summary>
        private readonly long _id = System.Threading.Interlocked.Increment(ref _idCounter);

        private readonly Kind _kind;
        private readonly Action _action;
        private readonly Func<bool> _predicate;
        private readonly bool _unscaled;
        private readonly int _maxFrames;   // EveryFrame upper bound; -1 = unbounded

        private int _framesRemaining;      // AfterFrames countdown (skips before the run)
        private int _framesRun;            // EveryFrame executed count
        private float _secondsRemaining;   // Delayed countdown

        private readonly PlayerLoopPoint _point;
        private readonly string _methodLabel;
        private readonly Type _ownerType;

        private CancellationTokenRegistration _ctReg;
        private LinkedTokenSource _owned;  // optional linked source this handle owns / disposes

        private Action _continuation;
        private volatile bool _cancelRequested;
        private bool _completed;
        private bool _cancelled;

        private FrameProcessState _state = FrameProcessState.Waiting;
        private float _errorRetainRemaining;

        private FrameHandle(Kind kind, Action action, Func<bool> predicate,
            int framesRemaining, int maxFrames, float seconds, bool unscaled,
            PlayerLoopPoint point, CancellationToken token, LinkedTokenSource owned)
        {
            _kind = kind;
            _action = action;
            _predicate = predicate;
            _framesRemaining = framesRemaining;
            _maxFrames = maxFrames;
            _secondsRemaining = seconds;
            _unscaled = unscaled;
            _point = point;
            _owned = owned;

            (_methodLabel, _ownerType) = DescribeMethod(action);

            if (token.CanBeCanceled)
                _ctReg = token.Register(static state => ((FrameHandle)state)._cancelRequested = true, this);
        }

        // ── Factories (created here, registered by FrameScheduler) ──────────

        internal static FrameHandle AfterFrames(Action action, int frames, PlayerLoopPoint point,
            CancellationToken token = default, LinkedTokenSource owned = null)
            => new(Kind.AfterFrames, action, null,
                Math.Max(0, frames - 1), -1, 0f, false, point, token, owned);

        internal static FrameHandle When(Func<bool> predicate, Action action, PlayerLoopPoint point,
            CancellationToken token = default, LinkedTokenSource owned = null)
            => new(Kind.When, action, predicate,
                0, -1, 0f, false, point, token, owned);

        internal static FrameHandle EveryFrame(Action action, int? maxFrames, PlayerLoopPoint point,
            CancellationToken token = default, LinkedTokenSource owned = null)
            => new(Kind.EveryFrame, action, null,
                0, maxFrames.HasValue ? Math.Max(1, maxFrames.Value) : -1, 0f, false, point, token, owned);

        internal static FrameHandle Delayed(Action action, float seconds, bool unscaled, PlayerLoopPoint point,
            CancellationToken token = default, LinkedTokenSource owned = null)
            => new(Kind.Delayed, action, null,
                0, -1, Mathf.Max(0f, seconds), unscaled, point, token, owned);

        // ── IFrameHandle ────────────────────────────────────────────────────

        internal PlayerLoopPoint Point => _point;

        public bool IsCompleted => _completed;
        public bool IsCancelled => _cancelled;

        internal bool IsDone => _completed || _cancelled;

        // ── Diagnostics (PlayerLoop window) ─────────────────────────────────

        internal long Id => _id;
        internal Kind ProcessKind => _kind;
        internal FrameProcessState State => _state;
        internal string MethodLabel => _methodLabel;
        internal Type OwnerType => _ownerType;

        public FrameHandleAwaiter GetAwaiter() => new(this);

        internal void RegisterContinuation(Action continuation)
        {
            if (continuation == null) return;
            if (IsDone) { continuation(); return; }
            _continuation += continuation;
        }

        public void Dispose()
        {
            if (IsDone) return;
            _cancelRequested = true; // resolved on the next main-thread pump (see Advance)
        }

        // ── Scheduler-driven evaluation (main thread) ───────────────────────

        /// <summary>
        /// Advances the handle one frame at its <see cref="Point"/>. Returns <c>true</c> when the handle
        /// is finished and should be removed from the scheduler. Must be called on the main thread.
        /// </summary>
        internal bool Advance()
        {
            // Errored handles linger so the PlayerLoop window can show the failure, then are removed.
            if (_state == FrameProcessState.Error)
            {
                _errorRetainRemaining -= Time.unscaledDeltaTime;
                return _errorRetainRemaining <= 0f;
            }

            if (_cancelRequested && !IsDone)
            {
                Finish(cancelled: true);
                return true;
            }

            if (IsDone) return true;

            switch (_kind)
            {
                case Kind.AfterFrames:
                    if (_framesRemaining <= 0)
                    {
                        RunAndComplete();
                        return _state != FrameProcessState.Error; // errored → retain one more tick
                    }
                    _framesRemaining--;
                    return false;

                case Kind.When:
                    bool met;
                    try
                    {
                        met = _predicate != null && _predicate();
                    }
                    catch (Exception e)
                    {
                        LifecycleLog.Exception(e);
                        EnterError();
                        return false;
                    }
                    if (met)
                    {
                        RunAndComplete();
                        return _state != FrameProcessState.Error;
                    }
                    return false;

                case Kind.EveryFrame:
                    _state = FrameProcessState.Running;
                    if (!RunActionSafe()) return false; // errored → retain
                    _framesRun++;
                    if (_maxFrames > 0 && _framesRun >= _maxFrames)
                    {
                        _state = FrameProcessState.Completed;
                        Finish(cancelled: false);
                        return true;
                    }
                    return false;

                case Kind.Delayed:
                    _secondsRemaining -= _unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                    if (_secondsRemaining <= 0f)
                    {
                        RunAndComplete();
                        return _state != FrameProcessState.Error;
                    }
                    return false;

                default:
                    Finish(cancelled: true);
                    return true;
            }
        }

        /// <summary>Force-cancels the handle on the main thread (scheduler reset / shutdown).</summary>
        internal void ForceCancel()
        {
            if (IsDone) return;
            Finish(cancelled: true);
        }

        // ── Internals ───────────────────────────────────────────────────────

        private void RunAndComplete()
        {
            if (!RunActionSafe()) return; // error path already handled by EnterError
            _state = FrameProcessState.Completed;
            Finish(cancelled: false);
        }

        /// <summary>Runs the action, returning <c>false</c> and entering the error state on failure.</summary>
        private bool RunActionSafe()
        {
            try
            {
                _action?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                LifecycleLog.Exception(e);
                EnterError();
                return false;
            }
        }

        /// <summary>
        /// Resolves the awaiter immediately (cancelled) but flags the handle as errored so it is retained
        /// in its scheduler bucket for <see cref="ErrorRetainSeconds"/> before removal.
        /// </summary>
        private void EnterError()
        {
            _state = FrameProcessState.Error;
            _errorRetainRemaining = ErrorRetainSeconds;
            Finish(cancelled: true);
        }

        private void Finish(bool cancelled)
        {
            if (_completed || _cancelled) return;

            if (cancelled) _cancelled = true;
            else _completed = true;

            _ctReg.Dispose();
            _owned?.Dispose();
            _owned = null;

            var continuation = _continuation;
            _continuation = null;
            continuation?.Invoke();
        }

        /// <summary>
        /// Derives a human-readable "Type.Method" label and the owning root type (for open-script) from
        /// the scheduled action. Lambdas / local functions resolve to their compiler-generated method
        /// name but the owner walks up to the declaring (non-nested) type so navigation still works.
        /// </summary>
        private static (string label, Type owner) DescribeMethod(Action action)
        {
            var method = action?.Method;
            if (method == null) return ("(anonymous)", null);

            var owner = method.DeclaringType;
            var root = owner;
            while (root is { IsNested: true, DeclaringType: not null })
                root = root.DeclaringType;

            var typeName = (root ?? owner)?.Name ?? "?";
            return ($"{typeName}.{method.Name}", root ?? owner);
        }
    }
}
