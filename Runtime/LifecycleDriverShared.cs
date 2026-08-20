using UnityEngine;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Shared driver logic, so both driver variants cannot drift apart.
    /// </summary>
    internal static class LifecycleDriverShared
    {
        internal static void ResetAll(string reason)
        {
            LifecycleLog.Info($"Reset ({reason}) — {ModuleRegistry.Entries.Count} stale entry(ies).");

            // CoreCLR premise: never rely on domain reload to clear injected PlayerLoop
            // delegates. Strip our nodes explicitly before anything else so a reset never
            // leaves a stale tick delegate pointing at collected state.
            LifecyclePlayerLoop.EnsureRemoved();

            // Order matters: quit hooks first, modules next (they may use tokens during
            // Shutdown), frame scheduler before tokens (its handles own linked sources and
            // must force-cancel their awaiters while a live token still exists), tokens last.
            ApplicationQuitPipeline.ResetStatics();
            ModuleRegistry.ResetStatics();
            FrameScheduler.ResetStatics();
            LifecycleToken.ResetStatics();
        }

        internal static void VerifyAfterReload()
        {
            if (!Application.isPlaying) return;
            if (ModuleRegistry.Entries.Count > 0) return;

            LifecycleLog.Error(
                "Scripts recompiled during Play Mode. All lifecycle statics were reset while scene " +
                "objects survived — no module is registered and Get<T>() will throw.\n" +
                "Stop and re-enter Play Mode. To avoid this, set Preferences ▸ General ▸ " +
                "Script Changes While Playing to 'Recompile After Finished Playing'.");

            LifecycleHost.EnsureHost();   // adopt the orphan rather than creating a duplicate later
        }
    }
}