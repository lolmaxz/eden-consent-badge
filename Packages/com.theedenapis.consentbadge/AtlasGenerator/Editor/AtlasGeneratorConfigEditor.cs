using UnityEditor;

namespace EdenApis.AtlasGenerator
{
    [CustomEditor(typeof(AtlasGeneratorConfig))]
    public class AtlasGeneratorConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginDisabledGroup(true);

            DrawDefaultInspector();

            EditorGUI.EndDisabledGroup();
        }
    }
}