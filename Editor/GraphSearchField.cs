using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    /// <summary>
    /// Shared toolbar search field for the Lifecycle graph windows (Initialization / Player Loop /
    /// Quit Pipeline Graph). Renders a Unity toolbar search box with a clear (<c>x</c>) button that
    /// appears only while there is input, and clears on <c>Escape</c> when the field is focused.
    /// <para>
    /// The helper is stateless: the caller owns the filter string and passes it by <c>ref</c>.
    /// <see cref="Draw"/> returns <c>true</c> when the value changed this frame, so the caller can
    /// relayout / repaint and re-run its own "show matches, hide others" filtering.
    /// </para>
    /// </summary>
    internal static class GraphSearchField
    {
        private const float CANCEL_W = 15f;

        private static GUIStyle _cancelStyle;
        private static GUIStyle _cancelEmptyStyle;

        // style names differ by Unity version — the old one is famously misspelled
        private static GUIStyle CancelStyle => _cancelStyle ??=
            FindStyle("ToolbarSearchCancelButton", "ToolbarSeachCancelButton");
        private static GUIStyle CancelEmptyStyle => _cancelEmptyStyle ??=
            FindStyle("ToolbarSearchCancelButtonEmpty", "ToolbarSeachCancelButtonEmpty");

        private static GUIStyle FindStyle(params string[] names)
        {
            foreach (var n in names)
            {
                var s = GUI.skin.FindStyle(n);
                if (s != null) return s;
            }
            return EditorStyles.toolbarButton;
        }

        /// <summary>
        /// Draws the search field. Returns <c>true</c> if <paramref name="filter"/> changed this frame.
        /// </summary>
        public static bool Draw(float width, string controlName, ref string filter)
        {
            var rect = GUILayoutUtility.GetRect(width, EditorGUIUtility.singleLineHeight,
                                                EditorStyles.toolbarSearchField, GUILayout.Width(width));
            var fieldRect  = new Rect(rect.x, rect.y, rect.width - CANCEL_W, rect.height);
            var cancelRect = new Rect(rect.xMax - CANCEL_W, rect.y, CANCEL_W, rect.height);

            // Escape clears while the field owns keyboard focus.
            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape &&
                GUI.GetNameOfFocusedControl() == controlName &&
                !string.IsNullOrEmpty(filter))
            {
                filter = string.Empty;
                GUIUtility.keyboardControl = 0;
                Event.current.Use();
                return true;
            }

            GUI.SetNextControlName(controlName);
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.TextField(fieldRect, filter, EditorStyles.toolbarSearchField);
            var changed = false;
            if (EditorGUI.EndChangeCheck() && next != filter)
            {
                filter = next;
                changed = true;
            }

            if (!string.IsNullOrEmpty(filter))
            {
                // Cancel button shown only when there is input.
                if (GUI.Button(cancelRect, GUIContent.none, CancelStyle))
                {
                    filter = string.Empty;
                    GUIUtility.keyboardControl = 0;
                    changed = true;
                }
                EditorGUIUtility.AddCursorRect(cancelRect, MouseCursor.Arrow);
            }
            else if (Event.current.type == EventType.Repaint)
            {
                GUI.Label(cancelRect, GUIContent.none, CancelEmptyStyle);
            }

            return changed;
        }

        /// <summary>Case-insensitive "contains" match; empty filter matches everything.</summary>
        public static bool Matches(string filter, params string[] fields)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (fields == null) return false;
            foreach (var f in fields)
            {
                if (!string.IsNullOrEmpty(f) &&
                    f.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
