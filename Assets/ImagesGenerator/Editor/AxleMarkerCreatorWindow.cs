using UnityEditor;
using UnityEngine;

public class AxleMarkerCreatorWindow : EditorWindow
{
    private const string ReferencePointsContainerName = "ReferencePoints";
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

        DrawDetectedAxles();

        EditorGUILayout.Space(12f);
        using (new EditorGUI.DisabledScope(truckRoot == null))
        {
            if (GUILayout.Button("Generate Axle Markers", buttonStyle, GUILayout.Height(42)))
            {
                CreateAxleMarkers();
            }
        }

        if (truckRoot == null)
        {
            EditorGUILayout.LabelField("Set Truck Root before generating markers.", instructionStyle);
        }
    }

    private void CreateAxleMarkers()
    {
        Transform[] axles = FindAxleTransforms();
        Transform[] steeringWheels = FindSteeringWheelPair();
        if (axles.Length == 0 && steeringWheels.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "No Axles Found",
                "No child transforms with 'Axle' in the name or steering wheel pair were found under the selected truck root.",
                "OK");
            return;
        }

        Transform referencePoints = GetOrCreateReferencePoints();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Axle Ground Markers");

        Transform lastMarker = null;
        if (steeringWheels.Length == 2)
        {
            Vector3 markerPosition = CalculateWheelMarkerPosition(steeringWheels[0], steeringWheels[1]);
            lastMarker = CreateOrUpdateMarker(referencePoints, "FrontAxleGroundCenter", markerPosition);
        }

        foreach (Transform axle in axles)
        {
            string detectedMarkerName = SanitizeMarkerName(axle.name) + "GroundCenter";
            Vector3 markerPosition = CalculateAxleMarkerPosition(axle);
            lastMarker = CreateOrUpdateMarker(referencePoints, detectedMarkerName, markerPosition);
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (lastMarker != null)
        {
            Selection.activeObject = lastMarker.gameObject;
            EditorGUIUtility.PingObject(lastMarker);
        }
    }

    private Transform CreateOrUpdateMarker(Transform referencePoints, string newMarkerName, Vector3 markerPosition)
    {
        Vector3 localMarkerPosition = referencePoints.InverseTransformPoint(markerPosition);
        localMarkerPosition.y = 0f;

        Transform existingMarker = referencePoints.Find(newMarkerName);
        if (existingMarker != null)
        {
            Undo.RecordObject(existingMarker, "Update Axle Ground Marker");
            existingMarker.localPosition = localMarkerPosition;
            existingMarker.localRotation = Quaternion.identity;
            return existingMarker;
        }

        GameObject marker = new GameObject(newMarkerName);
        Undo.RegisterCreatedObjectUndo(marker, "Create Axle Ground Marker");
        marker.transform.SetParent(referencePoints, false);
        marker.transform.localPosition = localMarkerPosition;
        marker.transform.localRotation = Quaternion.identity;
        return marker.transform;
    }

    private Transform GetOrCreateReferencePoints()
    {
        Transform existing = truckRoot.Find(ReferencePointsContainerName);
        if (existing != null)
        {
            AlignReferencePointsToRoadPlane(existing);
            return existing;
        }

        GameObject container = new GameObject(ReferencePointsContainerName);
        Undo.RegisterCreatedObjectUndo(container, "Create Reference Points Container");
        container.transform.SetParent(truckRoot, false);
        container.transform.localPosition = Vector3.zero;
        container.transform.localRotation = Quaternion.identity;
        container.transform.localScale = Vector3.one;
        AlignReferencePointsToRoadPlane(container.transform);
        return container.transform;
    }

    private void AlignReferencePointsToRoadPlane(Transform referencePoints)
    {
        Vector3 localRoadPoint = truckRoot.InverseTransformPoint(new Vector3(truckRoot.position.x, 0f, truckRoot.position.z));
        Vector3 localPosition = referencePoints.localPosition;
        localPosition.y = localRoadPoint.y;

        Undo.RecordObject(referencePoints, "Align Reference Points To Road Plane");
        referencePoints.localPosition = localPosition;
        referencePoints.localRotation = Quaternion.identity;
        referencePoints.localScale = Vector3.one;
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
                    + " -> " + FormatPosition(CalculateWheelMarkerPosition(steeringWheels[0], steeringWheels[1]))
                : "Front steering pair: not found",
            instructionStyle);

        if (axles.Length == 0)
        {
            EditorGUILayout.LabelField("Rear axles: none found", instructionStyle);
            return;
        }

        foreach (Transform axle in axles)
        {
            EditorGUILayout.LabelField(
                "Rear axle: " + axle.name + " -> " + FormatPosition(CalculateAxleMarkerPosition(axle)),
                instructionStyle);
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

    private Vector3 CalculateWheelMarkerPosition(Transform firstWheel, Transform secondWheel)
    {
        Vector3 midpoint = (firstWheel.position + secondWheel.position) * 0.5f;
        midpoint.y = 0f;
        return midpoint;
    }

    private Vector3 CalculateAxleMarkerPosition(Transform axle)
    {
        Vector3 axlePosition = axle.position;
        axlePosition.y = 0f;
        return axlePosition;
    }

    private static string FormatPosition(Vector3 position)
    {
        return string.Format("({0:0.###}, {1:0.###}, {2:0.###})", position.x, position.y, position.z);
    }

    private static string SanitizeMarkerName(string value)
    {
        foreach (char invalidCharacter in System.IO.Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter, '_');
        }

        return value.Replace(' ', '_');
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
