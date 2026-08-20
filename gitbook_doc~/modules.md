# Defining and Running Modules

Everything you initialize at startup is a **module**. You declare *which phase* a module belongs to and
*what it depends on*; the registry does the rest — sorting, running, awaiting and exposing it to the
rest of the game.

## Opt the Assembly In

Only assemblies marked with `[assembly: LifecycleAssembly]` are scanned. This keeps startup fast
(no reflection over the whole project) and makes module discovery explicit.

```csharp
using AceLand.Lifecycle;

// Put this once per assembly, e.g. in an AssemblyInfo.cs file.
[assembly: LifecycleAssembly]
```

If you forget it, your modules are simply never registered — the Editor validator will warn you.

---

## The Four Phases

Phases run in a strict order. A phase only begins after the previous one has completely finished
(including any awaited async work).

| Phase | Unity timing | Use it for |
| --- | --- | --- |
| `Core` | AfterAssembliesLoaded | Pure C# services; no UnityEngine objects yet |
| `Runtime` | BeforeSceneLoad | UnityEngine APIs are safe, but no scene objects exist |
| `Scene` | AfterSceneLoad | Needs the loaded scene or creates GameObjects |
| `Late` | after AfterSceneLoad | Final wrap-up: analytics, warm-up, intro flow |

{% hint style="success" %}
**Rule of thumb:** pick the **earliest** phase that still satisfies your needs. Do not push everything
into `Late` — that defeats the point of ordered startup.
{% endhint %}

---

## A Synchronous Module

Derive from `ModuleBase` and override `Initialize()` (and optionally `Shutdown()`). The attribute only
declares intent; it never decides the run order by itself.

```csharp
using AceLand.Lifecycle;
using UnityEngine;

[LifecycleModule(ModulePhase.Core, Order = -5000)]
public sealed class GameSettings : ModuleBase
{
    public string GameName { get; private set; }

    public override void Initialize()
    {
        GameName = "My Game";
        Debug.Log("GameSettings ready");
    }

    public override void Shutdown()
    {
        // Keep this re-entrant — it may be called more than once.
        GameName = null;
    }
}
```

`Order` is only a **tie-break** between modules on the same dependency level (lower runs first). Use it
sparingly; prefer real `DependsOn` links whenever one module genuinely needs another.

---

## An Asynchronous Module

Derive from `AsyncModuleBase` and override `InitializeAsync(CancellationToken)`. The registry awaits it
before the phase is considered complete. Always honour the token so shutdown can cancel cleanly.

```csharp
using System.Threading;
using System.Threading.Tasks;
using AceLand.Lifecycle;

// Depends on GameSettings, so it always runs after GameSettings is ready.
[LifecycleModule(ModulePhase.Core, DependsOn = new[] { typeof(GameSettings) })]
public sealed class RemoteConfigModule : AsyncModuleBase
{
    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // WhenReadyAsync gives you the fully-initialized dependency instance.
        var settings = await ModuleRegistry.WhenReadyAsync<GameSettings>(cancellationToken);

        // ... fetch remote config for settings.GameName ...
        await Task.Delay(500, cancellationToken);
    }
}
```

{% hint style="warning" %}
**Important constraint:** a *synchronous* module must not depend on an *async* module. The Validator
flags it as an issue — make the dependent module async too.
{% endhint %}

---

## Declaring Dependencies

`DependsOn` takes real `Type` values via `typeof(...)`. Because you point at the actual type:

- the code reference,
- the asmdef reference,
- and the package dependency

all stay consistent automatically. The topological sorter then guarantees the depended-on module is
fully ready before yours runs.

```csharp
[LifecycleModule(ModulePhase.Runtime, DependsOn = new[] { typeof(GameSettings), typeof(RemoteConfigModule) })]
public sealed class PlayerProfileModule : AsyncModuleBase { /* ... */ }
```

Cross-phase direction is enforced: a module may not depend on a module in a **later** phase
(for example `Core` depending on `Scene`). Cycles are detected too and reported as issues rather than
crashing the sort.

---

## Parallel Warm-Up (opt-in)

By default, modules on the same level run one after another. If two modules are independent (neither
depends on the other) and both set `AllowParallel = true`, the registry runs their `InitializeAsync`
together via `Task.WhenAll`.

```csharp
[LifecycleModule(ModulePhase.Runtime, AllowParallel = true)]
internal sealed class AudioWarmupModule : AsyncModuleBase
{
    public override async Task InitializeAsync(CancellationToken cancellationToken)
        => await Task.Delay(800, cancellationToken);
}

[LifecycleModule(ModulePhase.Runtime, AllowParallel = true)]
internal sealed class AssetWarmupModule : AsyncModuleBase
{
    public override async Task InitializeAsync(CancellationToken cancellationToken)
        => await Task.Delay(800, cancellationToken);
}
```

The two above share a level, so the phase spends about 800ms instead of 1600ms. Parallelism is strictly
opt-in — remove the flag and they run in order.

---

## Per-Module Timeout (never deadlock)

Set `TimeoutMs` to cap an async module's initialization. If it exceeds the budget, the registry marks it
**Failed** and records the reason on the module's entry — it never blocks the rest of the phase.

```csharp
[LifecycleModule(ModulePhase.Runtime, AllowParallel = true, TimeoutMs = 500)]
internal sealed class NetworkProbeModule : AsyncModuleBase
{
    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // If this exceeds 500ms it is marked Failed, and startup continues.
        await Task.Delay(5000, cancellationToken);
    }
}
```

`0` (the default) means no per-module timeout.

---

## Tuning a Whole Phase

Instead of repeating flags on every module, you can tune a phase for the entire assembly with
`[assembly: LifecyclePhaseOptions(...)]`. Multiple declarations are allowed; when they conflict, the
**strictest** setting wins.

```csharp
using AceLand.Lifecycle;

// Turn on parallelism and a shared timeout for the whole Runtime phase in this assembly.
[assembly: LifecyclePhaseOptions(ModulePhase.Runtime, Parallel = true, TimeoutMs = 1000)]
```

---

## Querying and Consuming Modules

Ask `ModuleRegistry` for a module from anywhere in your code.

{% tabs %}
{% tab title="Callback" %}
```csharp
// Runs the callback the moment the module is ready — immediately if it already is.
ModuleRegistry.WhenReady<GameSettings>(settings =>
{
    Debug.Log($"Using {settings.GameName}");
});
```
{% endtab %}

{% tab title="Await" %}
```csharp
// Await the module. Resolves as soon as it becomes ready.
var settings = await ModuleRegistry.WhenReadyAsync<GameSettings>();
Debug.Log(settings.GameName);
```
{% endtab %}

{% tab title="Immediate" %}
```csharp
// Non-blocking checks for code that must not wait.
if (ModuleRegistry.IsReady<GameSettings>() &&
    ModuleRegistry.TryGet<GameSettings>(out var settings))
{
    Debug.Log(settings.GameName);
}
```
{% endtab %}
{% endtabs %}

For readiness of the whole boot sequence rather than a single module:

```csharp
// Await the entire initialization.
await ModuleRegistry.Ready;

// Or a callback.
ModuleRegistry.WhenInitialized(() => Debug.Log("All phases complete"));
```

{% hint style="warning" %}
Events such as `ModuleStateChanged`, `PhaseCompleted` and `InitializationCompleted` are **live** and
do not replay. Late subscribers should use `WhenReady<T>` / `WhenInitialized` / `ObserveStates`
instead of hooking the raw events.
{% endhint %}

---

## Best Practices

- Keep `Initialize()` cheap — register services and set fields; push heavy or IO work into `InitializeAsync`.
- Make `Shutdown()` re-entrant and non-throwing on repeated calls.
- Model real needs with `DependsOn`; reserve `Order` for fine tie-breaking only.
- Opt into `AllowParallel` only for genuinely independent modules.
- Give slow, failure-prone async work a `TimeoutMs` so startup can never hang.
- Consume other modules with `WhenReady<T>` / `WhenReadyAsync<T>` rather than caching instances early.
