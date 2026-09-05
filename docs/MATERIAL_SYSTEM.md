# Material System

Materials are ScriptableObject definitions identified by immutable string keys. A definition supplies palette colors, emissive color/intensity, durability, density, value, tool requirements, construction uses, and extensible physical flags. Runtime simulation uses compact `ushort` material indices resolved through a generated catalog/LUT; gameplay and saves use keys. The extended authoring/content taxonomy is in `docs/RESOURCE_AND_SITE_CONTENT.md`.

The material cell is a universal fixed-volume physical unit. A resource cell in an asteroid is the same size as a hull cell, a loose cell, and a stored cargo cell. Its material changes its mass/value/properties, never its volume. All material grids and loose-particle packing obey exclusive occupancy; cells cannot overlap.

Material fields carry an index plus per-cell deterministic variation seed and state channels. Render shaders choose palette variation and emission from those fields. Inspection converts the sampled index back through the catalog and reports the definition; no switch statement needs to know individual material names.

The prototype content set is rock, iron, copper, ice, carbonaceous material, and rare ore. It is intentionally small and representative rather than a locked production list.
