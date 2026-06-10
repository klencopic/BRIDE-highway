using UnityEditor;
using UnityEngine;

public class AxleMarkerCreatorWindow : EditorWindow
{
    private static readonly Vector2 WindowSize = new Vector2(520f, 280f);
    private const float FieldLabelWidth = 160f;

    [SerializeField] private Transform truckRoot;

    private GUIStyle titleStyle;
    private GUIStyle instructionStyle;
    private GUIStyle sectionStyle;
    private GUIStyle buttonStyle;

    [MenuItem("Tools/Axle Marker Creator")]
    public static void Open()
    {
        AxleMarkerCreatorWindow window = CreateInstance<AxleMarkerCreatorWindow>();
        window.titleContent = new GUIContent("Axle Marker");
        window.minSize = WindowSize;
        window.ShowUtility();
    }

    private void OnEnable()
    {
        if (truckRoot == null && Selection.activeTransform != null)
        {
            truckRoot = Selection.activeTransform;
        }
    }

    private void OnGUI()
    {
        EnsureStyles();

        EditorGUILayout.LabelField("Axle Marker Creator", titleStyle);
        EditorGUILayout.LabelField(
            "Select one truck root. The tool will create ground markers for detected axles under the truck's ReferencePoints child.",
            instructionStyle);

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Input", sectionStyle);
        EditorGUIUtility.labelWidth = FieldLabelWidth;
        truckRoot = (Transform)EditorGUILayout.ObjectField("Truck Root", truckRoot, typeof(Transform), true);
        EditorGUIUtility.labelWidth = 0f;

        EditorGUILayout.Space(12f);
        using (new EditorGUI.DisabledScope(true))
        {
            GUILayout.Button("Generate Axle Markers", buttonStyle, GUILayout.Height(42));
        }

        if (truckRoot == null)
        {
            EditorGUILayout.LabelField("Set Truck Root before generating markers.", instructionStyle);
        }
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
        {
            return;
        }

        titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 20,
            wordWrap = true
        };

        instructionStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 15,
            wordWrap = true
        };

        sectionStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold
        };
    }
}
