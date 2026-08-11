using System;
using System.Collections.Generic;
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
        private const float TIMEOUT_SECONDS = 30f;

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
                    LifecycleLog.Warning("Force quit requested — aborting graceful shutdown.");
                    IsReadyToQuit = true;
                    ModuleRegistry.ShutdownAll();
                    LifecycleToken.SignalDead();
                    return;   // Stop intercepting and let it leave Play Mode
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
                    LifecycleLog.Warning("Force quit requested — aborting graceful shutdown.");
                    IsReadyToQuit = true;
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

            var ctx = new QuitContext
            {
                Token = LifecycleToken.ApplicationAlive,
                StartedAtUtc = DateTime.UtcNow,
                TimeoutSeconds = TIMEOUT_SECONDS,
            };

            LifecycleToken.SignalQuitting();   // The game loop stops immediately; ApplicationAlive is still alive
            ctx.SetStatus("Pending to quit ...");

            try { QuitStarted?.Invoke(ctx); }
            catch (Exception ex) { LifecycleLog.Exception(ex); }

            await WaitForBlockers(ctx);

            var plan = BuildPlan();
            foreach (var t in plan)
                await RunHandler(t, ctx);

            ctx.SetStatus("Shutting down modules ...");
            ModuleRegistry.ShutdownAll();

            ctx.SetStatus("Safe to quit.");
            try { QuitCompleted?.Invoke(); }
            catch (Exception ex) { LifecycleLog.Exception(ex); }

            LifecycleToken.SignalDead();       // Only now do we let ApplicationAlive die
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

        private static async Task RunHandler(HandlerEntry entry, QuitContext ctx)
        {
            if (ctx.IsTimedOut)
            {
                LifecycleLog.Error($"Skip quit handler '{entry.Name}' — pipeline already timed out.");
                return;
            }

            try
            {
                var task = entry.Run(ctx);
                if (task == null) return;

                if (ctx.HasTimeout && !task.IsCompleted)
                {
                    var finished = await Task.WhenAny(task, Task.Delay(ctx.Remaining));
                    if (finished != task)
                    {
                        LifecycleLog.Error($"Quit handler '{entry.Name}' timed out; continuing.");
                        // Observe the abandoned task to avoid an UnobservedTaskException
                        _ = task.ContinueWith(t =>
                            {
                                if (t.Exception != null) LifecycleLog.Exception(t.Exception);
                            }, TaskScheduler.Default);
                        return;
                    }
                }

                await task;
            }
            catch (OperationCanceledException)
            {
                LifecycleLog.Warning($"Quit handler '{entry.Name}' cancelled.");
            }
            catch (Exception ex)
            {
                LifecycleLog.Exception(new Exception($"[Lifecycle] Quit handler '{entry.Name}' failed.", ex));
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
    }
}