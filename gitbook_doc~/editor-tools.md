# Editor Tools and Profiling

Because the lfecycle is *declared* rather than hand-wired, you never have to guess how startup and
shutdown actually behaved. A set of Editor windows lets you **see** the Initialization Graph, the real
Initialization Timeline, the Quit Pipeline Graph and the Player Loop Graph — and export the timing data
for further analysis.

All windows live under **`Tools ▸ AceLand ▸ Lifecycle`**.

---

## Initialization Graph

A node-based view of every module and the `DependsOn` edges between them.

- In **Edit mode** it uses `TypeCache` to statically discover every `[LifecycleModule]`.
- In **Play mode** it overlays the live `ModuleRegistry` state on top of that graph.
- Supports zoom, pan and search; highlights the upstream/downstream of a selected node; cycles are drawn
  in red so illegal dependencies stand out immediately.
- Right-click a node to jump to its source, or cross-jump to the **Initialization Timeline** and the
  **Quit Pipeline Graph** for the same module.

Use it to answer "why does X run before Y?" and to spot accidental cycles or cross-phase mistakes before
you ever hit Play.

---

## Initialization Timeline

A Gantt-style chart of how long each module actually took to initialize.

- Bars are coloured by phase; the synchronous `Initialize()` portion is drawn solid and the awaited
  `InitializeAsync()` portion lighter, so you can tell CPU work from waiting.
- Timed-out or failed modules get a red outline.
- Live updates while the game boots, with zoom, a phase legend and a detail panel for the selected module.
- Right-click a row to cross-jump back into the **Initialization Graph**.

The data comes from `ModuleRegistry.GetTimeline()` and therefore requires profiling to be enabled
(see [Profiling](#profiling) below).

### Exporting the Timeline

The timeline toolbar has **Export JSON** and **Export CSV** buttons.

- **CSV** — one row per module plus a phase-summary header, ready to drop into a spreadsheet.
- **JSON** — a structured snapshot of the whole run.

Both are produced from the same `LifecycleTimeline` snapshot and are only written after the data verifies,
so you never get a half-baked file.

---

## Quit Pipeline Graph

Visualises the shutdown plan described in [The Quit Pipeline](quit-pipeline.md):

- The ordered list of `IQuitHandler` steps (respecting `[QuitOrder]`).
- Any active `IQuitBlocker` and its `BusyReason`, so a stuck shutdown is easy to diagnose.
- Open/ping the owning script straight from a blocker or step.

---

## Player Loop Graph

A read-only, node-based view of how Lifecycle is wired into Unity's player loop — see
[Player Loop](player-loop.md) for the underlying model.

- **Groups** are the eight `PlayerLoopPoint`s. A group header is **green** when its tick node is
  present in the live player loop, and **red** if it self-healed away — so a missing injection is
  obvious at a glance.
- **Nodes** are the frame processes you scheduled at each point (via `LifecycleFrame`). A node's title
  is the scheduled `Type.Method`, and its accent colour shows the state: **yellow** (waiting),
  **green** (running) or **red** (error).
- Completed processes disappear immediately; errored ones linger a few seconds so the failure stays
  visible. Double-click a node to open the declaring script.
- Supports pan, zoom, selection and search, and can optionally show empty points.

{% hint style="info" %}
This window deliberately has **no** install/remove controls. Injection is driven entirely by the
lifecycle and the quit pipeline, so the graph only ever reflects the real state — it never lets you
break that determinism by hand.
{% endhint %}

---

## Validate Dependencies

`Tools ▸ AceLand ▸ Lifecycle ▸ Validate Dependencies` runs the same checks as the graph without opening a
window, and can be toggled to **validate automatically after every compile**. It is pure `TypeCache`
reflection and never touches your assets. It reports:

- a synchronous module depending on an async module,
- a module depending on a later phase,
- and dependency cycles.

---

## Profiling

Timing collection is gated by a single switch so it costs nothing in shipping builds.

```csharp
using AceLand.Lifecycle;

// Toggle at runtime. Default policy: ON in the Editor and development builds, OFF in release.
LifecycleProfiler.Enabled = true;
```

When disabled, `ModuleRegistry` skips all timing instrumentation and timeline collection entirely — a true
zero-cost path. When enabled, each module records:

- `StartedAtMs` / `EndedAtMs` relative to the initialization origin, and
- separate `SyncMs` (time in `Initialize()`) and `AsyncMs` (time awaiting `InitializeAsync()`),

so awaiting and parallelism are clearly distinguishable in the timeline.

You can also read the snapshot yourself instead of using the window:

```csharp
var timeline = ModuleRegistry.GetTimeline();
foreach (var module in timeline.Modules)
    Debug.Log($"{module.DisplayName}: {module.TotalMs}ms (sync {module.SyncMs}, async {module.AsyncMs})");
```

{% hint style="warning" %}
If profiling is disabled, `GetTimeline()` returns an empty / disabled snapshot — enable
`LifecycleProfiler.Enabled` before the run you want to measure.
{% endhint %}

---

## Best Practices

- Keep the **Initialization Graph** open while designing startup order; fix cycles at edit time, not at runtime.
- Enable **auto-validate after compile** so illegal dependencies surface as soon as you write them.
- Turn on `LifecycleProfiler.Enabled` when investigating slow boots, then read the **Initialization Timeline**.
- Export **CSV/JSON** to compare boot cost across machines or over time.
- Leave profiling on its default policy for release builds so shipping players pay nothing.
