using UnityEditor;
using UnityEngine;

namespace PoFootball.EditorTools
{
    /// <summary>
    /// Creates or refreshes material assets backed by project Shader Graphs so
    /// runtime views can load them via Resources.Load without runtime Shader.Find.
    /// </summary>
    internal static class Editor_PresentationAssetSetup
    {
        private const string SURROUND_SHADER_NAME = "Shader Graphs/PoFootball_FieldSurround";
        private const string SURROUND_MATERIAL_PATH = "Assets/Resources/M_PoFootballSurround.mat";

        [MenuItem("Tools/PoFootball/Presentation/Build Surround Material")]
        internal static void BuildAssets()
        {
            Shader surroundShader = Shader.Find(SURROUND_SHADER_NAME);

            if (surroundShader == null)
            {
                Debug.LogError(
                    $"[PoFootball] {nameof(Editor_PresentationAssetSetup)}: shader '{SURROUND_SHADER_NAME}' not found. "
                    + "Ensure Assets/Art/Shaders/PoFootball_FieldSurround.shadergraph imported cleanly.");
                return;
            }

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(SURROUND_MATERIAL_PATH);

            if (existing == null)
            {
                Material created = new Material(surroundShader)
                {
                    name = "M_PoFootballSurround",
                    color = Color.white
                };
                AssetDatabase.CreateAsset(created, SURROUND_MATERIAL_PATH);
                Debug.Log($"[PoFootball] Created {SURROUND_MATERIAL_PATH}.");
            }
            else if (existing.shader != surroundShader)
            {
                existing.shader = surroundShader;
                EditorUtility.SetDirty(existing);
                Debug.Log($"[PoFootball] Updated shader on {SURROUND_MATERIAL_PATH}.");
            }

            AssetDatabase.SaveAssets();
        }
    }
}
