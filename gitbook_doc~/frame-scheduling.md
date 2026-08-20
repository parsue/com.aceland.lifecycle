# Frame Scheduling

Sometimes you need to run a small piece of work *next frame*, *after a delay*, or *once a condition
becomes true* — without spinning up a `MonoBehaviour` or a coroutine. `LifecycleFrame` schedules plain
`Action` callbacks on the Unity main thread at a chosen point in the player loop, and hands you a handle
you can cancel or await.

## Why Not a Coroutine

- No GameObject, no `StartCoroutine`, no component to keep alive.
- Works from anywhere — a plain C# class, a module, a service.
- Every handle is owned by the internal scheduler and **force-cancelled on quit, play-mode exit or
  assembly reload**, so nothing is ever left dangling (no reliance on domain reload).
- You choose exactly which player-loop point runs the callback.

---

## The API

Every method returns an [`IFrameHandle`](#the-iframehandle) and accepts an optional `CancellationToken`.
The default player-loop point is `PlayerLoopPoint.Update`.

```csharp
using AceLand.Lifecycle;
using UnityEngine;

// Run once next frame.
LifecycleFrame.RunNextFrame(() => Debug.Log("next frame"));

// Run once after N frames.
LifecycleFrame.RunAfterFrames(() => Debug.Log("3 frames later"), frames: 3);

// Run once on the first frame the predicate is true (polled once per frame on the main thread).
LifecycleFrame.RunWhen(() => player != null, () => Debug.Log("player exists"));

// Run once after a delay. Scaled time by default; pass unscaled: true to ignore timeScale.
LifecycleFrame.RunDelayed(() => Debug.Log("half a second later"), seconds: 0.5f);

// Run every frame, optionally capped. Dispose the handle to stop it early.
LifecycleFrame.RunEveryFrame(() => Tick(), maxFrames: 60);
```

### Choosing a Player-Loop Point

Each method has an overload that takes a `PlayerLoopPoint`, so the callback flushes exactly where you
need it in the frame:

```csharp
LifecycleFrame.RunNextFrame(() => Recalculate(), CancellationToken.None); // default Update
LifecycleFrame.RunEveryFrame(() => LateTick(), PlayerLoopPoint.PreLateUpdate);
```

Available points, in frame order: `TimeUpdate`, `Initialization`, `EarlyUpdate`, `FixedUpdate`,
`PreUpdate`, `Update` (default), `PreLateUpdate`, `PostLateUpdate`.

{% hint style="info" %}
Not sure which point to pick, or want to see how these nodes are installed into Unity's player
loop? The [Player Loop](player-loop.md) page walks through every point and how to inspect them live.
{% endhint %}

---

## The `IFrameHandle`

Every scheduled call returns an `IFrameHandle`. It has two usage styles.

{% tabs %}
{% tab title="Cancel with using" %}
```csharp
// Dispose() cancels the pending work. Great for scoping to a lifetime.
using var handle = LifecycleFrame.RunEveryFrame(() => Poll());
// ... when the scope ends, polling stops automatically.
```
{% endtab %}

{% tab title="Await completion" %}
```csharp
// Await resumes on the main thread once the work has run.
await LifecycleFrame.RunDelayed(() => Prepare(), seconds: 1f);
Debug.Log("delay finished, back on the main thread");

// If the work is cancelled or the app shuts down, the await throws OperationCanceledException.
```
{% endtab %}

{% tab title="Manual cancel" %}
```csharp
var handle = LifecycleFrame.RunEveryFrame(() => Stream());
if (done)
    handle.Dispose(); // stop it
Debug.Log(handle.IsCompleted); // finished successfully?
Debug.Log(handle.IsCancelled); // cancelled or shut down?
```
{% endtab %}
{% endtabs %}

| Member | Meaning |
| --- | --- |
| `IsCompleted` | `true` once the scheduled work has finished successfully |
| `IsCancelled` | `true` if it was disposed, cancelled by token, or the app shut down |
| `Dispose()` | Cancels the pending work |
| `await handle` | Resumes on the main thread when done; throws `OperationCanceledException` if cancelled |

Because continuations run inside the player-loop pump, awaited code **always resumes on the Unity
thread** — safe to touch Unity APIs immediately after the `await`.

---

## Tokens and Shutdown

Frame work is automatically linked to the application lifespan, so it cannot outlive a quit. If you also
want it to stop when your own token cancels, just pass it in:

```csharp
using AceLand.Lifecycle;

// Stops when myToken cancels OR the application shuts down.
LifecycleFrame.RunEveryFrame(() => Tick(), maxFrames: null, cancellationToken: myToken);
```

See [The Quit Pipeline](quit-pipeline.md) for `LifecycleToken.CreateLinked` / `CreateQuitLinked` if you
need to compose tokens yourself.

---

## Best Practices

- Reach for `LifecycleFrame` instead of a throwaway `MonoBehaviour` for one-shot or short-lived frame work.
- Prefer `RunWhen` over polling in your own `Update` when you are waiting for a single condition.
- Keep `RunEveryFrame` callbacks cheap, and always keep the handle so you can dispose it.
- `await` a handle when you need to continue on the main thread after a frame delay.
- Pass a `CancellationToken` for work that should also stop on your own signals — shutdown is handled for you.
