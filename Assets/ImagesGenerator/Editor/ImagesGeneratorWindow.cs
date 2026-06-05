using UnityEditor;
using UnityEngine;

public class ImagesGeneratorWindow : EditorWindow
{
    [SerializeField] private Camera captureCamera;
    [SerializeField] private GameObject truck;
    [SerializeField] private Vector3 startPosition;
    [SerializeField] private Vector3 endPosition;
    [SerializeField] private int imageCount = 10;

    private bool hasStartPosition;
    private bool hasEndPosition;

    [MenuItem("Tools/Images Generator")]
    public static void Open()
    {
        GetWindow<ImagesGeneratorWindow>("Images Generator");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Images Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Configure the camera, truck, and movement range for image generation.",
            MessageType.Info);

        captureCamera = (Camera)EditorGUILayout.ObjectField("Camera", captureCamera, typeof(Camera), true);
        truck = (GameObject)EditorGUILayout.ObjectField("Truck", truck, typeof(GameObject), true);

        EditorGUILayout.Space();
        DrawPositionRangeFields();

        EditorGUILayout.Space();
        imageCount = EditorGUILayout.IntField("Number Of Images", imageCount);
    }

    private void DrawPositionRangeFields()
    {
        EditorGUILayout.LabelField("Truck Position Range", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(truck == null))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            startPosition = EditorGUILayout.Vector3Field("Start Position", startPosition);
            if (EditorGUI.EndChangeCheck())
            {
                hasStartPosition = true;
            }
            if (GUILayout.Button("Use Truck Position", GUILayout.Width(125)))
            {
                startPosition = truck.transform.position;
                hasStartPosition = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            endPosition = EditorGUILayout.Vector3Field("End Position", endPosition);
            if (EditorGUI.EndChangeCheck())
            {
                hasEndPosition = true;
            }
            if (GUILayout.Button("Use Truck Position", GUILayout.Width(125)))
            {
                endPosition = truck.transform.position;
                hasEndPosition = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (truck == null)
        {
            EditorGUILayout.HelpBox("Select a truck before capturing start and end positions.", MessageType.Warning);
        }
        else if (!hasStartPosition || !hasEndPosition)
        {
            EditorGUILayout.HelpBox(
                "Move the truck to each endpoint and click Use Truck Position. " +
                "You can also type the coordinates manually.",
                MessageType.Warning);
        }
    }
}
