# Images Generator

Unity Editor tool for generating camera images of a selected truck object at
known positions.

Open it from:

```text
Tools > Images Generator
```

## Usage

1. Open the scene that contains the camera and truck.
2. Drag the camera from the Hierarchy window into the `Camera` field.
3. Drag the truck object from the Hierarchy window into the `Truck` field. 
4. Move the truck to the first position and click `Use Truck Position` next to
   `Start Position`.
5. Move the truck to the last position and click `Use Truck Position` next to
   `End Position`.
6. Set image count, resolution, and output directory.
7. Click `Generate Images`.

The truck is restored to its original transform from before image generation.

## Output

Generated files are written to:

```text
ImagesGeneratorOutput/
  <camera>_<truck>_<dd_MM_yyyy_HH_mm>/
    images/
      image_000001.png
    metadata.jsonl
    run_config.json
```

## Position Reference

The current truck position is the selected truck object's `Transform.position`.
This is the object's Unity world position/pivot, not necessarily the truck's
front plate, axle, or center.
