# Pick a group of tiles automatically

To automatically pick a group of neighboring tiles from the tile palette, use the group brush in the **Tile Palette** window.

Follow these steps:

1. Open the [**Tile Palette** window](https://docs.unity3d.com/Manual/tilemaps/tile-palettes/tile-palette-editor-reference.html).
2. In the Brush Inspector section at the bottom, set the brush to **Group Brush**.
3. Select the **Paint with Active Brush** (<span class="iconClr-Animation-Brush" aria-label="Brush"></span>) tool.
4. Select a tile in the tile palette. The group brush automatically includes tiles that surround the tile, based on the position and properties of the tiles.

To change the maximum number of tiles in the group, set the **Limit** property.

To change how many empty tiles must surround a group for the brush to recognize it as a group, set the **Gap** property.

## Inspector window properties

| **Property** | **Description** |
|:--|:--|
| **Gap** | Sets how many empty tiles must surround a group for the brush to recognize it as a group. For example, if you set the x value to 1, the brush recognizes only groups of neighboring tiles that have 1 empty tile to the left and right of the group. |
| **Limit** | Sets the maximum number of tiles in a group. For example, if you set **X** to 2 and select a tile, the brush includes a maximum of 2 additional tiles on the left and 2 additional tiles on the right. |
| **Lock Z Position** | Locks all the tiles you paint to the z position of the tile you select. |
| **Scene View Z Position** | Paints the tiles at this z position. This property is available only if you disable **Lock Z Position**. |
| **Palette Z Position** | Paints any tiles from the tile palette at this z position. This property is available only if you disable **Lock Z Position**. |

## Additional resources

- [Paint a line of tiles](LineBrush.md)

