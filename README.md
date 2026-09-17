# AceLand Lifecycle

[![Sponsor](https://img.shields.io/badge/Sponsor-%E2%9D%A4-db61a2?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/parsue)
[![Discord](https://img.shields.io/badge/Discord-Join-5865F2?logo=discord&logoColor=white)](https://discord.gg/XsCYGnYzuc)
[![Docs](https://img.shields.io/badge/Docs-GitBook-3884FF?logo=gitbook&logoColor=white)](https://docs.parsue.io/aceland-unity-packages)

**Deterministic, dependency-ordered** initialization and shutdown for modern Unity.

> ❤️ **Enjoying this package?** Consider [sponsoring on GitHub](https://github.com/sponsors/parsue). Sponsors get a community role on our [Discord](https://discord.gg/XsCYGnYzuc). (Sponsorship is a voluntary thank-you and is separate from the paid Editor tools / license.)

> 📖 **New here?** Read [**Why a New DI & Lifecycle Stack for Modern Unity**](https://docs.parsue.io/aceland-unity-packages/core-packages/why-a-new-di-and-lifecycle-stack-for-modern-unity) — the reasoning behind this package, how it replaces fragile `Awake`/`Start` + execution-order bootstrapping, and when *not* to use it.

## Why AceLand Lifecycle

Most projects grow their startup as a fragile pile of `Awake`/`Start` ordering hacks and
`[DefaultExecutionOrder]` magic numbers — until the day a system reads a service that
isn't ready yet. AceLand Lifecycle is a deliberate reset for the
**no-domain-reload / CoreCLR era**: you *declare* what depends on what, and a
topological sorter derives the real order for you.

- **You declare, the sorter decides.** Attributes only *register* a module's phase and
  dependencies. A topological sorter computes the true execution order — reordering
  systems never means editing a magic number again.
- **Sync and async, safely.** `IModule` for fast synchronous setup, `IAsyncModule` for
  loading/connecting. A synchronous module can't depend on an async one (the validator
  reports it), so you never read a not-yet-ready service by accident.
- **Never deadlocks.** Opt-in parallel init for same-level async modules, with per-module
  and per-phase timeouts. On timeout the phase is forced forward and the issue is recorded
  — the world always comes up.
- **A real quit pipeline.** Deterministic, reverse-order shutdown with a safe-quit filter,
  so you flush saves and close connections *before* the app actually exits.
- **Player-loop scheduling.** Run work at precise player-loop points, every frame, after
  N frames, after a delay, or when a condition becomes true — without scattering
  `MonoBehaviour` timers everywhere.
- **Survives no-domain-reload.** Designed from day one for Unity's fast enter-play and
  CoreCLR direction — no reload-dependent bootstrapping.

## How It Compares

Most teams roll their own startup on top of `RuntimeInitializeOnLoadMethod`, execution
order, and ad-hoc bootstrappers. AceLand Lifecycle replaces that hand-built layer with a
declared, verifiable one.

| Concern | Hand-rolled `Awake`/`Start` + execution order | Custom bootstrapper | **AceLand Lifecycle** |
|---|---|---|---|
| Ordering source of truth | Scattered magic numbers | Manual sequence | **Declared deps → topological sort** |
| Async initialization | Coroutines / ad-hoc | Manual `await` chains | **`IAsyncModule` with timeouts** |
| Parallel warm-up | Rare / manual | Manual | **Opt-in, per-phase/module timeouts, never deadlocks** |
| Clean shutdown | `OnApplicationQuit` scramble | Manual | **Reverse-order quit pipeline + safe-quit filter** |
| Verification | None | None | **Init graph + timeline profiler + quit-pipeline graph** |
| No-domain-reload design | Fragile | Depends | **Designed for it from day one** |

**When _not_ to reach for it:** a small project whose startup genuinely fits in a couple
of `Awake` calls; or a workflow still hard-locked to domain reload with no plans to move.

## Feature Highlights

- **Dependency-ordered initialization** — declare with `[LifecycleModule]`; a topological sorter derives the order.
- **Sync & async modules** — `IModule` / `IAsyncModule`, with the validator preventing sync→async dependencies.
- **Opt-in parallel init** — `AllowParallel` for same-level async modules, with per-module and per-phase timeouts.
- **Quit pipeline** — deterministic reverse-order shutdown with a safe-quit filter.
- **Player-loop scheduling** — precise player-loop points, per-frame, after-N-frames, delay, and run-when-condition.
- **Editor tooling** — initialization graph, initialization timeline profiler, player-loop graph, quit-pipeline graph, dependency validation.

## Pairs With AceLand Injection

Lifecycle answers *"in what order does the world come alive, and how does it shut down
cleanly?"* — its companion package
[**AceLand Injection**](https://docs.parsue.io/aceland-unity-packages/core-packages/injection-exp)
answers *"how do objects find each other?"* with a zero-reflection DI container.
Together they form a modern, no-domain-reload architecture baseline. See the
[architecture article](https://docs.parsue.io/aceland-unity-packages/core-packages/why-a-new-di-and-lifecycle-stack-for-modern-unity)
for how they work as one story.

## Quick Start

```csharp
using System.Threading;
using System.Threading.Tasks;
using AceLand.Lifecycle;

[LifecycleModule(ModulePhase.Runtime, DependsOn = new[] { typeof(RemoteConfigModule) })]
public sealed class PlayerSystemModule : AsyncModuleBase
{
    // Lightweight: register services and set up fields.
    public override void Initialize() { /* ... */ }

    // Heavy: load assets, open connections, warm caches.
    public override Task InitializeAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    // Reverse cleanup — must be re-entrant.
    public override void Shutdown() { /* ... */ }
}
```

You never hand-sort execution order. Because dependencies are expressed as `typeof(...)`,
the compiler enforces the reference, the asmdef must reference the assembly, and
`package.json` must declare the dependency — all three stay consistent automatically, and
the same data feeds the editor dependency graph.

## Open Core

The **runtime is free and open source, forever** — bootstrap, dependency-ordered
initialization, the quit pipeline, and player-loop scheduling are never gated, and
**builds are never gated**. Only the development-time Editor tools (Initialization Graph,
Player Loop Graph, Initialization Timeline, Quit Pipeline Graph, dependency validation)
require a paid AceLand license, and licensing is an **optional** dependency: unlicensed
editors keep the runtime fully functional and simply show a one-click install prompt on
the gated windows.

## Documents

We use GitBook as the public documentation for our packages.

> Visit our [GitBook](https://docs.parsue.io/aceland-unity-packages)

Please visit our GitBook for details.
