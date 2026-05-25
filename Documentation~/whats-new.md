# What's new in 2D Tilemap Extras package

Discover new features and performance improvements in the latest updates to 2D Tilemap Extras.

For more information, refer to the [changelog](../changelog/CHANGELOG.html).

## Version 8

### Entity tiles

You can now create entity versions of the animated, rule, and auto tiles, which use the Jobs system and the Burst compiler to speed up tilemaps. You can also create custom entity tiles in a C# script.

For more information, refer to the following:

- [Speed up rendering with entity tiles](EntityTiles-landing.md)
- [EntityIdTileBase](xref:Unity.Tilemaps.Experimental.EntityIdTileBase)
- [EntityIdTile](xref:Unity.Tilemaps.Experimental.EntityIdTile)

### New Physics Shape property for auto tiles

When you [create an auto tile](AutoTile.md), you can use the new **Has Physics Shape** property to automatically set the **Collider Type** property to **Sprite** if the sprite has a custom physics shape. For more information, refer to [Auto Tile asset Inspector window reference](AutoTile-Inspector.md).

## Additional resources

- [Changelog](../changelog/CHANGELOG.html)
