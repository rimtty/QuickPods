# QuickPods app icon v2 — generation prompt

- Generation mode: built-in ImageGen
- Use case: `logo-brand`
- Intended use: Windows desktop application icon and system-tray brand mark
- Design status: preferred redesign candidate

## Design rationale

Version 1 used a central dial, segmented marks, concentric construction, and a waveform. Version 2 removes all repeated marks, dots, small holes, enclosed circles, and clustered details. The new mark uses one broad, continuous headphone silhouette with a short lower-right extension that also suggests the tail of a “Q”.

## Final prompt

Preserve the deep-navy rounded-square tile, cyan color relationship, flat modern restraint, generous padding, and absence of decorative detail.

Create a single contiguous open headphone-Q glyph: a broad inverted-U headphone arch, two solid rectangular ear cushions integrated into the same continuous silhouette, and a short diagonal extension from the lower-right cushion that reads as the tail of a Q and as a quick volume-control gesture. Keep the bottom visibly open so there is no enclosed circular hole.

Use an ultra-minimal contemporary vector-like style with flat opaque shapes, thick consistent stroke weight, balanced geometry, and a silhouette that remains legible at 16 pixels. Center the symbol on a dark-navy rounded-square tile.

Do not use enclosed circles, dots, repeated shapes, small holes, segmented marks, dials, rings, sound waves, zigzag waveforms, radiating lines, perforations, seed-pod imagery, earbuds, AirPods silhouettes, Bluetooth runes, music notes, microphones, text, third-party logos, gradients, glow, 3D effects, bevels, thin strokes, glossy rendering, mockup presentation, or watermarks.

Generate outside the tile on a perfectly flat solid `#ff00ff` chroma-key background with no shadows, gradients, textures, reflections, or lighting variation.

## Output files

- `quickpods-icon-v2.png`: final 1024×1024 RGBA PNG
- `quickpods-icon-v2.ico`: Windows multi-resolution icon containing 16, 20, 24, 32, 40, 48, 64, 128, and 256 px images
- `quickpods-icon-v2-alpha-master.png`: original-resolution transparent master
- `quickpods-icon-v2-source.png`: original chroma-key generation
- `quickpods-icon-v2-size-preview.png`: small-size legibility check
