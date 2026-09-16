// Unity 6.5 (6000.5+) Lifecycle Management.
// Enabled automatically by the asmdef's versionDefines; no manual define required.
// Add ACELAND_LIFECYCLE_FORCE_LEGACY when you need to force the legacy fallback.

#if ACELAND_UNITY_6_5_OR_NEWER && !ACELAND_LIFECYCLE_FORCE_LEGACY

using Unity.Scripting.LifecycleManagement;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AceLand.Lifecycle
{
    internal partial class LifecycleDriver
    {
        // Assets are NOT loaded yet. Install hooks only — never run a phase here.
        [OnCodeInitializing]
        private static void OnCodeInitializing() => ApplicationQuitPipeline.Install();

        [OnCodeUnloading]
        private static void OnCodeUnloading() => ShutdownEverything();

        [OnCodeDeinitializing]
        private static void OnCodeDeinitializing() => ShutdownEverything();

        [OnExitingPlayMode]
        private static void OnExitingPlayMode() => ShutdownEverything();

        private static void ShutdownEverything()
        {
            // Cancel first so any in-flight await unwinds before modules are torn down.
            LifecycleToken.SignalQuitting();
            LifecycleToken.SignalDead();

            // Force-cancel every scheduled frame handle (draining awaiters with
            // OperationCanceledException) and unsubscribe the pump before modules tear down.
            FrameScheduler.ResetStatics();
            ModuleRegistry.ShutdownAll();

            // CoreCLR premise: explicitly remove our PlayerLoop nodes; do not wait for a
            // domain reload that may never happen.
            LifecyclePlayerLoop.EnsureRemoved();
        }

        // Phase driving still relies on RuntimeInitializeOnLoadMethod: it guarantees every
        // assembly finishes one stage before the next begins, which is the barrier we need.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RunCore()
        {
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
            ModuleRegistry.SealInitialization();
            LifecycleHost.EnsureHost();
        }

#if UNITY_EDITOR
        // [InitializeOnEnterPlayMode] rather than [OnEnteringPlayMode]: proven behaviour,
        // and only one reset hook so a failure is diagnosable rather than masked.
        [InitializeOnEnterPlayMode]
        private static void OnEnterPlayMode() => LifecycleDriverShared.ResetAll("entering play mode");

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded() => LifecycleDriverShared.VerifyAfterReload();
#endif
    }
}

#endif