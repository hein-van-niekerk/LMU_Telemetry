# Track Maps

## Adding Track Map Images

Place track map images in this folder to have them automatically appear in the dropdown selector.

### Supported Formats
- PNG (recommended for transparency)
- JPG/JPEG
- SVG (scalable vector graphics - best for crisp rendering at any zoom level)
- BMP
- GIF

### File Naming
The filename (without extension) will be used as the track name in the dropdown.

**Examples:**
- `Spa-Francorchamps.png` → appears as "Spa-Francorchamps"
- `Monza.jpg` → appears as "Monza"
- `Nurburgring-GP.png` → appears as "Nurburgring-GP"

### How It Works
1. Place image files in this folder: `Resources/TrackMaps/`
2. Build the project (images are embedded as resources)
3. Track names appear in the dropdown automatically
4. Select a track from the dropdown to load its map
5. The racing line (1.5px thickness) will render on top at 60% image opacity

### Tips for Best Results
- Use high-resolution images for clarity
- Ensure the track orientation matches your telemetry data rotation (80° for Spa)
- Images will be stretched to fill the canvas
- Background opacity is set to 60% for racing line visibility
- Use "Load Custom..." button for one-off external images without embedding
