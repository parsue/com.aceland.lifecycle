# Player Loop

Where your per-frame work is injected into Unity's engine loop.

## Overview
`AceLand.Lifecycle` runs its frame work by inserting tiny **tick nodes** directly into Unity's player
loop. You never touch the raw `PlayerLoopSystem` API — you simply pick a `PlayerLoopPoint`, and the
package installs (and later removes) the node for you.

This page explains the eight points, how to choose one, how the nodes are installed and self-heal, and
how they behave during application quit. If you just want to schedule a callback, start with
[Frame Scheduling](frame-scheduling.md); come back here when you need to know *exactly when* in the
frame your work runs.

---

## The Eight Points
`PlayerLoopPoint` mirrors Unity's built-in loop segments. Each value nests a Lifecycle tick node under
the matching Unity segment, so your work runs in that slice of the frame.

| Point | Runs in Unity's… | Typical use |
| --- | --- | --- |
| `TimeUpdate` | TimeUpdate | Very earliest per-frame work, before input |
| `Initialization` | Initialization | Frame setup before the main update |
| `EarlyUpdate` | EarlyUpdate | Input polling, pre-physics preparation |
| `FixedUpdate` | FixedUpdate | Fixed-step work aligned with physics |
| `PreUpdate` | PreUpdate | Just before the main update |
| `Update` (default) | Update | General gameplay logic |
| `PreLateUpdate` | PreLateUpdate | After update, before late passes |
| `PostLateUpdate` | PostLateUpdate | After everything — cameras, rendering hand-off |

{% hint style="info" %}
When you do not pass a point, `Update` is used. It is the right choice for almost all gameplay logic —
reach for the others only when ordering against physics, input or rendering actually matters.
{% endhint %}

The points are listed above in **frame order**: `TimeUpdate` runs first, `PostLateUpdate` last.

---

## Choosing a Point
A few rules of thumb:

- **General logic** → `Update`. This is the default and covers most cases.
- **Read input or prepare before physics** → `EarlyUpdate` or `PreUpdate`.
- **Move things in step with physics** → `FixedUpdate`.
- **React after transforms/animation have settled** → `PreLateUpdate` (camera follow, IK-style
  adjustments).
- **Do work after rendering has been prepared** → `PostLateUpdate`.

```csharp
using AceLand.Lifecycle;

// Camera follow reads final transforms — run it late in the frame.
LifecycleFrame.RunEveryFrame(() => FollowTarget(), PlayerLoopPoint.PreLateUpdate);
```

---

## How Ticks Are Installed
The package keeps exactly **one tick node per point in use**. The first time a point is needed, its
node is inserted under the corresponding Unity segment; nodes are shared by every callback registered
at that point.

Two design guarantees make this safe:

- **Idempotent** — installing a node that already exists does nothing, and removing one that is not
  there does nothing. There is never a duplicate tick, no matter how the state was reached.
- **Self-healing** — if Unity rebuilds its player loop (something other systems can do at runtime),
  the package detects the missing node and re-installs it, so ticks keep firing.

{% hint style="info" %}
These nodes assume the **CoreCLR / no-Domain-Reload** workflow. They do not rely on a domain reload to
clean up: the Editor safety net removes every node when you leave Play mode or when assemblies reload,
so nothing leaks between sessions.
{% endhint %}

---

## Quit-Time Behaviour
During application shutdown the tick nodes are eventually removed — but *when* they are removed decides
whether your frame-based work can still run while the quit pipeline drains. That timing is controlled by
`PlayerLoopQuitLifespan`.

| Lifespan | Ticks removed… | Meaning |
| --- | --- | --- |
| `OnWantToQuit` | The instant a quit is requested | Fastest teardown, but the frame pump is gone immediately |
| `AfterBlockers` (default) | After quit blockers finish | Blockers may still use frames; handlers must not |
| `LastMoment` | After module shutdown completes | Safest — frames keep pumping through the whole pipeline |

{% hint style="warning" %}
If your quit **blockers** or wrap-up **handlers** wait on frame-based work (for example
`WaitForNextFrame` or a `RunEveryFrame` poll), removing the pump too early will stall them until a
timeout forces the quit forward. When in doubt, prefer a later lifespan such as `LastMoment` so the
frame loop stays alive until shutdown is truly done.
{% endhint %}

See [The Quit Pipeline](quit-pipeline.md) for how blockers and handlers fit into shutdown.

---

## Inspecting the Loop — Player Loop Graph
Open **`Tools ▸ AceLand ▸ Lifecycle ▸ Player Loop Graph`** to see the live state of every point:

- **Green** — the tick node is present and running inside Unity's live player loop.
- **Red** — the node had to be **self-healed** (Unity dropped it and the package re-installed it).

The window is read-only: it reflects what is actually installed rather than what you asked for, which
makes it the fastest way to confirm your ticks are wired in where you expect.

---

## Best Practices
- Stick with the default `Update` unless a specific ordering requirement pushes you elsewhere.
- Match the point to intent — physics-aligned work in `FixedUpdate`, post-transform work in
  `PreLateUpdate`.
- If quit-time frame work matters, set a later `PlayerLoopQuitLifespan` so the pump outlives your
  blockers and handlers.
- Use the Player Loop Graph to verify nodes are green; a red node is a hint that another system is
  fighting over the loop.

---

## See Also
- [Frame Scheduling](frame-scheduling.md) — the `LifecycleFrame` API that runs callbacks at these points.
- [Cancellation](cancellation.md) — frame-scoped waits that ride on the same tick.
- [The Quit Pipeline](quit-pipeline.md) — how shutdown drains and where the pump fits in.
