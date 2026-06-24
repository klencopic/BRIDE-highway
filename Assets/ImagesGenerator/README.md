# Images Generator

Unity Editor tool for generating camera images of a selected truck object at
known positions.

Open it from:

```text
Tools > Open Images Generator
```

Camera placement is available from:

```text
Tools > Camera Placement
```

## Usage

1. Open the scene that contains the camera and truck.
2. Drag the camera from the Hierarchy window into the `Camera` field.
3. Drag the truck object from the Hierarchy window into the `Truck` field. 
4. Assign `CameraPlacementRig` as the `Metadata Reference Point` (it is found
   automatically when present).
5. Move the truck to the first position and click `Use Truck Position` next to
   `Start Position`.
6. Move the truck to the last position and click `Use Truck Position` next to
   `End Position`.
7. Set image count, resolution, and output directory.
8. Click `Generate Images`.

The truck is restored to its original transform from before image generation.

## Output

Generated files are written to:

```text
ImagesGeneratorOutput/
  <camera>_<truck>_<dd_MM_yyyy_HH_mm>/
    images/
      image_000001.png
    metadata.json
    run_config.json
```

## Position Reference

All positions and rotations in `run_config.json` and `metadata.json` are written
in the local coordinate frame of the selected `Metadata Reference Point`.
This includes movement endpoints, truck pose, camera pose, and axle ground
markers from the selected truck's `ReferencePoints` child.
`FrontAxleGroundCenter` is the primary truck reference point when present.

## Camera Placement

The camera placement tool uses two highway-parallel reference objects,
`colmesh_ground`, and `colmesh_walls` to calculate camera distance from the
inner highway edge, camera height above ground, and camera rotation relative to
the highway direction. Initialize the reference point at the inner highway
edge, then move its red Scene-view marker or edit its world position. The tool
automatically recalculates the camera's signed local X, Y, and Z relative to the
moved reference point. Edit those values and click `Apply Camera Values` to
move the selected camera. To aim at a scene object, assign its transform as the
`Look At Target` and click `Point Camera At Target`; the resulting relative
rotation is written back into the camera values.
