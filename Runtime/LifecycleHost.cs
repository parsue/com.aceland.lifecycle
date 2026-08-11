using UnityEngine;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// A shared host for modules that need scene objects.
    /// Don't create your own GameObject — put everything here so the Hierarchy stays clean and everything is cleaned up at once on shutdown.
    /// </summary>
    public static class LifecycleHost
    {
        static GameObject s_Root;
        static LifecycleHostBehaviour s_Behaviour;

        public static GameObject SRoot
        {
            get
            {
                EnsureHost();
                return s_Root;
            }
        }

        public static T AddComponent<T>() where T : Component => SRoot.AddComponent<T>();

        public static MonoBehaviour CoroutineRunner
        {
            get { EnsureHost(); return s_Behaviour; }
        }

        internal static void EnsureHost()
        {
            if (s_Root != null) return;
            if (!Application.isPlaying) return;

            s_Root = new GameObject("[AceLand Lifecycle]")
            {
                hideFlags = HideFlags.NotEditable
            };
            Object.DontDestroyOnLoad(s_Root);
            s_Behaviour = s_Root.AddComponent<LifecycleHostBehaviour>();
        }

        internal static void DestroyRoot()
        {
            if (s_Root == null) return;

            var go = s_Root;
            s_Root = null;
            s_Behaviour = null;

            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }
    }

    [AddComponentMenu("")]
    [DefaultExecutionOrder(-10000)]
    internal sealed class LifecycleHostBehaviour : MonoBehaviour
    {
        void Awake() => ApplicationQuitPipeline.Install();

        /// <summary>
        /// A safety net. Normally ApplicationQuitPipeline has already run the whole flow;
        /// this only covers platforms where wantsToQuit is never raised (iOS / Android being killed outright by the OS).
        /// </summary>
        void OnApplicationQuit()
        {
            if (ApplicationQuitPipeline.IsQuitting) return;
            LifecycleLog.Warning("Quit without pipeline (platform did not raise wantsToQuit).");
            LifecycleToken.SignalQuitting();
            ModuleRegistry.ShutdownAll();
            LifecycleToken.SignalDead();
        }
    }
}