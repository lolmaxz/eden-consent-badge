using UnityEditor;
using UnityEngine;

namespace EdenApis
{
    internal static class EdenBadgeCredits
    {
        internal static void Draw()
        {
            GUILayout.Space(16f);
            EditorGUILayout.HelpBox("Made possible by Krenki, Verde, Maxie, and Rekka.", MessageType.None);
        }
    }
}
