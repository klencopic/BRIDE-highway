using UnityEditor;
using UnityEngine;

public class CameraPlacementRigWindow : EditorWindow
{
    private const string DefaultGroundObjectName = "colmesh_ground";
    private const string DefaultWallsObjectName = "colmesh_walls";
    private static readonly Vector2 WindowSize = new Vector2(560f, 380f);
    private const float FieldLabelWidth = 220f;

    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform roadDirectionPointA;
    [SerializeField] private Transform roadDirectionPointB;
    [SerializeField] private GameObject groundObject;
    [SerializeField] private GameObject wallsObject;

    private GUIStyle titleStyle;
    private GUIStyle instructionStyle;
    private GUIStyle sectionStyle;

    [MenuItem("Tools/Camera Placement")]
    public static void Open()
    {
        CameraPlacementRigWindow window = CreateInstance<CameraPlacementRigWindow>();
        window.titleContent = new GUIContent("Camera Placement");
        window.minSize = WindowSize;
        window.ShowUtility();
    }

    private void OnEnable()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        AutoAssignSceneObjects();
    }

    private void OnGUI()
    {
        EnsureStyles();

        EditorGUILayout.LabelField("Camera Placement", titleStyle);
        EditorGUILayout.LabelField(
            "Select a camera and two highway-parallel reference points. The tool will use them to express camera placement relative to the inner highway edge.",
            instructionStyle);

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Inputs", sectionStyle);

        EditorGUIUtility.labelWidth = FieldLabelWidth;
        targetCamera = (Camera)EditorGUILayout.ObjectField("Camera", targetCamera, typeof(Camera), true);
        roadDirectionPointA = (Transform)EditorGUILayout.ObjectField("Highway Direction A", roadDirectionPointA, typeof(Transform), true);
        roadDirectionPointB = (Transform)EditorGUILayout.ObjectField("Highway Direction B", roadDirectionPointB, typeof(Transform), true);
        groundObject = (GameObject)EditorGUILayout.ObjectField("Ground Object", groundObject, typeof(GameObject), true);
        wallsObject = (GameObject)EditorGUILayout.ObjectField("Walls Object", wallsObject, typeof(GameObject), true);
        EditorGUIUtility.labelWidth = 0f;

        AutoAssignSceneObjects();

        EditorGUILayout.Space(8f);
        DrawInputStatus();
    }

    private void DrawInputStatus()
    {
        if (targetCamera == null)
        {
            EditorGUILayout.LabelField("Camera: missing", instructionStyle);
        }

        if (roadDirectionPointA == null || roadDirectionPointB == null)
        {
            EditorGUILayout.LabelField("Highway direction points: missing", instructionStyle);
        }

        if (groundObject == null)
        {
            EditorGUILayout.LabelField("Ground object: colmesh_ground not found", instructionStyle);
        }

        if (wallsObject == null)
        {
            EditorGUILayout.LabelField("Walls object: colmesh_walls not found", instructionStyle);
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
    }

    private void AutoAssignSceneObjects()
    {
        if (groundObject == null)
        {
            groundObject = GameObject.Find(DefaultGroundObjectName);
        }

        if (wallsObject == null)
        {
            wallsObject = GameObject.Find(DefaultWallsObjectName);
        }
    }
}
