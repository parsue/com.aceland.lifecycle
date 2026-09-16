using System.Collections;
using UnityEngine;

namespace AceLand.Lifecycle
{
    /// <summary>
    /// Owns the final Application.Quit(). Deliberately separate from LifecycleHost,
    /// which ShutdownAll() destroys before the final quit is issued.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-20000)]
    internal sealed class QuitDriver : MonoBehaviour
    {
        private const string HOST_NAME = "[AceLand Quit Driver]";
        private const int FRAME_GRACE = 2;
        private const float HARD_DEADLINE = 5f;

        private static QuitDriver _instance;

        internal static QuitDriver Ensure()
        {
            if (_instance != null) return _instance;
            if (!Application.isPlaying) return null;

            var go = GameObject.Find(HOST_NAME) ?? new GameObject(HOST_NAME);
            go.hideFlags = HideFlags.NotEditable | HideFlags.DontSave;
            DontDestroyOnLoad(go);

            _instance = go.GetComponent<QuitDriver>() ?? go.AddComponent<QuitDriver>();
            return _instance;
        }

        internal static void Clear()
        {
            if (_instance == null) return;

            var go = _instance.gameObject;
            _instance = null;

            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        internal void QuitDeferred() => StartCoroutine(QuitRoutine());

        private static IEnumerator QuitRoutine()
        {
            // Unity discards Application.Quit() issued in the same frame that a quit
            // request was refused. Let the frame complete first.
            for (var i = 0; i < FRAME_GRACE; i++) yield return null;

            LifecycleLog.Info("Issuing final Application.Quit().");
            Application.Quit();

            var deadline = Time.realtimeSinceStartup + HARD_DEADLINE;
            while (Time.realtimeSinceStartup < deadline) yield return null;

            LifecycleLog.Error($"Process still alive {HARD_DEADLINE:0}s after Application.Quit(). " +
                               "Forcing exit.");
            ForceExit();
        }

        private static void ForceExit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_STANDALONE || UNITY_SERVER
            System.Diagnostics.Process.GetCurrentProcess().Kill();
#else
            Application.Quit();
#endif
        }
    }
}