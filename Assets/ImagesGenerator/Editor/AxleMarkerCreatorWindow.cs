using UnityEditor;
using UnityEngine;

public class AxleMarkerCreatorWindow : EditorWindow
{
    private const string ReferencePointsContainerName = "ReferencePoints";
    private const string DefaultRoadObjectName = "colmesh_ground";
    private static readonly Vector2 WindowSize = new Vector2(520f, 280f);
    private const float FieldLabelWidth = 160f;

    [SerializeField] private Transform truckRoot;
    [SerializeField] private GameObject roadObject;

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

        AutoAssignRoadObject();
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

        AutoAssignRoadObject();
        DrawRoadStatus();
        DrawDetectedAxles();

        EditorGUILayout.Space(12f);
        using (new EditorGUI.DisabledScope(truckRoot == null))
        {
            if (GUILayout.Button("Generate Axle Markers", buttonStyle, GUILayout.Height(42)))
            {
                CreateReferencePointsContainer();
            }
        }

        if (truckRoot == null)
        {
            EditorGUILayout.LabelField("Set Truck Root before generating markers.", instructionStyle);
        }
    }

    private void CreateReferencePointsContainer()
    {
        Transform referencePoints = GetOrCreateReferencePoints();
        Selection.activeObject = referencePoints.gameObject;
        EditorGUIUtility.PingObject(referencePoints);
    }

    private Transform GetOrCreateReferencePoints()
    {
        Transform existing = truckRoot.Find(ReferencePointsContainerName);
        if (existing != null)
        {
            return existing;
        }

        GameObject container = new GameObject(ReferencePointsContainerName);
        Undo.RegisterCreatedObjectUndo(container, "Create Reference Points Container");
        container.transform.SetParent(truckRoot, false);
        container.transform.localPosition = Vector3.zero;
        container.transform.localRotation = Quaternion.identity;
        container.transform.localScale = Vector3.one;
        return container.transform;
    }

    private void DrawRoadStatus()
    {
        string roadStatus = roadObject == null
            ? "Road: colmesh_ground not found. The tool will use bounds as fallback."
            : "Road: " + roadObject.name + " collider will be used.";

        EditorGUILayout.LabelField(roadStatus, instructionStyle);
    }

    private void DrawDetectedAxles()
    {
        if (truckRoot == null)
        {
            return;
        }

        Transform[] steeringWheels = FindSteeringWheelPair();
        Transform[] axles = FindAxleTransforms();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Detected Axles", sectionStyle);
        EditorGUILayout.LabelField(
            steeringWheels.Length == 2
                ? "Front steering pair: " + steeringWheels[0].name + " / " + steeringWheels[1].name
                : "Front steering pair: not found",
            instructionStyle);

        if (axles.Length == 0)
        {
            EditorGUILayout.LabelField("Rear axles: none found", instructionStyle);
            return;
        }

        foreach (Transform axle in axles)
        {
            EditorGUILayout.LabelField("Rear axle: " + axle.name, instructionStyle);
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

    private void AutoAssignRoadObject()
    {
        if (roadObject != null)
        {
            return;
        }

        GameObject exactMatch = GameObject.Find(DefaultRoadObjectName);
        if (exactMatch != null && exactMatch.GetComponentInChildren<Collider>() != null)
        {
            roadObject = exactMatch;
            return;
        }

        foreach (GameObject sceneObject in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (sceneObject.name == DefaultRoadObjectName
                && sceneObject.scene.IsValid()
                && sceneObject.GetComponentInChildren<Collider>() != null)
            {
                roadObject = sceneObject;
                return;
            }
        }
    }

    private Transform[] FindAxleTransforms()
    {
        Transform[] allTransforms = truckRoot.GetComponentsInChildren<Transform>(true);
        System.Collections.Generic.List<Transform> axles = new System.Collections.Generic.List<Transform>();
        Transform existingReferencePoints = truckRoot.Find(ReferencePointsContainerName);

        foreach (Transform child in allTransforms)
        {
            if (child == truckRoot || (existingReferencePoints != null && child.IsChildOf(existingReferencePoints)))
            {
                continue;
            }

            string lowerName = child.name.ToLowerInvariant();
            if (lowerName.Contains("axle") && !lowerName.Contains("steering"))
            {
                axles.Add(child);
            }
        }

        axles.Sort((first, second) =>
            truckRoot.InverseTransformPoint(first.position).z.CompareTo(truckRoot.InverseTransformPoint(second.position).z));

        return axles.ToArray();
    }

    private Transform[] FindSteeringWheelPair()
    {
        Transform[] allTransforms = truckRoot.GetComponentsInChildren<Transform>(true);
        Transform leftSteer = null;
        Transform rightSteer = null;

        foreach (Transform child in allTransforms)
        {
            string normalizedName = child.name.ToLowerInvariant().Replace("_", string.Empty).Replace(" ", string.Empty);
            if (!normalizedName.Contains("steer"))
            {
                continue;
            }

            if (normalizedName.EndsWith("l") || normalizedName.Contains("steerl"))
            {
                leftSteer = child;
            }
            else if (normalizedName.EndsWith("r") || normalizedName.Contains("steerr"))
            {
                rightSteer = child;
            }
        }

        if (leftSteer != null && rightSteer != null)
        {
            return new[] { leftSteer, rightSteer };
        }

        return new Transform[0];
    }
}
