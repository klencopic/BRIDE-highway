using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

public class ImagesGeneratorWindow : EditorWindow
{
    private const string DefaultOutputDirectory = "ImagesGeneratorOutput";
    private const string CoordinateReferenceFrame = "Camera local coordinate frame";
    private const string PositionReference = "Selected truck GameObject Transform pivot";
    private const string ReferencePointsContainerName = "ReferencePoints";
    private static readonly Vector2 WindowSize = new Vector2(560f, 360f);

    [SerializeField] private Camera captureCamera;
    [SerializeField] private GameObject truck;
    [SerializeField] private Vector3 startPosition;
    [SerializeField] private Vector3 endPosition;
    [SerializeField] private int imageCount = 10;
    [SerializeField] private int imageWidth = 1920;
    [SerializeField] private int imageHeight = 1080;
    [SerializeField] private string outputDirectory = DefaultOutputDirectory;

    private bool hasStartPosition;
    private bool hasEndPosition;

    [MenuItem("Tools/Open Images Generator")]
    public static void Open()
    {
        ImagesGeneratorWindow window = CreateInstance<ImagesGeneratorWindow>();
        window.titleContent = new GUIContent("Images Generator");
        window.minSize = WindowSize;
        window.ShowUtility();
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
        imageWidth = EditorGUILayout.IntField("Image Width", imageWidth);
        imageHeight = EditorGUILayout.IntField("Image Height", imageHeight);

        EditorGUILayout.BeginHorizontal();
        outputDirectory = EditorGUILayout.TextField("Output Directory", outputDirectory);
        if (GUILayout.Button("Choose", GUILayout.Width(70)))
        {
            ChooseOutputDirectory();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!CanGenerate()))
        {
            if (GUILayout.Button("Generate Images", GUILayout.Height(32)))
            {
                GenerateImages();
            }
        }
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

    private bool CanGenerate()
    {
        return captureCamera != null
            && truck != null
            && hasStartPosition
            && hasEndPosition
            && imageCount > 0
            && imageWidth > 0
            && imageHeight > 0
            && !string.IsNullOrWhiteSpace(outputDirectory);
    }

    private void ChooseOutputDirectory()
    {
        string selectedDirectory = EditorUtility.OpenFolderPanel(
            "Select Images Output Directory",
            GetAbsoluteOutputDirectory(),
            string.Empty);

        if (!string.IsNullOrEmpty(selectedDirectory))
        {
            outputDirectory = selectedDirectory;
        }
    }

    private void GenerateImages()
    {
        if (!CanGenerate())
        {
            EditorUtility.DisplayDialog("Invalid Configuration", "Complete all image generator settings first.", "OK");
            return;
        }

        string runTimestamp = DateTime.Now.ToString("dd_MM_yyyy_HH_mm", CultureInfo.InvariantCulture);
        string runDirectoryName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}_{1}_{2}",
            SanitizePathPart(captureCamera.name),
            SanitizePathPart(truck.name),
            runTimestamp);
        string runDirectory = Path.Combine(GetAbsoluteOutputDirectory(), runDirectoryName);
        string imagesDirectory = Path.Combine(runDirectory, "images");
        string metadataPath = Path.Combine(runDirectory, "metadata.json");

        Directory.CreateDirectory(imagesDirectory);

        Vector3 originalTruckPosition = truck.transform.position;
        Quaternion originalTruckRotation = truck.transform.rotation;
        RenderTexture originalTargetTexture = captureCamera.targetTexture;
        RenderTexture renderTexture = null;
        Texture2D image = null;

        try
        {
            WriteRunConfiguration(runDirectory, runTimestamp);

            renderTexture = new RenderTexture(imageWidth, imageHeight, 24, RenderTextureFormat.ARGB32);
            image = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);
            captureCamera.targetTexture = renderTexture;

            List<ImageMetadata> metadataImages = new List<ImageMetadata>(imageCount);
            for (int index = 0; index < imageCount; index++)
            {
                float normalizedPosition = imageCount == 1 ? 0f : index / (float)(imageCount - 1);
                truck.transform.position = Vector3.Lerp(startPosition, endPosition, normalizedPosition);

                string filename = string.Format(CultureInfo.InvariantCulture, "image_{0:D6}.png", index + 1);
                string imagePath = Path.Combine(imagesDirectory, filename);

                CaptureImage(renderTexture, image, imagePath);
                metadataImages.Add(CreateImageMetadata(index + 1, filename, normalizedPosition));

                EditorUtility.DisplayProgressBar(
                    "Generating Images",
                    string.Format(CultureInfo.InvariantCulture, "Capturing image {0} of {1}", index + 1, imageCount),
                    (index + 1) / (float)imageCount);
            }

            WriteMetadata(metadataPath, runTimestamp, metadataImages);

            Debug.Log("Generated images at: " + runDirectory);
            EditorUtility.RevealInFinder(runDirectory);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Image Generation Failed", exception.Message, "OK");
        }
        finally
        {
            truck.transform.SetPositionAndRotation(originalTruckPosition, originalTruckRotation);
            captureCamera.targetTexture = originalTargetTexture;
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

    private void CaptureImage(RenderTexture renderTexture, Texture2D image, string imagePath)
    {
        captureCamera.Render();
        RenderTexture.active = renderTexture;
        image.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
        image.Apply();
        File.WriteAllBytes(imagePath, image.EncodeToPNG());
    }

    private void WriteRunConfiguration(string runDirectory, string runTimestamp)
    {
        RunConfiguration configuration = CreateRunConfiguration(runTimestamp);

        File.WriteAllText(
            Path.Combine(runDirectory, "run_config.json"),
            JsonUtility.ToJson(configuration, true));
    }

    private void WriteMetadata(string metadataPath, string runTimestamp, List<ImageMetadata> metadataImages)
    {
        MetadataFile metadata = new MetadataFile
        {
            run = CreateRunConfiguration(runTimestamp),
            images = metadataImages.ToArray()
        };

        File.WriteAllText(metadataPath, JsonUtility.ToJson(metadata, true));
    }

    private RunConfiguration CreateRunConfiguration(string runTimestamp)
    {
        return new RunConfiguration
        {
            generatedAt = runTimestamp,
            scene = captureCamera.gameObject.scene.path,
            camera = captureCamera.name,
            truck = truck.name,
            imageCount = imageCount,
            imageWidth = imageWidth,
            imageHeight = imageHeight,
            startPosition = SerializableVector3.From(startPosition),
            endPosition = SerializableVector3.From(endPosition),
            positionReference = PositionReference,
            coordinateReferenceFrame = CoordinateReferenceFrame
        };
    }

    private ImageMetadata CreateImageMetadata(int imageIndex, string filename, float normalizedPosition)
    {
        ImageMetadata metadata = new ImageMetadata
        {
            imageFilename = filename,
            imageIndex = imageIndex,
            camera = captureCamera.name,
            truck = truck.name,
            truckPosition = SerializableVector3.From(truck.transform.position),
            truckPositionCameraFrame = SerializableVector3.From(captureCamera.transform.InverseTransformPoint(truck.transform.position)),
            truckRotationEuler = SerializableVector3.From(truck.transform.eulerAngles),
            normalizedPositionAlongRange = normalizedPosition,
            cameraPosition = SerializableVector3.From(captureCamera.transform.position),
            cameraRotationEuler = SerializableVector3.From(captureCamera.transform.eulerAngles),
            imageWidth = imageWidth,
            imageHeight = imageHeight,
            generatedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            positionReference = PositionReference,
            coordinateReferenceFrame = CoordinateReferenceFrame
        };

        return metadata;
    }

    private string GetAbsoluteOutputDirectory()
    {
        if (Path.IsPathRooted(outputDirectory))
        {
            return outputDirectory;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), outputDirectory));
    }

    private static string SanitizePathPart(string value)
    {
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter, '_');
        }

        return value.Replace(' ', '_');
    }

    private AxleMarkerMetadata[] CollectAxleMarkerMetadata()
    {
        Transform referencePoints = truck.transform.Find(ReferencePointsContainerName);
        if (referencePoints == null)
        {
            return new AxleMarkerMetadata[0];
        }

        List<AxleMarkerMetadata> axleMarkers = new List<AxleMarkerMetadata>();
        foreach (Transform marker in referencePoints)
        {
            axleMarkers.Add(new AxleMarkerMetadata
            {
                name = marker.name,
                worldPosition = SerializableVector3.From(marker.position)
            });
        }

        axleMarkers.Sort((first, second) => string.Compare(first.name, second.name, StringComparison.Ordinal));
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
        public int imageCount;
        public int imageWidth;
        public int imageHeight;
        public SerializableVector3 startPosition;
        public SerializableVector3 endPosition;
        public string positionReference;
        public string coordinateReferenceFrame;
    }

    [Serializable]
    private class ImageMetadata
    {
        public string imageFilename;
        public int imageIndex;
        public string camera;
        public string truck;
        public SerializableVector3 truckPosition;
        public SerializableVector3 truckPositionCameraFrame;
        public SerializableVector3 truckRotationEuler;
        public float normalizedPositionAlongRange;
        public SerializableVector3 cameraPosition;
        public SerializableVector3 cameraRotationEuler;
        public int imageWidth;
        public int imageHeight;
        public string generatedAt;
        public string positionReference;
        public string coordinateReferenceFrame;
    }

    [Serializable]
    private class AxleMarkerMetadata
    {
        public string name;
        public SerializableVector3 worldPosition;
    }

    [Serializable]
    private struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public static SerializableVector3 From(Vector3 value)
        {
            return new SerializableVector3
            {
                x = value.x,
                y = value.y,
                z = value.z
            };
        }
    }
}
