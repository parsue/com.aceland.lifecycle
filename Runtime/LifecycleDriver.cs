// Legacy path: Unity < 6000.5, or forced fallback via ACELAND_LIFECYCLE_FORCE_LEGACY.
#if !ACELAND_UNITY_6_5_OR_NEWER || ACELAND_LIFECYCLE_FORCE_LEGACY

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AceLand.Lifecycle
{
    internal static class LifecycleDriver
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RunCore()
        {
            // No Prepare() here: the static constructor and ResetStatics() both cover it,
            // and calling it again abandons two live CancellationTokenSource.
            ApplicationQuitPipeline.Install();
            // Inject our PlayerLoop tick nodes as early as possible so the frame pump is live
            // for the whole play session. Idempotent + self-healing; removed on every teardown.
            LifecyclePlayerLoop.EnsureInstalled();
            ModuleRegistry.RunPhase(ModulePhase.Core);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RunRuntime() => ModuleRegistry.RunPhase(ModulePhase.Runtime);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RunScene()
        {
            ModuleRegistry.RunPhase(ModulePhase.Scene);
            ModuleRegistry.RunPhase(ModulePhase.Late);
            ModuleRegistry.SealInitialization();   // chained, so async modules are covered
            LifecycleHost.EnsureHost();
        }

#if UNITY_EDITOR
        /// <summary>
        /// The reset point when Domain Reload is disabled.
        /// Runs earlier than every RuntimeInitializeOnLoadMethod.
        /// </summary>
        [InitializeOnEnterPlayMode]
        private static void OnEnterPlayMode() => LifecycleDriverShared.ResetAll("entering play mode");

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded() => LifecycleDriverShared.VerifyAfterReload();
#endif
    }
}

#endif