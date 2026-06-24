using UnityEditor;
using UnityEngine;

public class CameraPlacementRigWindow : EditorWindow
{
    private const string DefaultRigName = "CameraPlacementRig";
    private const string DefaultGroundObjectName = "colmesh_ground";
    private const string DefaultWallsObjectName = "colmesh_walls";
    private const float RaycastStartHeight = 10f;
    private const float RaycastDistance = 80f;
    private const float WallRayHeight = 0.25f;
    private static readonly Vector2 WindowSize = new Vector2(600f, 590f);
    private const float FieldLabelWidth = 220f;

    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform roadDirectionPointA;
    [SerializeField] private Transform roadDirectionPointB;
    [SerializeField] private Transform placementRig;
    [SerializeField] private GameObject groundObject;
    [SerializeField] private GameObject wallsObject;
    [SerializeField] private Vector3 cameraPositionRelativeToRig;
    [SerializeField] private Vector3 cameraRotationRelativeToRig;
    [SerializeField] private Transform cameraLookAtTarget;

    private Vector3 lastRigPosition;
    private Quaternion lastRigRotation;
    private Vector3 lastRigScale;
    private bool hasRigTransformSnapshot;

    private GUIStyle titleStyle;
    private GUIStyle instructionStyle;
    private GUIStyle sectionStyle;
    private GUIStyle labelStyle;

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

        if (placementRig == null)
        {
            GameObject existingRig = GameObject.Find(DefaultRigName);
            if (existingRig != null)
            {
                placementRig = existingRig.transform;
                SyncCameraValuesFromCurrentCamera();
            }
        }
    }

    private void OnInspectorUpdate()
    {
        Repaint();
    }

    private void OnGUI()
    {
        EnsureStyles();

        EditorGUILayout.LabelField("Camera Placement", titleStyle);
        EditorGUILayout.LabelField(
            "Initialize a reference point at the inner highway edge, then move it as needed. Camera position is expressed as local X, Y, and Z relative to that point.",
            instructionStyle);

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Inputs", sectionStyle);

        EditorGUIUtility.labelWidth = FieldLabelWidth;
        EditorGUI.BeginChangeCheck();
        Camera newTargetCamera = (Camera)EditorGUILayout.ObjectField("Camera", targetCamera, typeof(Camera), true);
        if (EditorGUI.EndChangeCheck())
        {
            targetCamera = newTargetCamera;
            SyncCameraValuesFromCurrentCamera();
        }
        roadDirectionPointA = (Transform)EditorGUILayout.ObjectField("Highway Direction A", roadDirectionPointA, typeof(Transform), true);
        roadDirectionPointB = (Transform)EditorGUILayout.ObjectField("Highway Direction B", roadDirectionPointB, typeof(Transform), true);
        groundObject = (GameObject)EditorGUILayout.ObjectField("Ground Object", groundObject, typeof(GameObject), true);
        wallsObject = (GameObject)EditorGUILayout.ObjectField("Walls Object", wallsObject, typeof(GameObject), true);
        EditorGUIUtility.labelWidth = 0f;

        AutoAssignSceneObjects();

        EditorGUILayout.Space(8f);
        DrawInputStatus();

        EditorGUILayout.Space(14f);
        DrawReferencePointControls();

        SyncCameraValuesIfRigMoved();

        EditorGUILayout.Space(14f);
        DrawCameraValues();
    }

    private void DrawReferencePointControls()
    {
        EditorGUILayout.LabelField("1. Reference Point", sectionStyle);
        EditorGUILayout.LabelField(
            "Initialize at the inner highway edge, then move the red reference marker in the Scene view or edit its world position below.",
            instructionStyle);

        using (new EditorGUI.DisabledScope(!CanCreateOrUpdateRig()))
        {
            if (GUILayout.Button("Initialize At Inner Highway Edge", GUILayout.Height(36)))
            {
                UseCurrentCameraPosition();
            }
        }

        if (placementRig == null)
        {
            return;
        }

        EditorGUI.BeginChangeCheck();
        Vector3 newReferencePosition = DrawVector3Control("Reference Point World Position", placementRig.position);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(placementRig, "Move Camera Reference Point");
            placementRig.position = newReferencePosition;
            SyncCameraValuesFromCurrentCamera();
            SceneView.RepaintAll();
        }
    }

    private bool CanCreateOrUpdateRig()
    {
        return targetCamera != null
            && roadDirectionPointA != null
            && roadDirectionPointB != null
            && roadDirectionPointA != roadDirectionPointB
            && wallsObject != null;
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

        if (placementRig != null)
        {
            EditorGUILayout.LabelField("Placement rig: " + placementRig.name, instructionStyle);
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

        labelStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 14,
            wordWrap = true
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

    private void UseCurrentCameraPosition()
    {
        Transform rig = GetOrCreateRig();
        Undo.RecordObject(rig, "Update Camera Placement Rig");
        Vector3 groundPointBelowCamera = GetGroundPointBelowCamera();
        Quaternion rigRotation = CalculateHighwayRotation();

        // The rig has a mirrored local X axis, so its positive X direction is
        // the rotation's left direction in world space.
        Vector3 rigPositiveX = rigRotation * Vector3.left;
        if (!TryFindInnerEdge(groundPointBelowCamera, rigPositiveX, out Vector3 innerEdgePoint))
        {
            EditorUtility.DisplayDialog(
                "Inner Edge Not Found",
                "Could not raycast from the camera line along the rig's positive X axis to colmesh_walls.",
                "OK");
            return;
        }

        rig.position = new Vector3(innerEdgePoint.x, 0f, innerEdgePoint.z);
        rig.rotation = rigRotation;
        rig.localScale = new Vector3(-1f, 1f, 1f);
        placementRig = rig;

        SyncCameraValuesFromCurrentCamera();
    }

    private Quaternion CalculateHighwayRotation()
    {
        Vector3 roadForward = roadDirectionPointB.position - roadDirectionPointA.position;
        roadForward.y = 0f;
        if (roadForward.sqrMagnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(roadForward.normalized, Vector3.up);
    }

    private void DrawCameraValues()
    {
        if (targetCamera == null || placementRig == null)
        {
            EditorGUILayout.LabelField("Use the current camera position to calculate camera values.", instructionStyle);
            return;
        }

        EditorGUILayout.LabelField("2. Camera Relative To Reference Point", sectionStyle);
        cameraPositionRelativeToRig = DrawVector3Control("Position", cameraPositionRelativeToRig);
        cameraRotationRelativeToRig = DrawVector3Control("Rotation", cameraRotationRelativeToRig);

        if (GUILayout.Button("Apply Camera Values", GUILayout.Height(36)))
        {
            ApplyCameraValues();
        }

        EditorGUILayout.Space(8f);
        cameraLookAtTarget = (Transform)EditorGUILayout.ObjectField(
            "Look At Target",
            cameraLookAtTarget,
            typeof(Transform),
            true);

        using (new EditorGUI.DisabledScope(cameraLookAtTarget == null))
        {
            if (GUILayout.Button("Point Camera At Target", GUILayout.Height(32)))
            {
                PointCameraAtTarget();
            }
        }
    }

    private Vector3 DrawVector3Control(string label, Vector3 value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, labelStyle, GUILayout.Width(270f));
        Vector3 newValue = EditorGUILayout.Vector3Field(GUIContent.none, value);
        EditorGUILayout.EndHorizontal();
        return newValue;
    }

    private void ApplyCameraValues()
    {
        Undo.RecordObject(targetCamera.transform, "Apply Camera Values");
        targetCamera.transform.position = placementRig.TransformPoint(cameraPositionRelativeToRig);
        targetCamera.transform.rotation = placementRig.rotation * Quaternion.Euler(cameraRotationRelativeToRig);
    }

    private void PointCameraAtTarget()
    {
        if (targetCamera == null || cameraLookAtTarget == null)
        {
            return;
        }

        Vector3 lookDirection = cameraLookAtTarget.position - targetCamera.transform.position;
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            EditorUtility.DisplayDialog(
                "Cannot Point Camera",
                "The camera and look-at target are at the same position.",
                "OK");
            return;
        }

        Undo.RecordObject(targetCamera.transform, "Point Camera At Target");
        targetCamera.transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        SyncCameraValuesFromCurrentCamera();
        SceneView.RepaintAll();
    }

    private void SyncCameraValuesFromCurrentCamera()
    {
        if (targetCamera == null || placementRig == null)
        {
            return;
        }

        cameraPositionRelativeToRig = placementRig.InverseTransformPoint(targetCamera.transform.position);

        Quaternion relativeRotation = Quaternion.Inverse(placementRig.rotation) * targetCamera.transform.rotation;
        cameraRotationRelativeToRig = NormalizeEuler(relativeRotation.eulerAngles);
        SaveRigTransformSnapshot();
    }

    private void SyncCameraValuesIfRigMoved()
    {
        if (targetCamera == null || placementRig == null)
        {
            return;
        }

        if (!hasRigTransformSnapshot
            || placementRig.position != lastRigPosition
            || placementRig.rotation != lastRigRotation
            || placementRig.localScale != lastRigScale)
        {
            SyncCameraValuesFromCurrentCamera();
        }
    }

    private void SaveRigTransformSnapshot()
    {
        lastRigPosition = placementRig.position;
        lastRigRotation = placementRig.rotation;
        lastRigScale = placementRig.localScale;
        hasRigTransformSnapshot = true;
    }

    private Vector3 GetGroundPointBelowCamera()
    {
        Vector3 cameraPosition = targetCamera.transform.position;
        Vector3 fallback = new Vector3(cameraPosition.x, 0f, cameraPosition.z);

        if (groundObject == null)
        {
            return fallback;
        }

        Ray ray = new Ray(cameraPosition + Vector3.up * RaycastStartHeight, Vector3.down);
        if (TryRaycastObject(groundObject, ray, RaycastStartHeight + RaycastDistance, out RaycastHit hit))
        {
            return hit.point;
        }

        return fallback;
    }

    private bool TryFindInnerEdge(Vector3 groundPointBelowCamera, Vector3 edgeDirection, out Vector3 innerEdgePoint)
    {
        innerEdgePoint = groundPointBelowCamera;
        Vector3 rayOrigin = groundPointBelowCamera + Vector3.up * WallRayHeight;
        Ray wallRay = new Ray(rayOrigin, edgeDirection.normalized);

        if (!TryRaycastObject(wallsObject, wallRay, RaycastDistance, out RaycastHit wallHit))
        {
            return false;
        }

        innerEdgePoint = wallHit.point;
        if (groundObject != null)
        {
            Ray groundRay = new Ray(wallHit.point + Vector3.up * RaycastStartHeight, Vector3.down);
            if (TryRaycastObject(groundObject, groundRay, RaycastStartHeight + RaycastDistance, out RaycastHit groundHit))
            {
                innerEdgePoint = groundHit.point;
            }
        }

        return true;
    }

    private static bool TryRaycastObject(GameObject target, Ray ray, float distance, out RaycastHit closestHit)
    {
        closestHit = default;
        Collider[] colliders = target.GetComponentsInChildren<Collider>();
        float closestDistance = float.PositiveInfinity;
        bool foundHit = false;

        foreach (Collider collider in colliders)
        {
            if (collider.Raycast(ray, out RaycastHit hit, distance) && hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                foundHit = true;
            }
        }

        return foundHit;
    }

    private Transform GetOrCreateRig()
    {
        if (placementRig != null)
        {
            return placementRig;
        }

        GameObject rigObject = GameObject.Find(DefaultRigName);
        if (rigObject == null)
        {
            rigObject = new GameObject(DefaultRigName);
            Undo.RegisterCreatedObjectUndo(rigObject, "Create Camera Placement Rig");
        }

        return rigObject.transform;
    }

    private static Vector3 NormalizeEuler(Vector3 eulerAngles)
    {
        return new Vector3(
            NormalizeAngle(eulerAngles.x),
            NormalizeAngle(eulerAngles.y),
            NormalizeAngle(eulerAngles.z));
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180f)
        {
            angle -= 360f;
        }

        while (angle < -180f)
        {
            angle += 360f;
        }

        return angle;
    }
}

[InitializeOnLoad]
internal static class CameraPlacementRigSceneMarker
{
    private const string RigName = "CameraPlacementRig";
    private static GUIStyle labelStyle;

    static CameraPlacementRigSceneMarker()
    {
        SceneView.duringSceneGui += DrawRigMarker;
    }

    private static void DrawRigMarker(SceneView sceneView)
    {
        GameObject rigObject = GameObject.Find(RigName);
        if (rigObject == null)
        {
            return;
        }

        Transform rig = rigObject.transform;
        float handleSize = HandleUtility.GetHandleSize(rig.position);
        float cubeSize = handleSize * 0.22f;

        Handles.color = new Color(0.9f, 0.05f, 0.05f, 1f);
        if (Handles.Button(
            rig.position,
            rig.rotation,
            cubeSize,
            cubeSize * 1.4f,
            Handles.CubeHandleCap))
        {
            Selection.activeTransform = rig;
            EditorGUIUtility.PingObject(rigObject);
        }

        EnsureLabelStyle();
        Vector3 labelPosition = rig.position + sceneView.camera.transform.up * (handleSize * 0.32f);
        Handles.Label(labelPosition, "REFERENCE POINT", labelStyle);
    }

    private static void EnsureLabelStyle()
    {
        if (labelStyle != null)
        {
            return;
        }

        labelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter
        };
        labelStyle.normal.textColor = new Color(1f, 0.12f, 0.12f, 1f);
    }
}
