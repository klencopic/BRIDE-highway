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
    private static readonly Vector2 WindowSize = new Vector2(560f, 380f);
    private const float FieldLabelWidth = 220f;

    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform roadDirectionPointA;
    [SerializeField] private Transform roadDirectionPointB;
    [SerializeField] private Transform placementRig;
    [SerializeField] private GameObject groundObject;
    [SerializeField] private GameObject wallsObject;
    [SerializeField] private float cameraDistanceFromRig;
    [SerializeField] private float cameraDistanceFromGround;
    [SerializeField] private float cameraLateralSign = -1f;
    [SerializeField] private Vector3 cameraRotationRelativeToRig;

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

        EditorGUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(!CanCreateOrUpdateRig()))
        {
            if (GUILayout.Button("Use Current Camera Position", GUILayout.Height(36)))
            {
                UseCurrentCameraPosition();
            }
        }

        EditorGUILayout.Space(14f);
        DrawCameraValues();
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

        if (!TryFindInnerEdgeOnRight(groundPointBelowCamera, rigRotation * Vector3.right, out Vector3 innerEdgePoint))
        {
            EditorUtility.DisplayDialog(
                "Inner Edge Not Found",
                "Could not raycast from the camera line to colmesh_walls on the right side.",
                "OK");
            return;
        }

        rig.position = new Vector3(innerEdgePoint.x, 0f, innerEdgePoint.z);
        rig.rotation = rigRotation;
        rig.localScale = Vector3.one;
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

        EditorGUILayout.LabelField("Camera Values", sectionStyle);
        cameraDistanceFromRig = DrawFloatControl("Distance From Inner Highway Edge", cameraDistanceFromRig);
        cameraDistanceFromGround = DrawFloatControl("Distance From Ground", cameraDistanceFromGround);
        cameraRotationRelativeToRig = DrawVector3Control("Rotation Relative To Highway Direction", cameraRotationRelativeToRig);

        using (new EditorGUI.DisabledScope(cameraDistanceFromRig < 0f))
        {
            if (GUILayout.Button("Apply Camera Values", GUILayout.Height(36)))
            {
                ApplyCameraValues();
            }
        }
    }

    private float DrawFloatControl(string label, float value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, labelStyle, GUILayout.Width(270f));
        float newValue = EditorGUILayout.FloatField(value);
        EditorGUILayout.EndHorizontal();
        return newValue;
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
        Vector3 currentLocalPosition = placementRig.InverseTransformPoint(targetCamera.transform.position);
        if (Mathf.Abs(currentLocalPosition.x) > 0.0001f)
        {
            cameraLateralSign = Mathf.Sign(currentLocalPosition.x);
        }

        Vector3 newLocalPosition = new Vector3(
            cameraLateralSign * Mathf.Abs(cameraDistanceFromRig),
            cameraDistanceFromGround,
            currentLocalPosition.z);

        Undo.RecordObject(targetCamera.transform, "Apply Camera Values");
        targetCamera.transform.position = placementRig.TransformPoint(newLocalPosition);
        targetCamera.transform.rotation = placementRig.rotation * Quaternion.Euler(cameraRotationRelativeToRig);
    }

    private void SyncCameraValuesFromCurrentCamera()
    {
        if (targetCamera == null || placementRig == null)
        {
            return;
        }

        Vector3 localCameraPosition = placementRig.InverseTransformPoint(targetCamera.transform.position);
        cameraDistanceFromRig = Mathf.Abs(localCameraPosition.x);
        cameraDistanceFromGround = localCameraPosition.y;

        if (Mathf.Abs(localCameraPosition.x) > 0.0001f)
        {
            cameraLateralSign = Mathf.Sign(localCameraPosition.x);
        }

        Quaternion relativeRotation = Quaternion.Inverse(placementRig.rotation) * targetCamera.transform.rotation;
        cameraRotationRelativeToRig = NormalizeEuler(relativeRotation.eulerAngles);
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

    private bool TryFindInnerEdgeOnRight(Vector3 groundPointBelowCamera, Vector3 rightDirection, out Vector3 innerEdgePoint)
    {
        innerEdgePoint = groundPointBelowCamera;
        Vector3 rayOrigin = groundPointBelowCamera + Vector3.up * WallRayHeight;
        Ray wallRay = new Ray(rayOrigin, rightDirection.normalized);

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
