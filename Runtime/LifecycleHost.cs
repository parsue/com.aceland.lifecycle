using UnityEngine;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// A shared host for modules that need scene objects.
    /// Don't create your own GameObject — put everything here so the Hierarchy stays clean and everything is cleaned up at once on shutdown.
    /// </summary>
    public static class LifecycleHost
    {
        private static GameObject _root;
        private static LifecycleHostBehaviour _behaviour;
        private const string HOST_NAME = "[AceLand Lifecycle]";

        public static GameObject Root
        {
            get
            {
                EnsureHost();
                return _root;
            }
        }

        public static T AddComponent<T>() where T : Component => Root.AddComponent<T>();

        public static MonoBehaviour CoroutineRunner
        {
            get { EnsureHost(); return _behaviour; }
        }

        internal static void EnsureHost()
        {
            if (_root != null) return;
            if (!Application.isPlaying) return;

            // A mid-play domain reload nulls our statics while the GameObject survives.
            // Adopt it instead of creating a duplicate.
            var existing = GameObject.Find(HOST_NAME);
            if (existing != null)
            {
                _root = existing;
                _behaviour = existing.GetComponent<LifecycleHostBehaviour>()
                              ?? existing.AddComponent<LifecycleHostBehaviour>();
                return;
            }

            _root = new GameObject(HOST_NAME) { hideFlags = HideFlags.NotEditable };
            Object.DontDestroyOnLoad(_root);
            _behaviour = _root.AddComponent<LifecycleHostBehaviour>();
        }

        internal static void DestroyRoot()
        {
            if (_root == null) return;

            var go = _root;
            _root = null;
            _behaviour = null;

            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }
    }

    [AddComponentMenu("")]
    [DefaultExecutionOrder(-10000)]
    internal sealed class LifecycleHostBehaviour : MonoBehaviour
    {
        private void Awake() => ApplicationQuitPipeline.Install();

        /// <summary>
        /// A safety net. Normally ApplicationQuitPipeline has already run the whole flow;
        /// this only covers platforms where wantsToQuit is never raised (iOS / Android being killed outright by the OS).
        /// </summary>
        private void OnApplicationQuit()
        {
            if (ApplicationQuitPipeline.IsQuitting) return;
            LifecycleLog.Warning("Quit without pipeline (platform did not raise wantsToQuit).");
            LifecycleToken.SignalQuitting();
            ModuleRegistry.ShutdownAll();
            LifecycleToken.SignalDead();
        }
    }
}