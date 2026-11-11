using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;


public class ImagesGeneratorWindow : EditorWindow
{

    public LicencePlateProjectionMetadata licencePlateProjection;
    private const string DefaultOutputDirectory = "ImagesGeneratorOutput";
    private const string DefaultMetadataReferencePointName = "CameraPlacementRig";

    private const string PositionReference =
        "Axle ground markers under Truck/ReferencePoints; " +
        "FrontAxleGroundCenter is the primary truck reference point";

    private const string ReferencePointsContainerName = "ReferencePoints";

    private static readonly Vector2 WindowSize = new Vector2(620f, 700f);

    private const int TruckSlotCount = 10;

    [SerializeField] private Camera captureCamera;
    [SerializeField] private GameObject truck;
    [SerializeField] private bool generateAllTrucks;
    [SerializeField] private GameObject[] trucks = new GameObject[TruckSlotCount];
    [SerializeField] private Transform metadataReferencePoint;

    private Vector2 scrollPosition;

    // All of these are expressed relative to CameraPlacementRig.
    [SerializeField] private Vector3 startPosition;
    [SerializeField] private Vector3 startRotation;
    [SerializeField] private Vector3 endPosition;

    // Gaussian position-distribution parameters.
    // X = lateral direction, Z = longitudinal direction.
    [SerializeField] private float gaussianMeanX;
    [SerializeField] private float gaussianStdX = 0.27f;
    [SerializeField] private float gaussianMeanZ;
    [SerializeField] private float gaussianStdZ = 1.0f;
    [SerializeField] private int randomSeed = 12345;

    // Default lateral mean is shifted 0.15 m from the X-range midpoint.
    // If the hard shoulder is in the negative-X direction in a scene,
    // simply enter the corresponding mean manually in the UI.
    private const float DefaultLateralMeanOffsetM = 0.15f;
    private const float DefaultLateralStdM = 0.27f;

    private bool gaussianDefaultsInitialized;

    [SerializeField] private int imageCount = 10;
    [SerializeField] private int imageWidth = 1920;
    [SerializeField] private int imageHeight = 1080;
    [SerializeField] private string outputDirectory = DefaultOutputDirectory;

    // Bound the cost of large high-resolution image batches. Zero disables an option.
    [SerializeField] private int collectGarbageEveryNImages = 50;
    [SerializeField] private int saveMetadataEveryNImages = 250;
    [SerializeField] private int logMemoryEveryNImages = 250;

    private const int ProgressUpdateEveryNImages = 10;

    [SerializeField] private TimeOfDay lightingTimeOfDay = TimeOfDay.Daytime;
    [SerializeField] private bool fogEnabled;
    [SerializeField] private bool rainEnabled;

    private bool hasStartPosition;
    private bool hasEndPosition;

    [MenuItem("Tools/Open Images Generator")]
    public static void Open()
    {
        ImagesGeneratorWindow window =
            CreateInstance<ImagesGeneratorWindow>();

        window.titleContent =
            new GUIContent("Images Generator");

        window.minSize = WindowSize;
        window.ShowUtility();
    }

    private void OnEnable()
    {
        EnsureTruckArraySize();

        if (metadataReferencePoint == null)
        {
            GameObject referencePointObject =
                GameObject.Find(DefaultMetadataReferencePointName);

            if (referencePointObject != null)
            {
                metadataReferencePoint =
                    referencePointObject.transform;
            }
        }
    }

    private void OnGUI()
    {
        EnsureTruckArraySize();

        scrollPosition =
            EditorGUILayout.BeginScrollView(
                scrollPosition
            );

        EditorGUILayout.LabelField(
            "Images Generator",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "All truck positions and rotations in this tool are expressed " +
            "relative to CameraPlacementRig.",
            MessageType.Info
        );

        captureCamera =
            (Camera)EditorGUILayout.ObjectField(
                "Camera",
                captureCamera,
                typeof(Camera),
                true
            );

        generateAllTrucks =
            EditorGUILayout.Toggle(
                "Generate All Trucks",
                generateAllTrucks
            );

        if (generateAllTrucks)
        {
            EditorGUILayout.LabelField(
                "Truck References",
                EditorStyles.boldLabel
            );

            for (int i = 0; i < TruckSlotCount; i++)
            {
                trucks[i] =
                    (GameObject)EditorGUILayout.ObjectField(
                        "Truck " + (i + 1),
                        trucks[i],
                        typeof(GameObject),
                        true
                    );
            }

            EditorGUILayout.HelpBox(
                "Assign the complete root GameObject for each truck. " +
                "Only these 10 supplied GameObjects are moved and activated/deactivated; " +
                "the generator does not change visibility on child GameObjects. " +
                "All trucks receive exactly the same transform for each sampled position.",
                MessageType.Info
            );

            if (!AllTruckSlotsAssigned())
            {
                EditorGUILayout.HelpBox(
                    "All 10 truck references must be assigned.",
                    MessageType.Warning
                );
            }
            else if (!TruckReferencesAreUnique())
            {
                EditorGUILayout.HelpBox(
                    "Each truck reference must point to a different GameObject.",
                    MessageType.Warning
                );
            }
            else if (!TruckReferencesAreIndependentRoots())
            {
                EditorGUILayout.HelpBox(
                    "Truck references must not be children of one another. " +
                    "Reference the complete independent root GameObject for each truck.",
                    MessageType.Warning
                );
            }
        }
        else
        {
            truck =
                (GameObject)EditorGUILayout.ObjectField(
                    "Truck",
                    truck,
                    typeof(GameObject),
                    true
                );
        }

        metadataReferencePoint =
            (Transform)EditorGUILayout.ObjectField(
                "Reference Frame",
                metadataReferencePoint,
                typeof(Transform),
                true
            );

        EditorGUILayout.Space();

        DrawPositionRangeFields();

        EditorGUILayout.Space();

        DrawGaussianDistributionFields();

        EditorGUILayout.Space();

        imageCount =
            EditorGUILayout.IntField(
                generateAllTrucks
                    ? "Images Per Truck"
                    : "Number Of Images",
                imageCount
            );

        if (generateAllTrucks && imageCount > 0)
        {
            EditorGUILayout.LabelField(
                "Total Images",
                (imageCount * TruckSlotCount).ToString(
                    CultureInfo.InvariantCulture
                )
            );
        }

        imageWidth =
            EditorGUILayout.IntField(
                "Image Width",
                imageWidth
            );

        imageHeight =
            EditorGUILayout.IntField(
                "Image Height",
                imageHeight
            );

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Large Batch Memory", EditorStyles.boldLabel);
        collectGarbageEveryNImages = Mathf.Max(0,
            EditorGUILayout.IntField("GC Every N Images (0 = off)", collectGarbageEveryNImages));
        saveMetadataEveryNImages = Mathf.Max(0,
            EditorGUILayout.IntField("Save Metadata Every N (0 = off)", saveMetadataEveryNImages));
        logMemoryEveryNImages = Mathf.Max(0,
            EditorGUILayout.IntField("Log Memory Every N (0 = off)", logMemoryEveryNImages));
        EditorGUILayout.HelpBox(
            "For 1920x1440 batches, start with GC=50 and metadata checkpoints=250. " +
            "Save output outside the Unity project's Assets directory.",
            MessageType.Info);

        EditorGUILayout.Space();

        DrawLightingFields();

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();

        outputDirectory =
            EditorGUILayout.TextField(
                "Output Directory",
                outputDirectory
            );

        if (GUILayout.Button("Choose", GUILayout.Width(70f)))
        {
            ChooseOutputDirectory();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(!CanGenerate()))
        {
            if (GUILayout.Button(
                    "Generate Images",
                    GUILayout.Height(34f)))
            {
                GenerateImages();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawLightingFields()
    {
        EditorGUILayout.LabelField(
            "Lighting",
            EditorStyles.boldLabel
        );

        lightingTimeOfDay =
            (TimeOfDay)EditorGUILayout.EnumPopup(
                "Time Of Day",
                lightingTimeOfDay
            );

        fogEnabled =
            EditorGUILayout.Toggle(
                "Fog",
                fogEnabled
            );

        rainEnabled =
            EditorGUILayout.Toggle(
                "Rain",
                rainEnabled
            );
    }

    private void DrawPositionRangeFields()
    {
        EditorGUILayout.LabelField(
            "Truck Transform",
            EditorStyles.boldLabel
        );

        GameObject referenceTruck =
            GetReferenceTruck();

        if (metadataReferencePoint == null)
        {
            EditorGUILayout.HelpBox(
                "A reference frame is required.",
                MessageType.Warning
            );
        }

        using (new EditorGUI.DisabledScope(
                   referenceTruck == null ||
                   metadataReferencePoint == null))
        {
            EditorGUI.BeginChangeCheck();

            startPosition =
                EditorGUILayout.Vector3Field(
                    "Start Position",
                    startPosition
                );

            if (EditorGUI.EndChangeCheck())
            {
                hasStartPosition = true;
                gaussianDefaultsInitialized = false;
            }

            startRotation =
                EditorGUILayout.Vector3Field(
                    "Start Rotation",
                    startRotation
                );

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Use Truck Transform"))
            {
                CaptureTruckAsStartTransform();
            }

            if (GUILayout.Button(
                    generateAllTrucks
                        ? "Apply Start Transform To All Trucks"
                        : "Apply Start Transform To Truck"))
            {
                ApplyStartTransformToTruck();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);

            EditorGUI.BeginChangeCheck();

            endPosition =
                EditorGUILayout.Vector3Field(
                    "End Position",
                    endPosition
                );

            if (EditorGUI.EndChangeCheck())
            {
                hasEndPosition = true;
                gaussianDefaultsInitialized = false;
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Use Truck Position As End"))
            {
                endPosition =
                    metadataReferencePoint.InverseTransformPoint(
                        referenceTruck.transform.position
                    );

                hasEndPosition = true;
                gaussianDefaultsInitialized = false;
            }

            using (new EditorGUI.DisabledScope(!hasEndPosition))
            {
                if (GUILayout.Button(
                        generateAllTrucks
                            ? "Apply End Position To All Trucks"
                            : "Apply End Position To Truck"))
                {
                    ApplyEndPositionToTruck();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        if (referenceTruck == null)
        {
            EditorGUILayout.HelpBox(
                generateAllTrucks
                    ? "Assign Truck 1 to use the transform controls."
                    : "Select a truck.",
                MessageType.Warning
            );
        }
        else if (metadataReferencePoint == null)
        {
            EditorGUILayout.HelpBox(
                "Select or create CameraPlacementRig.",
                MessageType.Warning
            );
        }
        else if (!hasStartPosition || !hasEndPosition)
        {
            EditorGUILayout.HelpBox(
                "Define both start and end positions before generating.",
                MessageType.Warning
            );
        }
    }

    private void DrawGaussianDistributionFields()
    {
        EditorGUILayout.LabelField(
            "Gaussian Position Distribution",
            EditorStyles.boldLabel
        );

        if (hasStartPosition &&
            hasEndPosition &&
            !gaussianDefaultsInitialized)
        {
            ResetGaussianDefaultsFromRange();
        }

        using (new EditorGUI.DisabledScope(
                   !hasStartPosition || !hasEndPosition))
        {
            gaussianMeanX =
                EditorGUILayout.FloatField(
                    "Mean X [m]",
                    gaussianMeanX
                );

            gaussianStdX =
                Mathf.Max(
                    0f,
                    EditorGUILayout.FloatField(
                        "STD X [m]",
                        gaussianStdX
                    )
                );

            gaussianMeanZ =
                EditorGUILayout.FloatField(
                    "Mean Z [m]",
                    gaussianMeanZ
                );

            gaussianStdZ =
                Mathf.Max(
                    0f,
                    EditorGUILayout.FloatField(
                        "STD Z [m]",
                        gaussianStdZ
                    )
                );

            randomSeed =
                EditorGUILayout.IntField(
                    "Random Seed",
                    randomSeed
                );

            if (GUILayout.Button(
                    "Reset Distribution Defaults From Range"))
            {
                ResetGaussianDefaultsFromRange();
            }
        }

        EditorGUILayout.HelpBox(
            "X and Z are sampled independently from Gaussian distributions. " +
            "Start and End Position act as hard bounds. Mean and standard " +
            "deviation can be edited independently for each axis.",
            MessageType.Info
        );

        if (hasStartPosition && hasEndPosition)
        {
            float minX = Mathf.Min(startPosition.x, endPosition.x);
            float maxX = Mathf.Max(startPosition.x, endPosition.x);
            float minZ = Mathf.Min(startPosition.z, endPosition.z);
            float maxZ = Mathf.Max(startPosition.z, endPosition.z);

            if (gaussianMeanX < minX || gaussianMeanX > maxX ||
                gaussianMeanZ < minZ || gaussianMeanZ > maxZ)
            {
                EditorGUILayout.HelpBox(
                    "The Gaussian mean should lie inside the corresponding " +
                    "Start/End bounds.",
                    MessageType.Warning
                );
            }
        }
    }

    private void ResetGaussianDefaultsFromRange()
    {
        float midpointX =
            0.5f * (startPosition.x + endPosition.x);

        float midpointZ =
            0.5f * (startPosition.z + endPosition.z);

        gaussianMeanX =
            Mathf.Clamp(
                midpointX + DefaultLateralMeanOffsetM,
                Mathf.Min(startPosition.x, endPosition.x),
                Mathf.Max(startPosition.x, endPosition.x)
            );

        gaussianStdX =
            DefaultLateralStdM;

        gaussianMeanZ =
            midpointZ;

        float zRange =
            Mathf.Abs(endPosition.z - startPosition.z);

        // The default Z spread places the configured bounds at roughly
        // +/- 2 standard deviations around the midpoint.
        gaussianStdZ =
            zRange > 0f
                ? zRange / 4f
                : 0f;

        gaussianDefaultsInitialized = true;
    }

    private void CaptureTruckAsStartTransform()
    {
        GameObject referenceTruck =
            GetReferenceTruck();

        if (referenceTruck == null ||
            metadataReferencePoint == null)
        {
            return;
        }

        startPosition =
            metadataReferencePoint.InverseTransformPoint(
                referenceTruck.transform.position
            );

        startRotation =
            ToReferencePointRotation(
                referenceTruck.transform.rotation
            );

        hasStartPosition = true;
        gaussianDefaultsInitialized = false;
    }

    private void ApplyStartTransformToTruck()
    {
        if (metadataReferencePoint == null)
        {
            return;
        }

        Vector3 worldPosition =
            metadataReferencePoint.TransformPoint(
                startPosition
            );

        Quaternion worldRotation =
            ReferencePointRotationToWorld(
                startRotation
            );

        ApplyTransformToGenerationTrucks(
            worldPosition,
            worldRotation,
            "Apply Truck Start Transform"
        );
    }

    private void ApplyEndPositionToTruck()
    {
        if (metadataReferencePoint == null ||
            !hasEndPosition)
        {
            return;
        }

        Vector3 worldPosition =
            metadataReferencePoint.TransformPoint(
                endPosition
            );

        Quaternion worldRotation =
            ReferencePointRotationToWorld(
                startRotation
            );

        ApplyTransformToGenerationTrucks(
            worldPosition,
            worldRotation,
            "Apply Truck End Transform"
        );
    }

    private void ApplyTransformToGenerationTrucks(
        Vector3 worldPosition,
        Quaternion worldRotation,
        string undoName)
    {
        List<GameObject> generationTrucks =
            GetGenerationTrucks();

        foreach (GameObject currentTruck in generationTrucks)
        {
            if (currentTruck == null)
            {
                continue;
            }

            Undo.RecordObject(
                currentTruck.transform,
                undoName
            );

            currentTruck.transform.SetPositionAndRotation(
                worldPosition,
                worldRotation
            );

            EditorUtility.SetDirty(
                currentTruck.transform
            );
        }

        SceneView.RepaintAll();
    }

    private void EnsureTruckArraySize()
    {
        if (trucks != null &&
            trucks.Length == TruckSlotCount)
        {
            return;
        }

        GameObject[] resized =
            new GameObject[TruckSlotCount];

        if (trucks != null)
        {
            int copyCount =
                Mathf.Min(
                    trucks.Length,
                    TruckSlotCount
                );

            for (int i = 0; i < copyCount; i++)
            {
                resized[i] = trucks[i];
            }
        }

        trucks = resized;
    }

    private GameObject GetReferenceTruck()
    {
        if (!generateAllTrucks)
        {
            return truck;
        }

        EnsureTruckArraySize();
        return trucks[0];
    }

    private List<GameObject> GetGenerationTrucks()
    {
        List<GameObject> result =
            new List<GameObject>();

        if (!generateAllTrucks)
        {
            if (truck != null)
            {
                result.Add(truck);
            }

            return result;
        }

        EnsureTruckArraySize();

        for (int i = 0; i < TruckSlotCount; i++)
        {
            if (trucks[i] != null)
            {
                result.Add(trucks[i]);
            }
        }

        return result;
    }

    private bool AllTruckSlotsAssigned()
    {
        EnsureTruckArraySize();

        for (int i = 0; i < TruckSlotCount; i++)
        {
            if (trucks[i] == null)
            {
                return false;
            }
        }

        return true;
    }

    private bool TruckReferencesAreUnique()
    {
        EnsureTruckArraySize();

        HashSet<GameObject> seen =
            new HashSet<GameObject>();

        for (int i = 0; i < TruckSlotCount; i++)
        {
            if (trucks[i] == null ||
                !seen.Add(trucks[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool TruckReferencesAreIndependentRoots()
    {
        EnsureTruckArraySize();

        for (int i = 0; i < TruckSlotCount; i++)
        {
            if (trucks[i] == null)
            {
                return false;
            }

            for (int j = 0; j < TruckSlotCount; j++)
            {
                if (i == j || trucks[j] == null)
                {
                    continue;
                }

                if (trucks[i].transform.IsChildOf(
                        trucks[j].transform))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool CanGenerate()
    {
        bool trucksValid =
            generateAllTrucks
                ? AllTruckSlotsAssigned() &&
                  TruckReferencesAreUnique() &&
                  TruckReferencesAreIndependentRoots()
                : truck != null;

        return captureCamera != null
            && trucksValid
            && metadataReferencePoint != null
            && hasStartPosition
            && hasEndPosition
            && imageCount > 0
            && imageWidth > 0
            && imageHeight > 0
            && GaussianSettingsAreValid()
            && !string.IsNullOrWhiteSpace(outputDirectory);
    }

    private bool GaussianSettingsAreValid()
    {
        float minX = Mathf.Min(startPosition.x, endPosition.x);
        float maxX = Mathf.Max(startPosition.x, endPosition.x);
        float minZ = Mathf.Min(startPosition.z, endPosition.z);
        float maxZ = Mathf.Max(startPosition.z, endPosition.z);

        return gaussianStdX >= 0f
            && gaussianStdZ >= 0f
            && gaussianMeanX >= minX
            && gaussianMeanX <= maxX
            && gaussianMeanZ >= minZ
            && gaussianMeanZ <= maxZ;
    }

    private void ChooseOutputDirectory()
    {
        string selectedDirectory =
            EditorUtility.OpenFolderPanel(
                "Select Images Output Directory",
                GetAbsoluteOutputDirectory(),
                string.Empty
            );

        if (!string.IsNullOrEmpty(selectedDirectory))
        {
            outputDirectory = selectedDirectory;
        }
    }

    private void GenerateImages()
    {
        if (!CanGenerate())
        {
            EditorUtility.DisplayDialog(
                "Invalid Configuration",
                "Complete all image generator settings first.",
                "OK"
            );

            return;
        }

        List<GameObject> generationTrucks =
            GetGenerationTrucks();

        string runTimestamp =
            DateTime.Now.ToString(
                "dd_MM_yyyy_HH_mm",
                CultureInfo.InvariantCulture
            );

        string runDirectoryName;

        if (generateAllTrucks)
        {
            runDirectoryName =
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_AllTrucks_{1}",
                    SanitizePathPart(captureCamera.name),
                    runTimestamp
                );
        }
        else
        {
            runDirectoryName =
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}_{2}",
                    SanitizePathPart(captureCamera.name),
                    SanitizePathPart(generationTrucks[0].name),
                    runTimestamp
                );
        }

        string runDirectory =
            Path.Combine(
                GetAbsoluteOutputDirectory(),
                runDirectoryName
            );

        string imagesDirectory =
            Path.Combine(
                runDirectory,
                "images"
            );

        string metadataPath =
            Path.Combine(
                runDirectory,
                "metadata.json"
            );

        Directory.CreateDirectory(imagesDirectory);

        // Files generated under Assets may be imported and cached by the Editor.
        // This can make a long capture run consume much more memory.
        string assetsRoot = Path.GetFullPath(Application.dataPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(imagesDirectory).StartsWith(
                assetsRoot, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning(
                "[ImagesGenerator] Output is inside Assets. " +
                "For large batches, choose a directory outside the Unity project Assets folder.");
        }

        List<TruckSceneState> originalTruckStates =
            new List<TruckSceneState>(
                generationTrucks.Count
            );

        foreach (GameObject currentTruck in generationTrucks)
        {
            originalTruckStates.Add(
                new TruckSceneState
                {
                    truck = currentTruck,
                    position = currentTruck.transform.position,
                    rotation = currentTruck.transform.rotation,
                    activeSelf = currentTruck.activeSelf
                }
            );
        }

        RenderTexture originalTargetTexture =
            captureCamera.targetTexture;

        RenderTexture renderTexture = null;
        Texture2D image = null;
        List<ImageMetadata> metadataImages = null;
        bool cancelled = false;

        try
        {
            WriteRunConfiguration(
                runDirectory,
                runTimestamp,
                generationTrucks
            );

            renderTexture =
                new RenderTexture(
                    imageWidth,
                    imageHeight,
                    24,
                    RenderTextureFormat.ARGB32
                );

            image =
                new Texture2D(
                    imageWidth,
                    imageHeight,
                    TextureFormat.RGB24,
                    false
                );

            captureCamera.targetTexture =
                renderTexture;

            int totalImageCount =
                generationTrucks.Count * imageCount;

            metadataImages =
                new List<ImageMetadata>(
                    totalImageCount
                );

            Quaternion worldRotation =
                ReferencePointRotationToWorld(
                    startRotation
                );

            System.Random random =
                new System.Random(randomSeed);

            int globalImageIndex = 0;

            for (int sampleIndex = 0;
                 sampleIndex < imageCount;
                 sampleIndex++)
            {
                float normalizedPositionX;
                float normalizedPositionZ;

                Vector3 referencePosition =
                    SampleGaussianReferencePosition(
                        random,
                        out normalizedPositionX,
                        out normalizedPositionZ
                    );

                Vector3 worldPosition =
                    metadataReferencePoint.TransformPoint(
                        referencePosition
                    );

                // IMPORTANT: apply the exact same ROOT transform to every
                // supplied truck before any images for this sample are taken.
                // No child GameObject transforms are modified here.
                foreach (GameObject currentTruck in generationTrucks)
                {
                    currentTruck.transform.SetPositionAndRotation(
                        worldPosition,
                        worldRotation
                    );
                }

                for (int truckIndex = 0;
                     truckIndex < generationTrucks.Count;
                     truckIndex++)
                {
                    GameObject currentTruck =
                        generationTrucks[truckIndex];

                    if (generateAllTrucks)
                    {
                        SetOnlyTruckActive(
                            generationTrucks,
                            truckIndex
                        );

                        // Reapply the requested root transform after activation
                        // in case a component reacts to OnEnable.
                        currentTruck.transform.SetPositionAndRotation(
                            worldPosition,
                            worldRotation
                        );
                    }

                    globalImageIndex++;

                    string filename;

                    if (generateAllTrucks)
                    {
                        filename =
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "sample_{0:D6}_truck_{1:D2}_{2}.png",
                                sampleIndex + 1,
                                truckIndex + 1,
                                SanitizePathPart(currentTruck.name)
                            );
                    }
                    else
                    {
                        filename =
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "image_{0:D6}.png",
                                sampleIndex + 1
                            );
                    }

                    string imagePath =
                        Path.Combine(
                            imagesDirectory,
                            filename
                        );

                    CaptureImage(
                        renderTexture,
                        image,
                        imagePath
                    );

                    metadataImages.Add(
                        CreateImageMetadata(
                            globalImageIndex,
                            sampleIndex + 1,
                            truckIndex + 1,
                            filename,
                            normalizedPositionX,
                            normalizedPositionZ,
                            currentTruck
                        )
                    );

                    // Checkpoint metadata during the run so a crash does not
                    // discard all metadata for already-saved images.
                    if (saveMetadataEveryNImages > 0 &&
                        globalImageIndex % saveMetadataEveryNImages == 0)
                    {
                        WriteMetadata(
                            metadataPath, runTimestamp, metadataImages, generationTrucks);
                    }

                    // EncodeToPNG creates a new byte[] for every image. Force
                    // periodic collections instead of accumulating thousands
                    // of large unreachable buffers in one uninterrupted loop.
                    if (collectGarbageEveryNImages > 0 &&
                        globalImageIndex % collectGarbageEveryNImages == 0)
                    {
                        GC.Collect();
                    }

                    if (logMemoryEveryNImages > 0 &&
                        globalImageIndex % logMemoryEveryNImages == 0)
                    {
                        LogMemoryUsage(globalImageIndex, totalImageCount);
                    }

                    // Updating UI for every high-resolution image adds overhead.
                    // The cancel button also saves partial results cleanly.
                    if (globalImageIndex % ProgressUpdateEveryNImages == 0 ||
                        globalImageIndex == totalImageCount)
                    {
                        cancelled = EditorUtility.DisplayCancelableProgressBar(
                            "Generating Images",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Sample {0}/{1}, truck {2}/{3}: {4}",
                                sampleIndex + 1,
                                imageCount,
                                truckIndex + 1,
                                generationTrucks.Count,
                                currentTruck.name
                            ),
                            globalImageIndex / (float)totalImageCount);
                        if (cancelled)
                        {
                            break;
                        }
                    }
                }

                if (cancelled)
                {
                    break;
                }
            }

            WriteMetadata(
                metadataPath,
                runTimestamp,
                metadataImages,
                generationTrucks
            );

            Debug.Log(
                (cancelled ? "Image generation cancelled; partial data saved at: "
                           : "Generated images at: ") +
                runDirectory + " (" + metadataImages.Count + " images)"
            );

            if (!cancelled)
            {
                EditorUtility.RevealInFinder(runDirectory);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);

            // Best effort: preserve metadata for images finished before an
            // exception. Earlier periodic checkpoints also remain on disk.
            if (metadataImages != null && metadataImages.Count > 0)
            {
                try
                {
                    WriteMetadata(
                        metadataPath, runTimestamp, metadataImages, generationTrucks);
                }
                catch (Exception checkpointException)
                {
                    Debug.LogWarning(
                        "[ImagesGenerator] Could not write final partial metadata: " +
                        checkpointException.Message);
                }
            }

            EditorUtility.DisplayDialog(
                "Image Generation Failed",
                exception.Message,
                "OK"
            );
        }
        finally
        {
            foreach (TruckSceneState state in originalTruckStates)
            {
                if (state.truck == null)
                {
                    continue;
                }

                state.truck.transform.SetPositionAndRotation(
                    state.position,
                    state.rotation
                );

                state.truck.SetActive(
                    state.activeSelf
                );
            }

            captureCamera.targetTexture =
                originalTargetTexture;

            RenderTexture.active = null;

            EditorUtility.ClearProgressBar();

            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyImmediate(renderTexture);
            }

            if (image != null)
            {
                DestroyImmediate(image);
            }

            SceneView.RepaintAll();
        }
    }

    private static void SetOnlyTruckActive(
        List<GameObject> generationTrucks,
        int activeTruckIndex)
    {
        for (int i = 0;
             i < generationTrucks.Count;
             i++)
        {
            GameObject currentTruck =
                generationTrucks[i];

            bool shouldBeActive =
                i == activeTruckIndex;

            if (currentTruck.activeSelf != shouldBeActive)
            {
                // Visibility is changed only at the supplied truck root.
                // No child GameObjects are individually modified.
                currentTruck.SetActive(
                    shouldBeActive
                );
            }
        }
    }

    private Vector3 SampleGaussianReferencePosition(
        System.Random random,
        out float normalizedPositionX,
        out float normalizedPositionZ)
    {
        float minX = Mathf.Min(startPosition.x, endPosition.x);
        float maxX = Mathf.Max(startPosition.x, endPosition.x);
        float minZ = Mathf.Min(startPosition.z, endPosition.z);
        float maxZ = Mathf.Max(startPosition.z, endPosition.z);

        float sampledX =
            SampleTruncatedGaussian(
                random,
                gaussianMeanX,
                gaussianStdX,
                minX,
                maxX
            );

        float sampledZ =
            SampleTruncatedGaussian(
                random,
                gaussianMeanZ,
                gaussianStdZ,
                minZ,
                maxZ
            );

        // Only X and Z are randomized. Keep the truck at the start Y level.
        float sampledY = startPosition.y;

        normalizedPositionX =
            NormalizedValueInRange(
                sampledX,
                startPosition.x,
                endPosition.x
            );

        normalizedPositionZ =
            NormalizedValueInRange(
                sampledZ,
                startPosition.z,
                endPosition.z
            );

        return new Vector3(
            sampledX,
            sampledY,
            sampledZ
        );
    }

    private static float SampleTruncatedGaussian(
        System.Random random,
        float mean,
        float standardDeviation,
        float minValue,
        float maxValue)
    {
        if (maxValue <= minValue)
        {
            return minValue;
        }

        if (standardDeviation <= 0f)
        {
            return Mathf.Clamp(mean, minValue, maxValue);
        }

        // Rejection sampling keeps the Gaussian distribution while enforcing
        // Start/End Position as hard limits.
        for (int attempt = 0; attempt < 256; attempt++)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();

            double standardNormal =
                Math.Sqrt(-2.0 * Math.Log(u1)) *
                Math.Cos(2.0 * Math.PI * u2);

            float sample =
                mean +
                standardDeviation * (float)standardNormal;

            if (sample >= minValue && sample <= maxValue)
            {
                return sample;
            }
        }

        // Defensive fallback for extreme user-entered parameters.
        return Mathf.Clamp(mean, minValue, maxValue);
    }

    private static float NormalizedValueInRange(
        float value,
        float start,
        float end)
    {
        float denominator = end - start;

        if (Mathf.Abs(denominator) < 1e-6f)
        {
            return 0.5f;
        }

        return Mathf.Clamp01(
            (value - start) / denominator
        );
    }

    private void CaptureImage(
        RenderTexture renderTexture,
        Texture2D image,
        string imagePath)
    {
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            captureCamera.Render();
            RenderTexture.active = renderTexture;

            // CPU readback only. No mipmap rebuild and no unnecessary
            // Texture2D.Apply() GPU upload before encoding to PNG.
            image.ReadPixels(
                new Rect(0, 0, imageWidth, imageHeight),
                0,
                0,
                false
            );

            byte[] pngBytes = image.EncodeToPNG();
            if (pngBytes == null || pngBytes.Length == 0)
            {
                throw new IOException("PNG encoding failed for " + imagePath);
            }

            File.WriteAllBytes(imagePath, pngBytes);
            // pngBytes goes out of scope after this call; the periodic GC in
            // GenerateImages reclaims these large temporary managed arrays.
        }
        finally
        {
            RenderTexture.active = previousActive;
        }
    }

    private static void LogMemoryUsage(int completed, int total)
    {
        const double MiB = 1024.0 * 1024.0;
        long editorResidentBytes = 0L;

        try
        {
            using (System.Diagnostics.Process process =
                System.Diagnostics.Process.GetCurrentProcess())
            {
                process.Refresh();
                editorResidentBytes = process.WorkingSet64;
            }
        }
        catch (Exception)
        {
            // Process memory information may not be available on all platforms.
        }

        Debug.Log(string.Format(
            CultureInfo.InvariantCulture,
            "[ImagesGenerator] {0}/{1} | Editor RSS={2:F1} MiB | " +
            "Managed live={3:F1} MiB | Unity allocated={4:F1} MiB | " +
            "Unity reserved={5:F1} MiB",
            completed,
            total,
            editorResidentBytes / MiB,
            GC.GetTotalMemory(false) / MiB,
            UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / MiB,
            UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / MiB
        ));
    }

    private void WriteRunConfiguration(
        string runDirectory,
        string runTimestamp,
        List<GameObject> generationTrucks)
    {
        RunConfiguration configuration =
            CreateRunConfiguration(
                runTimestamp,
                generationTrucks
            );

        File.WriteAllText(
            Path.Combine(
                runDirectory,
                "run_config.json"
            ),
            JsonUtility.ToJson(
                configuration,
                true
            )
        );
    }

    private void WriteMetadata(
        string metadataPath,
        string runTimestamp,
        List<ImageMetadata> metadataImages,
        List<GameObject> generationTrucks)
    {
        MetadataFile metadata =
            new MetadataFile
            {
                run =
                    CreateRunConfiguration(
                        runTimestamp,
                        generationTrucks
                    ),

                images =
                    metadataImages.ToArray()
            };

        File.WriteAllText(
            metadataPath,
            JsonUtility.ToJson(
                metadata,
                true
            )
        );
    }

    private RunConfiguration CreateRunConfiguration(
        string runTimestamp,
        List<GameObject> generationTrucks)
    {
        string[] truckNames =
            new string[generationTrucks.Count];

        for (int i = 0;
             i < generationTrucks.Count;
             i++)
        {
            truckNames[i] =
                generationTrucks[i].name;
        }

        return new RunConfiguration
        {
            generatedAt =
                runTimestamp,

            scene =
                captureCamera.gameObject.scene.path,

            camera =
                captureCamera.name,

            truck =
                generateAllTrucks
                    ? "ALL_TRUCKS"
                    : generationTrucks[0].name,

            trucks =
                truckNames,

            generateAllTrucks =
                generateAllTrucks,

            truckCount =
                generationTrucks.Count,

            imageCount =
                imageCount,

            imagesPerTruck =
                imageCount,

            totalImageCount =
                imageCount * generationTrucks.Count,

            imageWidth =
                imageWidth,

            imageHeight =
                imageHeight,

            referencePoint =
                metadataReferencePoint.name,

            startPosition =
                SerializableVector3.From(
                    startPosition
                ),

            startRotationEuler =
                SerializableVector3.From(
                    startRotation
                ),

            endPosition =
                SerializableVector3.From(
                    endPosition
                ),

            gaussianMeanX =
                gaussianMeanX,

            gaussianStdX =
                gaussianStdX,

            gaussianMeanZ =
                gaussianMeanZ,

            gaussianStdZ =
                gaussianStdZ,

            randomSeed =
                randomSeed,

            positionReference =
                PositionReference,

            coordinateReferenceFrame =
                GetCoordinateReferenceFrameDescription(),

            lighting =
                CreateLightingMetadata()
        };
    }

    private ImageMetadata CreateImageMetadata(
        int imageIndex,
        int sampleIndex,
        int truckIndex,
        string filename,
        float normalizedPositionX,
        float normalizedPositionZ,
        GameObject currentTruck)
    {

        ImageMetadata metadata =
            new ImageMetadata
            {
                imageFilename =
                    filename,

                imageIndex =
                    imageIndex,

                sampleIndex =
                    sampleIndex,

                truckImageIndex =
                    sampleIndex,

                truckIndex =
                    truckIndex,

                camera =
                    captureCamera.name,

                truck =
                    currentTruck.name,

                truckPosition =
                    SerializableVector3.From(
                        ToReferencePointPosition(
                            currentTruck.transform.position
                        )
                    ),

                truckRotationEuler =
                    SerializableVector3.From(
                        ToReferencePointRotation(
                            currentTruck.transform.rotation
                        )
                    ),

                normalizedPositionX =
                    normalizedPositionX,

                normalizedPositionZ =
                    normalizedPositionZ,

                cameraPosition =
                    SerializableVector3.From(
                        ToReferencePointPosition(
                            captureCamera.transform.position
                        )
                    ),

                cameraRotationEuler =
                    SerializableVector3.From(
                        ToReferencePointRotation(
                            captureCamera.transform.rotation
                        )
                    ),

                imageWidth =
                    imageWidth,

                imageHeight =
                    imageHeight,

                generatedAt =
                    DateTime.Now.ToString(
                        "o",
                        CultureInfo.InvariantCulture
                    ),

                positionReference =
                    PositionReference,

                coordinateReferenceFrame =
                    GetCoordinateReferenceFrameDescription(),

                lighting =
                    CreateLightingMetadata(),

                axleMarkers =
                    CollectAxleMarkerMetadata(
                        currentTruck
                    ),

                licencePlateProjection = LicencePlateProjectionMetadataCapture.CaptureForTruck(
                    captureCamera,
                    metadataReferencePoint,
                    currentTruck.transform,
                    imageWidth,
                    imageHeight,
                    0.52f,
                    0.11f
                )
            };

        return metadata;
    }

    private LightingMetadata CreateLightingMetadata()
    {
        return new LightingMetadata
        {
            timeOfDay =
                lightingTimeOfDay.ToString(),

            fog =
                fogEnabled,

            rain =
                rainEnabled
        };
    }

    private Vector3 ToReferencePointPosition(
        Vector3 worldPosition)
    {
        return metadataReferencePoint
            .InverseTransformPoint(
                worldPosition
            );
    }

    private Vector3 ToReferencePointRotation(
        Quaternion worldRotation)
    {
        Quaternion relativeRotation =
            Quaternion.Inverse(
                metadataReferencePoint.rotation
            )
            * worldRotation;

        Vector3 euler =
            NormalizeEuler(
                relativeRotation.eulerAngles
            );

        // Same convention as Camera Placement tool.
        return new Vector3(
            -euler.x,
            -euler.y,
            -euler.z
        );
    }

    private Quaternion ReferencePointRotationToWorld(
        Vector3 referenceRotation)
    {
        Quaternion localRotation =
            Quaternion.Euler(
                -referenceRotation.x,
                -referenceRotation.y,
                -referenceRotation.z
            );

        return metadataReferencePoint.rotation *
               localRotation;
    }

    private string GetCoordinateReferenceFrameDescription()
    {
        return
            "Local coordinate frame of reference point '" +
            metadataReferencePoint.name +
            "'";
    }

    private string GetAbsoluteOutputDirectory()
    {
        if (Path.IsPathRooted(outputDirectory))
        {
            return outputDirectory;
        }

        return Path.GetFullPath(
            Path.Combine(
                Directory.GetCurrentDirectory(),
                outputDirectory
            )
        );
    }

    private static string SanitizePathPart(
        string value)
    {
        foreach (
            char invalidCharacter
            in Path.GetInvalidFileNameChars())
        {
            value =
                value.Replace(
                    invalidCharacter,
                    '_'
                );
        }

        return value.Replace(
            ' ',
            '_'
        );
    }

    private AxleMarkerMetadata[] CollectAxleMarkerMetadata(
        GameObject currentTruck)
    {
        Transform referencePoints =
            currentTruck.transform.Find(
                ReferencePointsContainerName
            );

        if (referencePoints == null)
        {
            return new AxleMarkerMetadata[0];
        }

        List<AxleMarkerMetadata> axleMarkers =
            new List<AxleMarkerMetadata>();

        foreach (Transform marker in referencePoints)
        {
            axleMarkers.Add(
                new AxleMarkerMetadata
                {
                    name =
                        marker.name,

                    position =
                        SerializableVector3.From(
                            ToReferencePointPosition(
                                marker.position
                            )
                        )
                }
            );
        }

        axleMarkers.Sort(
            (first, second) =>
                string.Compare(
                    first.name,
                    second.name,
                    StringComparison.Ordinal
                )
        );

        return axleMarkers.ToArray();
    }

    [Serializable]
    private class MetadataFile
    {
        public RunConfiguration run;
        public ImageMetadata[] images;
    }

    [Serializable]
    private class RunConfiguration
    {
        public string generatedAt;
        public string scene;
        public string camera;
        public string truck;
        public string[] trucks;
        public string referencePoint;

        public bool generateAllTrucks;
        public int truckCount;
        public int imageCount;
        public int imagesPerTruck;
        public int totalImageCount;
        public int imageWidth;
        public int imageHeight;

        public SerializableVector3 startPosition;
        public SerializableVector3 startRotationEuler;
        public SerializableVector3 endPosition;

        public float gaussianMeanX;
        public float gaussianStdX;
        public float gaussianMeanZ;
        public float gaussianStdZ;
        public int randomSeed;

        public string positionReference;
        public string coordinateReferenceFrame;

        public LightingMetadata lighting;
    }

    [Serializable]
    private class ImageMetadata
    {
        public string imageFilename;
        public int imageIndex;
        public int sampleIndex;
        public int truckImageIndex;
        public int truckIndex;

        public string camera;
        public string truck;

        public SerializableVector3 truckPosition;
        public SerializableVector3 truckRotationEuler;

        public float normalizedPositionX;
        public float normalizedPositionZ;

        public SerializableVector3 cameraPosition;
        public SerializableVector3 cameraRotationEuler;

        public int imageWidth;
        public int imageHeight;

        public string generatedAt;

        public string positionReference;
        public string coordinateReferenceFrame;

        public LightingMetadata lighting;

        public AxleMarkerMetadata[] axleMarkers;
        public LicencePlateProjectionMetadata licencePlateProjection;

    }

    [Serializable]
    private class LightingMetadata
    {
        public string timeOfDay;
        public bool fog;
        public bool rain;
    }

    [Serializable]
    private class AxleMarkerMetadata
    {
        public string name;
        public SerializableVector3 position;
    }

    private struct TruckSceneState
    {
        public GameObject truck;
        public Vector3 position;
        public Quaternion rotation;
        public bool activeSelf;
    }

    private enum TimeOfDay
    {
        Daytime,
        Nighttime
    }

    [Serializable]
    private struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public static SerializableVector3 From(
            Vector3 value)
        {
            return new SerializableVector3
            {
                x = value.x,
                y = value.y,
                z = value.z
            };
        }
    }

    private static Vector3 NormalizeEuler(
        Vector3 eulerAngles)
    {
        return new Vector3(
            NormalizeAngle(eulerAngles.x),
            NormalizeAngle(eulerAngles.y),
            NormalizeAngle(eulerAngles.z)
        );
    }

    private static float NormalizeAngle(
        float angle)
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