# What's new in 2D Tilemap Extras package

Discover new features and performance improvements in the latest updates to 2D Tilemap Extras.

For more information, refer to the [changelog](../changelog/CHANGELOG.html).

## Version 10.0.0

### Entity tiles

You can now create entity versions of the animated, rule, and auto tiles, which use the Jobs system and the Burst compiler to speed up tilemaps. You can also create custom entity tiles in a C# script.

For more information, refer to the following:

- [Speed up rendering with entity tiles](EntityTiles-landing.md)
- [Create an entity tile](IntroductionEntityTiles.md)
- [Create a custom entity tile](CustomEntityIdTiles.md)

### Convert existing tiles to entity tiles

Use the new **Tile Asset Converter** window to convert tiles, animated tiles, rule tiles, and auto tiles to their entity versions. Tile palettes continue to work with the converted tiles. For more information, refer to [Convert tiles](TileAssetConverter.md).

### Call Grid functions from jobs

Use `GridData` and `GridUtility` to call the coordinate conversion functions of a Grid component from inside a Burst-compiled job. For more information, refer to [Call Grid functions from Unity Jobs](GridDataInJobs.md).

### Choose which tiles the Create menu lists

Use the new **Tilemap Extras** page in the **Project Settings** window to set whether the **Assets** &gt; **Create** &gt; **2D** &gt; **Tiles** menu lists the entity tiles, the other tiles, or both. For more information, refer to [Choose which tiles the Create menu lists](Preferences.md).

### Asset preview for auto tiles

Auto tile assets now display a preview of their sprites in the **Project** window, instead of the default asset icon. For more information, refer to [Create an auto tile](AutoTile.md).

## Version 7.0.0

### New Physics Shape property for auto tiles

When you [create an auto tile](AutoTile.md), you can use the **Has Physics Shape** property to set the **Tile Collider** property to **None** if the sprite has no custom physics shape. This property is available only when you set **Tile Collider** to **Sprite**. For more information, refer to [Auto Tile asset Inspector window reference](AutoTile-Inspector.md).

## Additional resources

- [Changelog](../changelog/CHANGELOG.html)
