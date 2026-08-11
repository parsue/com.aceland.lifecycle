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
        static void RunCore()
        {
            LifecycleToken.Prepare();
            ApplicationQuitPipeline.Install();
            ModuleRegistry.RunPhase(ModulePhase.Core);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RunRuntime() => ModuleRegistry.RunPhase(ModulePhase.Runtime);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RunScene()
        {
            ModuleRegistry.RunPhase(ModulePhase.Scene);
            ModuleRegistry.RunPhase(ModulePhase.Late);
            LifecycleHost.EnsureHost();
        }

#if UNITY_EDITOR
        /// <summary>The reset point when Domain Reload is disabled; runs earlier than every RuntimeInitializeOnLoadMethod.</summary>
        [InitializeOnEnterPlayMode]
        static void OnEnterPlayMode()
        {
            ApplicationQuitPipeline.ResetStatics();
            ModuleRegistry.ResetStatics();
            LifecycleToken.ResetStatics();
        }
#endif
    }
}

#endif