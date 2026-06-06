using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

public class ImagesGeneratorWindow : EditorWindow
{
    private const string DefaultOutputDirectory = "ImagesGeneratorOutput";
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

    [MenuItem("Tools/Images Generator")]
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

        Directory.CreateDirectory(imagesDirectory);

        RenderTexture originalTargetTexture = captureCamera.targetTexture;
        RenderTexture renderTexture = null;
        Texture2D image = null;

        try
        {
            renderTexture = new RenderTexture(imageWidth, imageHeight, 24, RenderTextureFormat.ARGB32);
            image = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);
            captureCamera.targetTexture = renderTexture;

            for (int index = 0; index < imageCount; index++)
            {
                float normalizedPosition = imageCount == 1 ? 0f : index / (float)(imageCount - 1);
                truck.transform.position = Vector3.Lerp(startPosition, endPosition, normalizedPosition);

                string filename = string.Format(CultureInfo.InvariantCulture, "image_{0:D6}.png", index + 1);
                string imagePath = Path.Combine(imagesDirectory, filename);

                CaptureImage(renderTexture, image, imagePath);

                EditorUtility.DisplayProgressBar(
                    "Generating Images",
                    string.Format(CultureInfo.InvariantCulture, "Capturing image {0} of {1}", index + 1, imageCount),
                    (index + 1) / (float)imageCount);
            }

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
}
