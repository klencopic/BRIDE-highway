using System;
using UnityEngine;

/// <summary>
/// Utility for recording licence-plate ground-truth image geometry directly
/// from Unity at capture time.
///
/// Pixel coordinates use image convention:
///   u = 0 at left
///   v = 0 at top
/// so they can be compared directly with OpenCV / YOLO coordinates.
///
/// Preferred setup:
///   - LicencePlateCenter transform at the physical plate centre.
///   - Optional explicit transforms for TopLeft/TopRight/BottomRight/BottomLeft.
///
/// If explicit corners are not supplied, the four physical corners are derived
/// from the plate centre using truckRoot.right / truckRoot.up and the supplied
/// physical plate width/height.
/// </summary>
public static class LicencePlateProjectionMetadataCapture
{
    public const float DefaultPlateWidthM = 0.52f;
    public const float DefaultPlateHeightM = 0.11f;

    /// <summary>
    /// Convenience overload for BRIDE truck prefabs. It looks under
    /// ReferencePoints for LicencePlateCenter and, if present, the four
    /// explicit corner markers:
    ///   LicencePlateTopLeft
    ///   LicencePlateTopRight
    ///   LicencePlateBottomRight
    ///   LicencePlateBottomLeft
    /// If corner markers are absent, corners are derived from truck axes.
    /// </summary>
    public static LicencePlateProjectionMetadata CaptureForTruck(
        Camera camera,
        Transform referenceFrame,
        Transform truckRoot,
        int imageWidth,
        int imageHeight,
        float plateWidthM = DefaultPlateWidthM,
        float plateHeightM = DefaultPlateHeightM)
    {
        if (truckRoot == null)
            throw new ArgumentNullException(nameof(truckRoot));

        Transform referencePoints = truckRoot.Find("ReferencePoints");
        if (referencePoints == null)
            throw new InvalidOperationException(
                $"Truck '{truckRoot.name}' has no ReferencePoints child.");

        Transform center = referencePoints.Find("LicencePlateCenter");
        if (center == null)
            throw new InvalidOperationException(
                $"Truck '{truckRoot.name}' has no ReferencePoints/LicencePlateCenter.");

        return Capture(
            camera,
            referenceFrame,
            truckRoot,
            center,
            imageWidth,
            imageHeight,
            plateWidthM,
            plateHeightM,
            referencePoints.Find("LicencePlateTopLeft"),
            referencePoints.Find("LicencePlateTopRight"),
            referencePoints.Find("LicencePlateBottomRight"),
            referencePoints.Find("LicencePlateBottomLeft"));
    }

    public static LicencePlateProjectionMetadata Capture(
        Camera camera,
        Transform referenceFrame,
        Transform truckRoot,
        Transform plateCenter,
        int imageWidth,
        int imageHeight,
        float plateWidthM = DefaultPlateWidthM,
        float plateHeightM = DefaultPlateHeightM,
        Transform topLeft = null,
        Transform topRight = null,
        Transform bottomRight = null,
        Transform bottomLeft = null)
    {
        if (camera == null)
            throw new ArgumentNullException(nameof(camera));
        if (referenceFrame == null)
            throw new ArgumentNullException(nameof(referenceFrame));
        if (truckRoot == null)
            throw new ArgumentNullException(nameof(truckRoot));
        if (plateCenter == null)
            throw new ArgumentNullException(nameof(plateCenter));
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentOutOfRangeException("Image dimensions must be positive.");
        if (plateWidthM <= 0f || plateHeightM <= 0f)
            throw new ArgumentOutOfRangeException("Plate dimensions must be positive.");

        bool hasExplicitCorners =
            topLeft != null &&
            topRight != null &&
            bottomRight != null &&
            bottomLeft != null;

        Vector3 centerWorld = plateCenter.position;
        Vector3 tlWorld;
        Vector3 trWorld;
        Vector3 brWorld;
        Vector3 blWorld;

        if (hasExplicitCorners)
        {
            tlWorld = topLeft.position;
            trWorld = topRight.position;
            brWorld = bottomRight.position;
            blWorld = bottomLeft.position;
        }
        else
        {
            // The truck root is assumed to use Unity's conventional local axes:
            // local X = lateral/right, local Y = up.
            // This is more faithful than assuming the plate always lies along world X
            // because the rectangle rotates together with the truck.
            Vector3 right = truckRoot.right.normalized;
            Vector3 up = truckRoot.up.normalized;

            float halfWidth = 0.5f * plateWidthM;
            float halfHeight = 0.5f * plateHeightM;

            tlWorld = centerWorld - right * halfWidth + up * halfHeight;
            trWorld = centerWorld + right * halfWidth + up * halfHeight;
            brWorld = centerWorld + right * halfWidth - up * halfHeight;
            blWorld = centerWorld - right * halfWidth - up * halfHeight;
        }

        LicencePlateProjectionMetadata result = new LicencePlateProjectionMetadata
        {
            cornerSource = hasExplicitCorners
                ? "explicit_corner_transforms"
                : "derived_from_center_and_truck_axes",
            pixelCoordinateConvention = "origin top-left; +u right; +v down",
            plateWidthM = plateWidthM,
            plateHeightM = plateHeightM,

            center = MakePoint(
                "center", centerWorld, camera, referenceFrame,
                imageWidth, imageHeight),
            topLeft = MakePoint(
                "top_left", tlWorld, camera, referenceFrame,
                imageWidth, imageHeight),
            topRight = MakePoint(
                "top_right", trWorld, camera, referenceFrame,
                imageWidth, imageHeight),
            bottomRight = MakePoint(
                "bottom_right", brWorld, camera, referenceFrame,
                imageWidth, imageHeight),
            bottomLeft = MakePoint(
                "bottom_left", blWorld, camera, referenceFrame,
                imageWidth, imageHeight)
        };

        LpProjectionPointMetadata[] corners =
        {
            result.topLeft,
            result.topRight,
            result.bottomRight,
            result.bottomLeft
        };

        float minU = float.PositiveInfinity;
        float maxU = float.NegativeInfinity;
        float minV = float.PositiveInfinity;
        float maxV = float.NegativeInfinity;
        bool allInFront = result.center.inFrontOfCamera;
        bool allInside = result.center.insideImage;

        foreach (LpProjectionPointMetadata p in corners)
        {
            minU = Mathf.Min(minU, p.pixel.u);
            maxU = Mathf.Max(maxU, p.pixel.u);
            minV = Mathf.Min(minV, p.pixel.v);
            maxV = Mathf.Max(maxV, p.pixel.v);
            allInFront &= p.inFrontOfCamera;
            allInside &= p.insideImage;
        }

        result.projectedAxisAlignedBBox = new LpPixelBBoxMetadata
        {
            xMin = minU,
            yMin = minV,
            xMax = maxU,
            yMax = maxV,
            width = maxU - minU,
            height = maxV - minV,
            centerU = 0.5f * (minU + maxU),
            centerV = 0.5f * (minV + maxV)
        };

        // Pure projective-geometry offset: even with a perfect box, the center
        // of an axis-aligned image bbox need not equal the projected 3D centre.
        result.bboxCenterMinusPhysicalCenterPx = new LpPixelMetadata
        {
            u = result.projectedAxisAlignedBBox.centerU - result.center.pixel.u,
            v = result.projectedAxisAlignedBBox.centerV - result.center.pixel.v
        };

        result.allPointsInFrontOfCamera = allInFront;
        result.allPointsInsideImage = allInside;

        return result;
    }

    private static LpProjectionPointMetadata MakePoint(
        string name,
        Vector3 worldPosition,
        Camera camera,
        Transform referenceFrame,
        int imageWidth,
        int imageHeight)
    {
        // WorldToViewportPoint is independent of Game-view/editor window size.
        // Unity viewport coordinates have origin at bottom-left, while YOLO/OpenCV
        // image coordinates have origin at top-left, hence the vertical flip.
        Vector3 viewport = camera.WorldToViewportPoint(worldPosition);

        float u = viewport.x * imageWidth;
        float v = (1f - viewport.y) * imageHeight;

        return new LpProjectionPointMetadata
        {
            name = name,
            worldPosition = LpVector3Metadata.From(worldPosition),
            referencePosition = LpVector3Metadata.From(
                referenceFrame.InverseTransformPoint(worldPosition)),
            pixel = new LpPixelMetadata { u = u, v = v },
            viewportDepth = viewport.z,
            inFrontOfCamera = viewport.z > 0f,
            insideImage =
                viewport.z > 0f &&
                u >= 0f && u < imageWidth &&
                v >= 0f && v < imageHeight
        };
    }
}

[Serializable]
public class LicencePlateProjectionMetadata
{
    public string cornerSource;
    public string pixelCoordinateConvention;
    public float plateWidthM;
    public float plateHeightM;

    public LpProjectionPointMetadata center;
    public LpProjectionPointMetadata topLeft;
    public LpProjectionPointMetadata topRight;
    public LpProjectionPointMetadata bottomRight;
    public LpProjectionPointMetadata bottomLeft;

    public LpPixelBBoxMetadata projectedAxisAlignedBBox;
    public LpPixelMetadata bboxCenterMinusPhysicalCenterPx;

    public bool allPointsInFrontOfCamera;
    public bool allPointsInsideImage;
}

[Serializable]
public class LpProjectionPointMetadata
{
    public string name;
    public LpVector3Metadata worldPosition;
    public LpVector3Metadata referencePosition;
    public LpPixelMetadata pixel;
    public float viewportDepth;
    public bool inFrontOfCamera;
    public bool insideImage;
}

[Serializable]
public struct LpPixelMetadata
{
    public float u;
    public float v;
}

[Serializable]
public struct LpPixelBBoxMetadata
{
    public float xMin;
    public float yMin;
    public float xMax;
    public float yMax;
    public float width;
    public float height;
    public float centerU;
    public float centerV;
}

[Serializable]
public struct LpVector3Metadata
{
    public float x;
    public float y;
    public float z;

    public static LpVector3Metadata From(Vector3 value)
    {
        return new LpVector3Metadata
        {
            x = value.x,
            y = value.y,
            z = value.z
        };
    }
}
