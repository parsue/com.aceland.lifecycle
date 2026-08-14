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

            // Order matters: quit hooks first, modules next (they may use tokens during
            // Shutdown), tokens last so that shutdown code still has a live token.
            ApplicationQuitPipeline.ResetStatics();
            ModuleRegistry.ResetStatics();
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