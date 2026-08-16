# Changelog

## [0.5.1] - 2026-08-16

### Added

- Toggles that turn the applied tone adjustment and each copied material setting on and off after applying, with direct editing of their values.
- A before/after texture preview of the pending edit.
- Re-baking of the generated texture from the edited values.

### Changed

- The default output creates a duplicated material and bakes the adjustment into a new texture, so the match survives a shader fallback.
- Copied material settings are grouped by feature, and value columns are labelled.
- Windows open at a larger size, and the settings window keeps its actions visible.

### Fixed

- The applied region now covers every renderer that shares the material, so the whole hair is adjusted.
- Semi-transparent strands are no longer left at their original color when baking.

## [0.5.0] - 2026-08-16

### Added

- Hair Tone Matcher to suggest color adjustments that bring replacement hair closer to the original hair.
- Material-based tone adjustment with a choice between creating and assigning a duplicate or overwriting the destination material.
- Statistical matching and point sampling from two textures.
- Selective copying of compatible material settings from the source hair.
- Region masks for excluding areas that should keep their original color.
- Support for lilToon and Poiyomi materials.
- Batch matching for multiple renderers and material slots, with adjustments calculated separately for each target material.
- An output mode that creates a newly adjusted main texture without changing the original texture.

### Changed

- Compatible material settings are grouped by feature and can be filtered by name before copying.

## [0.4.0] - 2026-08-10

### Added

- Helper for AAO Merge Bone to manage bone merge settings from a hierarchy tree.
- Chain thinning controls to select bones for merging at a fixed interval.

## [0.3.0] - 2026-08-10

### Added

- Helper for AAO Merge PhysBone to compare source values and suggest override values from minimum, maximum, mean, median, or mode.
- Curve-aware suggestions based on the effective value of each source PhysBone.

## [0.2.0] - 2026-08-10

### Added
- Blendshape Keeper: preview the original and modified animations side by side or by switching views.
- Optional dependency support for tools that integrate with external packages.
- Helper for MA Blendshape Sync to configure outfit blendshape bindings in bulk.

### Changed
- Blendshape Keeper: choose between overwriting source animations and saving modified copies.

## [0.1.0] - 2026-08-08

### Added
- Settings window at Tools > Candy Box to enable or disable each bundled tool.
- Disabled tools are excluded from compilation entirely.
- Blendshape Keeper: raise animation keys below the current blendshape weight.
