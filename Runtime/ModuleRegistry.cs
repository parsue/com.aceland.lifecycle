using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// The module registry. Attributes only feed modules in here; the real execution order is decided by the topological sort in this class.
    /// </summary>
    public static class ModuleRegistry
    {
        private static readonly List<ModuleEntry> entries = new();
        private static readonly Dictionary<Type, ModuleEntry> byId = new();
        private static readonly List<ModuleEntry> started = new();
        private static readonly List<string> issues = new();

        private static readonly Dictionary<Type, List<Action<IModule>>> readyCallbacks = new();
        internal static IReadOnlyList<ModuleEntry> StartedEntries => started;

        private static Task _chain = Task.CompletedTask;
        private static bool _scanned;
        private static bool _shuttingDown;

        /// <summary>
        /// State change notification. <b>Never replayed</b> — subscribing in Start() misses every event from the Core / Runtime phases.
        /// To obtain a module use <see cref="WhenReady{T}"/>; to observe everything use <see cref="ObserveStates"/>.
        /// </summary>
        public static event Action<ModuleEntry> ModuleStateChanged;

        /// <summary>A Task that completes when the entire initialization chain (including async modules) is done.</summary>
        public static Task Ready => _chain;

        public static IReadOnlyList<ModuleEntry> Entries => entries;
        public static IReadOnlyList<string> Issues => issues;

        // ── Registration ────────────────────────────────────────────────────

        /// <summary>Registers using the module's own type as the Id. Unspecified parameters are read from <see cref="LifecycleModuleAttribute"/>.</summary>
        public static void Register(IModule module,
                                    ModulePhase? phase = null,
                                    Type[] dependsOn = null,
                                    int? order = null)
            => Register(module?.GetType(), module, phase, dependsOn, order, autoRegistered: false);

        /// <summary>Registers using <typeparamref name="TId"/> (usually the public-facing interface) as the Id.</summary>
        public static void Register<TId>(IModule module,
                                         ModulePhase? phase = null,
                                         Type[] dependsOn = null,
                                         int? order = null)
            => Register(typeof(TId), module, phase, dependsOn, order, autoRegistered: false);

        internal static void Register(Type id, IModule module, ModulePhase? phase,
                                      Type[] dependsOn, int? order, bool autoRegistered)
        {
            if (module == null) { LifecycleLog.Error("Register(null) ignored."); return; }
            id ??= module.GetType();

            var attr = (LifecycleModuleAttribute)Attribute.GetCustomAttribute(
                module.GetType(), typeof(LifecycleModuleAttribute));

            if (attr?.Id != null && id == module.GetType()) id = attr.Id;

            if (phase == null)
            {
                if (attr == null)
                {
                    LifecycleLog.Error($"{module.GetType().Name}: phase not specified and no [LifecycleModule].");
                    return;
                }
                phase = attr.Phase;
            }

            var deps = dependsOn ?? attr?.DependsOn ?? Type.EmptyTypes;
            var ord = order ?? attr?.Order ?? 0;

            if (byId.TryGetValue(id, out var existing))
            {
                // A manual registration may override the auto-scanned result; anything else counts as a duplicate.
                if (existing.AutoRegistered && !autoRegistered)
                {
                    entries.Remove(existing);
                    byId.Remove(id);
                }
                else
                {
                    if (!autoRegistered)
                        LifecycleLog.Warning($"Duplicate registration for '{id.Name}' ignored.");
                    return;
                }
            }

            var entry = new ModuleEntry
            {
                Id = id,
                Module = module,
                Phase = phase.Value,
                Order = ord,
                DependsOn = deps,
                IsAsync = module is IAsyncModule,
                AutoRegistered = autoRegistered,
                State = ModuleState.Registered,
            };

            entries.Add(entry);
            byId[id] = entry;
            ModuleStateChanged?.Invoke(entry);
        }

        // ── Queries ─────────────────────────────────────────────────────────

        public static bool IsReady<T>() => IsReady(typeof(T));

        public static bool IsReady(Type id)
            => id != null && byId.TryGetValue(id, out var e) && e.State == ModuleState.Ready;

        public static bool TryGet<T>(out T module) where T : class
        {
            if (byId.TryGetValue(typeof(T), out var e) && e.State == ModuleState.Ready)
            {
                module = e.Module as T;
                return module != null;
            }
            module = null;
            return false;
        }

        /// <summary>Gets a module, throwing if it is not ready. Use this where the caller guarantees the dependency has been declared.</summary>
        public static T Get<T>() where T : class
        {
            if (TryGet<T>(out var m)) return m;
            throw new InvalidOperationException(
                $"[Lifecycle] Module '{typeof(T).Name}' is not ready. " +
                "Did you declare it in DependsOn?");
        }
        
        // ── Ready callbacks ─────────────────────────────────────────────────

        /// <summary>
        /// Invokes the callback <b>immediately and synchronously</b> if the module is already Ready; otherwise queues it until then.
        /// <para>This is the correct way to obtain a module from a MonoBehaviour — you don't have to worry about subscription timing.</para>
        /// </summary>
        /// <returns>Used to cancel the wait. Returns an empty disposable when it has already run.</returns>
        public static IDisposable WhenReady<T>(Action<T> callback) where T : class
        {
            if (callback == null) return Disposable.Empty;

            var id = typeof(T);

            if (byId.TryGetValue(id, out var existing))
            {
                switch (existing.State)
                {
                    case ModuleState.Ready:
                        SafeInvoke(callback, existing.Module as T, id);
                        return Disposable.Empty;

                    case ModuleState.Failed:
                    case ModuleState.Skipped:
                        LifecycleLog.Error(
                            $"WhenReady<{id.Name}>() will never fire: module is {existing.State}. " +
                            $"({existing.Error})");
                        return Disposable.Empty;
                }
            }

            Action<IModule> boxed = m => SafeInvoke(callback, m as T, id);

            if (!readyCallbacks.TryGetValue(id, out var list))
                readyCallbacks[id] = list = new List<Action<IModule>>();
            list.Add(boxed);

            return new Disposable(() =>
            {
                if (readyCallbacks.TryGetValue(id, out var l)) l.Remove(boxed);
            });
        }

        /// <summary>The awaitable version. Returns an already-completed Task when the module is Ready.</summary>
        public static Task<T> WhenReadyAsync<T>(CancellationToken cancellationToken = default)
            where T : class
        {
            if (TryGet<T>(out var ready)) return Task.FromResult(ready);

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sub = WhenReady<T>(m => tcs.TrySetResult(m));

            if (cancellationToken.CanBeCanceled)
            {
                var reg = cancellationToken.Register(() =>
                {
                    sub.Dispose();
                    tcs.TrySetCanceled(cancellationToken);
                });
                tcs.Task.ContinueWith(_ => reg.Dispose(), TaskScheduler.Default);
            }

            return tcs.Task;
        }

        /// <summary>
        /// Subscribes to state changes and <b>immediately replays</b> the current state of every known module.
        /// Meant for diagnostics and the Editor window; to simply obtain a module use <see cref="WhenReady{T}"/> instead.
        /// </summary>
        public static IDisposable ObserveStates(Action<ModuleEntry> observer)
        {
            if (observer == null) return Disposable.Empty;

            foreach (var t in entries)
            {
                try { observer(t); }
                catch (Exception ex) { LifecycleLog.Exception(ex); }
            }

            ModuleStateChanged += observer;
            return new Disposable(() => ModuleStateChanged -= observer);
        }

        private static void SafeInvoke<T>(Action<T> callback, T module, Type id) where T : class
        {
            if (module == null)
            {
                LifecycleLog.Error($"WhenReady<{id.Name}>(): registered instance is not a {id.Name}.");
                return;
            }

            try { callback(module); }
            catch (Exception ex) { LifecycleLog.Exception(ex); }
        }

        private static void FlushReadyCallbacks(ModuleEntry entry)
        {
            if (!readyCallbacks.TryGetValue(entry.Id, out var list) || list.Count == 0) return;

            // Take a copy: a callback may register new callbacks while running
            var snapshot = list.ToArray();
            readyCallbacks.Remove(entry.Id);

            foreach (var t in snapshot)
                t(entry.Module);
        }

        // ── Execution ───────────────────────────────────────────────────────

        internal static void RunPhase(ModulePhase phase)
        {
            _chain = RunPhaseChained(_chain, phase);
        }

        private static async Task RunPhaseChained(Task previous, ModulePhase phase)
        {
            // When previous is already completed, the await continues synchronously → fully synchronous projects behave exactly as they do today.
            try { await previous; }
            catch (Exception ex) { LifecycleLog.Exception(ex); }
            await RunPhaseInternal(phase);
        }

        private static async Task RunPhaseInternal(ModulePhase phase)
        {
            EnsureScanned();

            var batch = new List<ModuleEntry>();
            foreach (var t in entries)
                if (t.Phase == phase && t.State == ModuleState.Registered)
                    batch.Add(t);

            if (batch.Count == 0) return;

            var sorted = ModuleSorter.Sort(batch, byId, issues);
            LifecycleLog.DumpOrder(phase, sorted);

            var token = GetAppToken();

            for (var i = 0; i < sorted.Count; i++)
            {
                var e = sorted[i];
                e.SortIndex = i;

                var missing = FirstUnmetDependency(e);
                if (missing != null)
                {
                    e.State = ModuleState.Skipped;
                    e.Error = $"dependency '{missing.Name}' is not ready";
                    LifecycleLog.Error($"Skip {e.DisplayName}: {e.Error}.");
                    ModuleStateChanged?.Invoke(e);
                    continue;
                }

                var sw = Stopwatch.StartNew();
                try
                {
                    e.State = ModuleState.Initializing;
                    ModuleStateChanged?.Invoke(e);

                    e.Module.Initialize();

                    if (e.Module is IAsyncModule async)
                        await async.InitializeAsync(token);

                    e.State = ModuleState.Ready;
                    started.Add(e);
                    FlushReadyCallbacks(e);
                }
                catch (OperationCanceledException)
                {
                    e.State = ModuleState.Skipped;
                    e.Error = "cancelled";
                    return;
                }
                catch (Exception ex)
                {
                    e.State = ModuleState.Failed;
                    e.Error = ex.Message;
                    LifecycleLog.Exception(
                        new Exception($"[Lifecycle] '{e.DisplayName}' initialization failed.", ex));
                }
                finally
                {
                    sw.Stop();
                    e.InitMilliseconds = sw.Elapsed.TotalMilliseconds;
                    ModuleStateChanged?.Invoke(e);
                }
            }
        }

        private static Type FirstUnmetDependency(ModuleEntry e)
        {
            foreach (var d in e.DependsOn)
            {
                if (d == null) continue;
                if (!byId.TryGetValue(d, out var dep) || dep.State != ModuleState.Ready)
                    return d;
            }

            return null;
        }

        private static CancellationToken GetAppToken() => LifecycleToken.ApplicationAlive;

        private static void EnsureScanned()
        {
            if (_scanned) return;
            _scanned = true;
            ModuleAutoScanner.ScanAndRegister();
        }

        // ── Shutdown ────────────────────────────────────────────────────────

        /// <summary>Shuts down all modules in the reverse order of initialization. Re-entrant.</summary>
        internal static void ShutdownAll()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;

            for (var i = started.Count - 1; i >= 0; i--)
            {
                var e = started[i];
                try
                {
                    e.Module.Shutdown();
                    e.State = ModuleState.ShutDown;
                }
                catch (Exception ex)
                {
                    e.State = ModuleState.Failed;
                    e.Error = ex.Message;
                    LifecycleLog.Exception(ex);
                }
                ModuleStateChanged?.Invoke(e);
            }

            started.Clear();
            LifecycleHost.DestroyRoot();
            _shuttingDown = false;
        }

        /// <summary>
        /// The reset point when Domain Reload is disabled. Called by LifecycleDriver before entering Play Mode.
        /// </summary>
        internal static void ResetStatics()
        {
            ShutdownAll();
            entries.Clear();
            byId.Clear();
            started.Clear();
            issues.Clear();
            _chain = Task.CompletedTask;
            _scanned = false;
            readyCallbacks.Clear();
            _shuttingDown = false;
            ModuleStateChanged = null;
        }
    }
}