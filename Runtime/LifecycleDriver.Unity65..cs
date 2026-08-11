// Unity 6.5 (6000.5+) Lifecycle Management.
// Enabled automatically by the asmdef's versionDefines; no manual define required.
// Add ACELAND_LIFECYCLE_FORCE_LEGACY when you need to force the legacy fallback.

#if ACELAND_UNITY_6_5_OR_NEWER && !ACELAND_LIFECYCLE_FORCE_LEGACY

using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace AceLand.Lifecycle
{
    internal partial class LifecycleDriver
    {
        // Assets are not fully loaded yet → only reset state and install hooks here; do not touch assets.
        [OnCodeInitializing]
        private static void OnCodeInitializing()
        {
            LifecycleToken.Prepare();
            ApplicationQuitPipeline.Install();
        }

        [OnCodeUnloading]
        private static void OnCodeUnloading() => ShutdownEverything();

        [OnCodeDeinitializing]
        private static void OnCodeDeinitializing() => ShutdownEverything();

        [OnEnteringPlayMode]
        private static void OnEnteringPlayMode()
        {
            ApplicationQuitPipeline.ResetStatics();
            ModuleRegistry.ResetStatics();
            LifecycleToken.ResetStatics();
            ApplicationQuitPipeline.Install();
        }

        [OnExitingPlayMode]
        private static void OnExitingPlayMode() => ShutdownEverything();

        private static void ShutdownEverything()
        {
            ModuleRegistry.ShutdownAll();
            LifecycleToken.SignalQuitting();
            LifecycleToken.SignalDead();
        }

        // Phase driving still relies on RuntimeInitializeOnLoadMethod:
        // it guarantees that all assemblies finish one stage before the next begins, which is exactly the barrier we want.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RunCore() => ModuleRegistry.RunPhase(ModulePhase.Core);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RunRuntime() => ModuleRegistry.RunPhase(ModulePhase.Runtime);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RunScene()
        {
            ModuleRegistry.RunPhase(ModulePhase.Scene);
            ModuleRegistry.RunPhase(ModulePhase.Late);
            LifecycleHost.EnsureHost();
        }
    }
}

#endif