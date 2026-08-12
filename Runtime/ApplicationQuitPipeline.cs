using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AceLand.Lifecycle
{
    /// <summary>
    /// A unified "before quit" pipeline. Replaces the SafeToClose logic scattered all over the place.
    ///
    /// Execution order:
    ///   1. Cancel the Quitting token (the game loop stops working)
    ///   2. Wait until every IQuitBlocker becomes idle
    ///   3. Run the IQuitHandlers in order (default = reverse of initialization order)
    ///   4. ModuleRegistry.ShutdownAll()
    ///   5. Cancel the ApplicationAlive token → actually quit / exit Play Mode
    /// </summary>
    public static class ApplicationQuitPipeline
    {
        // ── Settings ────────────────────────────────────────────────────────

        /// <summary>Overall timeout (seconds). &lt;= 0 means infinite. After the timeout the pipeline forces its way forward so the app can never get stuck.</summary>
        internal const float TIMEOUT_SECONDS = 30f;

        /// <summary>Polling interval while waiting for blockers.</summary>
        private const int BLOCKER_POLL_MILLISECONDS = 50;

        /// <summary>While shutting down, whether a second quit request from the user is let through immediately (escape hatch against deadlocks).</summary>
        private const bool ALLOW_FORCE_QUIT_ON_SECOND_REQUEST = true;

        // ── State ───────────────────────────────────────────────────────────

        public static bool IsQuitting { get; private set; }
        public static bool IsReadyToQuit { get; private set; }
        public static string CurrentStatus { get; private set; }

        public static event Action<QuitContext> QuitStarted;
        public static event Action<string> StatusChanged;
        /// <summary>Raised once when a blocker holds up the quit; the argument is the BusyReason. Hook your toast here.</summary>
        public static event Action<string> QuitBlocked;
        public static event Action QuitCompleted;
        
        private static DateTime? s_CompletedAtUtc;

        public static bool IsActive => Phase is QuitPhase.WaitingForBlockers
            or QuitPhase.RunningHandlers
            or QuitPhase.ShuttingDown;

        public static bool HasResult => Phase is QuitPhase.Completed or QuitPhase.Forced;
        
        public static double ElapsedSeconds
        {
            get
            {
                if (!_startedAtUtc.HasValue) return 0d;
                var end = s_CompletedAtUtc ?? DateTime.UtcNow;
                return (end - _startedAtUtc.Value).TotalSeconds;
            }
        }
        
        // ── Registration ────────────────────────────────────────────────────

        internal sealed class HandlerEntry
        {
            public string Name;
            public int Order;
            public Func<QuitContext, Task> Run;
            public bool FromModule;
        }

        private static readonly List<HandlerEntry> manual = new();
        private static readonly List<IQuitBlocker> blockers = new();
        private static bool _installed;
        private static int _requestCount;

        /// <summary>Registers a before-quit job. Dispose the returned IDisposable to unregister.</summary>
        public static IDisposable AddHandler(Func<QuitContext, Task> handler, int order = 0, string name = null)
        {
            if (handler == null) return Disposable.Empty;

            var entry = new HandlerEntry
            {
                Name = name ?? handler.Method.DeclaringType?.Name ?? "anonymous",
                Order = order,
                Run = handler,
            };
            manual.Add(entry);
            return new Disposable(() => manual.Remove(entry));
        }

        /// <summary>Synchronous overload.</summary>
        public static IDisposable AddHandler(Action<QuitContext> handler, int order = 0, string name = null)
        {
            if (handler == null) return Disposable.Empty;
            return AddHandler(ctx => { handler(ctx); return Task.CompletedTask; }, order, name);
        }

        public static IDisposable AddBlocker(IQuitBlocker blocker)
        {
            if (blocker == null) return Disposable.Empty;
            blockers.Add(blocker);
            return new Disposable(() => blockers.Remove(blocker));
        }

        /// <summary>
        /// Scope-style busy flag, replacing <c>Playground.SetBusy(...)</c>:
        /// <code>using (ApplicationQuitPipeline.Busy("Saving scene...")) { await Save(); }</code>
        /// </summary>
        public static IDisposable Busy(string reason)
        {
            var scope = new BusyScope(reason);
            blockers.Add(scope);
            scope.OnDispose = () => blockers.Remove(scope);
            return scope;
        }

        /// <summary>Whether every blocker is currently idle. Equivalent to the original Playground.IsFree.</summary>
        public static bool IsFree
        {
            get
            {
                foreach (var t in blockers)
                    if (t is { IsBusy: true }) return false;

                return true;
            }
        }

        public static string BusyReason
        {
            get
            {
                foreach (var t in blockers)
                    if (t is { IsBusy: true })
                        return t.BusyReason ?? "busy";

                return null;
            }
        }

        // ── Public trigger ──────────────────────────────────────────────────

        /// <summary>A unified quit entry point that works in both the Editor and a Player build.</summary>
        public static void Quit()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;   // Goes through ExitingPlayMode → intercepted by us
#else
            Application.Quit();                     // Goes through wantsToQuit → intercepted by us
#endif
        }

        internal static void RaiseStatus(string status)
        {
            CurrentStatus = status;
            LifecycleLog.Info($"Quit: {status}");
            try { StatusChanged?.Invoke(status); }
            catch (Exception ex) { LifecycleLog.Exception(ex); }
        }

        // ── Installing hooks ────────────────────────────────────────────────

        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
            Application.wantsToQuit -= OnWantsToQuit;
            Application.wantsToQuit += OnWantsToQuit;
        }

        internal static void Uninstall()
        {
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
            Application.wantsToQuit -= OnWantsToQuit;
            _installed = false;
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode) return;
            if (IsReadyToQuit) return;

            if (IsQuitting)
            {
                _requestCount++;
                if (ALLOW_FORCE_QUIT_ON_SECOND_REQUEST && _requestCount >= 2)
                {
                    MarkForced();          // ★ was: Warning + IsReadyToQuit + ShutdownAll + SignalDead
                    return;                // don't intercept — let it leave Play Mode
                }
            }

            // Intercept: pull Play Mode back and only let go once the pipeline has finished
            EditorApplication.isPlaying = true;
            if (IsQuitting) return;

            _requestCount = 1;
            _ = RunThenStopEditor();
        }

        private static async Task RunThenStopEditor()
        {
            try { await RunAsync(); }
            catch (Exception ex) { LifecycleLog.Exception(ex); }
            finally
            {
                IsReadyToQuit = true;
                EditorApplication.isPlaying = false;
            }
        }
#endif

        private static bool OnWantsToQuit()
        {
            if (IsReadyToQuit) return true;

            if (IsQuitting)
            {
                _requestCount++;
                if (ALLOW_FORCE_QUIT_ON_SECOND_REQUEST && _requestCount >= 2)
                {
                    MarkForced();          // ★ was: Warning + IsReadyToQuit only
                    return true;
                }
                return false;
            }

            _requestCount = 1;
            _ = RunThenQuitPlayer();
            return false;
        }

        private static async Task RunThenQuitPlayer()
        {
            try { await RunAsync(); }
            catch (Exception ex) { LifecycleLog.Exception(ex); }
            finally
            {
                IsReadyToQuit = true;
                Application.Quit();
            }
        }

        // ── Main pipeline ───────────────────────────────────────────────────

        private static async Task RunAsync()
        {
            if (IsQuitting) return;
            IsQuitting = true;

            _startedAtUtc = DateTime.UtcNow;
            s_CompletedAtUtc = null;
            CurrentHandler = null;

            var ctx = new QuitContext
            {
                Token = LifecycleToken.ApplicationAlive,
                StartedAtUtc = _startedAtUtc.Value,
                TimeoutSeconds = TIMEOUT_SECONDS,
            };

            LifecycleToken.SignalQuitting();
            ctx.SetStatus("Pending to quit ...");

            try { QuitStarted?.Invoke(ctx); }
            catch (Exception ex) { LifecycleLog.Exception(ex); }

            // Freeze the plan so the panel shows a stable list.
            var plan = BuildPlan();
            _activeSteps = new List<QuitStepInfo>(plan.Count);
            foreach (var e in plan)
                _activeSteps.Add(new QuitStepInfo
                {
                    Name = e.Name,
                    Order = e.Order,
                    FromModule = e.FromModule,
                    State = QuitStepState.Pending,
                });

            Phase = QuitPhase.WaitingForBlockers;
            await WaitForBlockers(ctx);

            Phase = QuitPhase.RunningHandlers;
            for (var i = 0; i < plan.Count; i++)
                await RunHandler(plan[i], ctx, _activeSteps[i]);

            CurrentHandler = null;
            Phase = QuitPhase.ShuttingDown;
            ctx.SetStatus("Shutting down modules ...");
            ModuleRegistry.ShutdownAll();

            Phase = QuitPhase.Completed;
            s_CompletedAtUtc = DateTime.UtcNow;
            ctx.SetStatus("Safe to quit.");

            try { QuitCompleted?.Invoke(); }
            catch (Exception ex) { LifecycleLog.Exception(ex); }

            LifecycleToken.SignalDead();
        }

        private static async Task WaitForBlockers(QuitContext ctx)
        {
            if (IsFree) return;

            var reason = BusyReason;
            LifecycleLog.Info($"Quit blocked: {reason}");
            ctx.SetStatus(reason);

            try { QuitBlocked?.Invoke(reason); }
            catch (Exception ex) { LifecycleLog.Exception(ex); }

            while (!IsFree)
            {
                if (ctx.IsTimedOut)
                {
                    LifecycleLog.Error($"Quit blocker timed out after {ctx.TimeoutSeconds}s: {BusyReason}");
                    return;
                }

                var current = BusyReason;
                if (current != null && current != ctx.Status) ctx.SetStatus(current);

                try { await Task.Delay(BLOCKER_POLL_MILLISECONDS, ctx.Token); }
                catch (OperationCanceledException) { return; }
            }
        }

        private static async Task RunHandler(HandlerEntry entry, QuitContext ctx, QuitStepInfo step)
        {
            if (ctx.IsTimedOut)
            {
                step.State = QuitStepState.Skipped;
                LifecycleLog.Error($"Skip quit handler '{entry.Name}' — pipeline already timed out.");
                return;
            }

            CurrentHandler = entry.Name;
            step.State = QuitStepState.Running;

            LifecycleLog.Info($"Quit handler ► {entry.Name}");
            var sw = Stopwatch.StartNew();

            try
            {
                var task = entry.Run(ctx);
                if (task == null) { step.State = QuitStepState.Done; return; }

                if (ctx.HasTimeout && !task.IsCompleted)
                {
                    var finished = await Task.WhenAny(task, Task.Delay(ctx.Remaining));
                    if (finished != task)
                    {
                        step.State = QuitStepState.TimedOut;
                        LifecycleLog.Error($"Quit handler '{entry.Name}' timed out; continuing.");
                        _ = task.ContinueWith(t =>
                        {
                            if (t.Exception != null) LifecycleLog.Exception(t.Exception);
                        }, TaskScheduler.Default);
                        return;
                    }
                }

                await task;
                step.State = QuitStepState.Done;
            }
            catch (OperationCanceledException)
            {
                step.State = QuitStepState.Skipped;
                LifecycleLog.Warning($"Quit handler '{entry.Name}' cancelled.");
            }
            catch (Exception ex)
            {
                step.State = QuitStepState.Failed;
                LifecycleLog.Exception(new Exception($"[Lifecycle] Quit handler '{entry.Name}' failed.", ex));
            }
            finally
            {
                sw.Stop();
                step.Milliseconds = sw.Elapsed.TotalMilliseconds;
                LifecycleLog.Info($"Quit handler ◄ {entry.Name}  ({sw.ElapsedMilliseconds} ms)");
            }
        }

        /// <summary>Default = reverse of the module initialization order, then a stable sort by QuitOrder.</summary>
        internal static List<HandlerEntry> BuildPlan()
        {
            var list = new List<HandlerEntry>();

            var started = ModuleRegistry.StartedEntries;
            for (var i = started.Count - 1; i >= 0; i--)
            {
                if (!(started[i].Module is IQuitHandler h)) continue;

                var attr = (QuitOrderAttribute)Attribute.GetCustomAttribute(
                    started[i].Module.GetType(), typeof(QuitOrderAttribute));

                list.Add(new HandlerEntry
                {
                    Name = started[i].DisplayName,
                    Order = attr?.Order ?? 0,
                    Run = h.OnBeforeQuitAsync,
                    FromModule = true,
                });
            }

            list.AddRange(manual);

            // OrderBy is a stable sort → entries with the same Order keep the "reverse initialization" order
            var sorted = new List<HandlerEntry>(list.Count);
            foreach (var e in SortStable(list)) sorted.Add(e);
            return sorted;
        }

        private static IEnumerable<HandlerEntry> SortStable(List<HandlerEntry> list)
        {
            var buckets = new SortedDictionary<int, List<HandlerEntry>>();
            foreach (var e in list)
            {
                if (!buckets.TryGetValue(e.Order, out var b)) buckets[e.Order] = b = new List<HandlerEntry>();
                b.Add(e);
            }
            foreach (var kv in buckets)
                foreach (var e in kv.Value)
                    yield return e;
        }

        /// <summary>Inspection data for the Editor window.</summary>
        public static IReadOnlyList<(string name, int order, bool fromModule)> GetPlan()
        {
            var plan = BuildPlan();
            var result = new List<(string, int, bool)>(plan.Count);
            foreach (var e in plan) result.Add((e.Name, e.Order, e.FromModule));
            return result;
        }

        public static IReadOnlyList<IQuitBlocker> Blockers => blockers;

        internal static void ResetStatics()
        {
            Uninstall();
            manual.Clear();
            blockers.Clear();
            IsQuitting = false;
            IsReadyToQuit = false;
            CurrentStatus = null;
            _requestCount = 0;
            QuitStarted = null;
            StatusChanged = null;
            QuitBlocked = null;
            QuitCompleted = null;
            _activeSteps = null;
            _startedAtUtc = null;
            s_CompletedAtUtc = null;
            Phase = QuitPhase.Idle;
            CurrentHandler = null;
        }
        
        /// <summary>
        /// Clears the diagnostics of a finished run. Does nothing while a quit is in flight.
        /// Registrations and event subscriptions are untouched.
        /// </summary>
        public static void ClearDiagnostics()
        {
            if (IsActive) return;

            _activeSteps = null;
            _startedAtUtc = null;
            s_CompletedAtUtc = null;
            Phase = QuitPhase.Idle;
            CurrentHandler = null;
            CurrentStatus = null;
            IsQuitting = false;
            IsReadyToQuit = false;
            _requestCount = 0;
        }

        // ── helpers ────────────────────────────────────────────────────────

        private sealed class BusyScope : IQuitBlocker, IDisposable
        {
            public Action OnDispose;
            private bool _disposed;

            public BusyScope(string reason) => BusyReason = reason;

            public bool IsBusy => !_disposed;
            public string BusyReason { get; }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                OnDispose?.Invoke();
            }
        }

        private sealed class Disposable : IDisposable
        {
            public static readonly Disposable Empty = new(null);
            private readonly Action _action;
            public Disposable(Action action) => _action = action;
            public void Dispose() => _action?.Invoke();
        }
        
        /// <summary>
        /// Abandons a graceful shutdown in progress. Cancels the alive token first so
        /// in-flight handlers unwind, then tears modules down.
        /// </summary>
        private static void MarkForced()
        {
            LifecycleLog.Warning("Force quit requested — aborting graceful shutdown.");

            IsReadyToQuit = true;
            Phase = QuitPhase.Forced;
            s_CompletedAtUtc = DateTime.UtcNow;
            CurrentHandler = null;

            LifecycleToken.SignalDead();
            ModuleRegistry.ShutdownAll();
        }
        
        // ── Diagnostics types ───────────────────────────────────────────────

        public enum QuitPhase
        {
            Idle = 0,
            WaitingForBlockers,
            RunningHandlers,
            ShuttingDown,
            Completed,
            Forced,
        }

        public enum QuitStepState
        {
            Pending = 0,
            Running,
            Done,
            Failed,
            TimedOut,
            Skipped,
        }

        public sealed class QuitStepInfo
        {
            public string Name;
            public int Order;
            public bool FromModule;
            public QuitStepState State;
            public double Milliseconds;
        }

        public sealed class QuitBlockerInfo
        {
            public IQuitBlocker Blocker;
            public string Name;
            public bool IsBusy;
            public string Reason;
        }

        // ── Diagnostics state ───────────────────────────────────────────────

        private static List<QuitStepInfo> _activeSteps;
        private static DateTime? _startedAtUtc;

        public static QuitPhase Phase { get; private set; } = QuitPhase.Idle;
        public static string CurrentHandler { get; private set; }

        /// <summary>Remaining budget in seconds; -1 when unlimited.</summary>
        public static double RemainingSeconds
        {
            get
            {
                if (TIMEOUT_SECONDS <= 0f) return -1d;
                return Math.Max(0d, TIMEOUT_SECONDS - ElapsedSeconds);
            }
        }

        /// <summary>True when the given instance is currently registered as a blocker.</summary>
        public static bool IsBlockerRegistered(object instance)
        {
            if (instance == null) return false;
            for (var i = 0; i < blockers.Count; i++)
                if (ReferenceEquals(blockers[i], instance)) return true;
            return false;
        }

        /// <summary>Blocker snapshot. User property exceptions are contained.</summary>
        public static IReadOnlyList<QuitBlockerInfo> GetBlockers()
        {
            var result = new List<QuitBlockerInfo>(blockers.Count);

            foreach (var b in blockers)
            {
                if (b == null) continue;

                var info = new QuitBlockerInfo { Blocker = b, Name = b.GetType().Name };

                try { info.IsBusy = b.IsBusy; }
                catch (Exception ex) { info.Reason = $"IsBusy threw: {ex.Message}"; }

                if (info.Reason == null)
                {
                    try { info.Reason = b.BusyReason; }
                    catch (Exception ex) { info.Reason = $"BusyReason threw: {ex.Message}"; }
                }

                result.Add(info);
            }

            return result;
        }

        /// <summary>
        /// The handler plan. While quitting this is the frozen plan with live per-step state;
        /// otherwise it is a fresh preview with every step Pending.
        /// </summary>
        public static IReadOnlyList<QuitStepInfo> GetSteps()
        {
            if (_activeSteps != null) return _activeSteps;

            var plan = BuildPlan();
            var preview = new List<QuitStepInfo>(plan.Count);
            foreach (var e in plan)
                preview.Add(new QuitStepInfo
                {
                    Name = e.Name,
                    Order = e.Order,
                    FromModule = e.FromModule,
                    State = QuitStepState.Pending,
                });

            return preview;
        }
    }
}