using UnityEditor;
using UnityEngine;

public class ImagesGeneratorWindow : EditorWindow
{
    [MenuItem("Tools/Images Generator")]
    public static void Open()
    {
        GetWindow<ImagesGeneratorWindow>("Images Generator");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Images Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Image generation settings and controls will be added here.",
            MessageType.Info);
    }
}
