# Paint random tiles

To paint random tiles, use the random brush in the **Tile Palette** window.

## Enable the random brush

To enable the random brush, follow these steps:

1. Open the [**Tile Palette** window](https://docs.unity3d.com/Manual/tilemaps/tile-palettes/tile-palette-editor-reference.html).
2. In the Brush Inspector section at the bottom, set the brush to **Random Brush**.

## Paint random tiles

To paint random tiles, first pick the set of tiles you want the random brush to choose from, then paint onto the **Scene** view.

Follow these steps:

1. Select **Add To Random Tiles**.
2. Select the **Pick** (<span class="icon-EditorUI-Colorpicker" aria-label="Colorpicker"></span>) tool in the toolbar of the **Tile Palette** window.
3. Select individual tiles from the tile palette. Each tile becomes a new tile set, which is an option the random brush chooses from.

    You can also set **Number of Tiles** to the number of options you want, then drag tiles from the **Project** window to the numbered **Tile** fields. 

4. Select the **Paint with Active Brush** (<span class="iconClr-Animation-Brush" aria-label="Brush"></span>) tool.
5. Paint onto the **Scene** view.

**Note:** The tile sets for the random brush aren't related to the tile set asset you can use to [create a new tile palette](https://docs.unity3d.com/Manual/tilemaps/tiles-for-tilemaps/create-tile-assets.html).

## Paint groups of random tiles

To paint groups of random tiles, set the size of the group, then pick the groups of tiles you want the random brush to choose from.

Follow these steps:

1. Set **Tile Set Size** to the size you want to paint. For example, set **X** to 2 and **Y** to 2 to paint a 2 × 2 group of tiles.
2. Select **Add To Random Tiles**.
3. Select the **Pick** (<span class="icon-EditorUI-Colorpicker" aria-label="Colorpicker"></span>) tool in the toolbar of the **Tile Palette** window.
4. Click and drag to select a group of tiles from the tile palette. Each group becomes a new tile set.

    You can also set **Number of Tiles** to the number of options, then drag tiles from the **Project** window to the numbered **Tile** fields. For example, if you set **Number of Tiles** to 3, Unity creates 3 sets of 2 × 2 tiles.

5. Select the **Paint with Active Brush** (<span class="iconClr-Animation-Brush" aria-label="Brush"></span>) tool.
6. Paint onto the **Scene** view.

## Inspector window properties

| **Property** | **Description** |
|:--|:--|
| **Pick Random Tiles** | Enables creating tile sets by using the **Pick** tool in the toolbar of the **Tile Palette** window. |
| **Add To Random Tiles** | Adds a new tile set when you select a tile with the **Pick** tool, instead of replacing the existing tile sets. |
| **Tile Set Size** | Sets the size the brush paints with, in tiles. For example, if you set **X** to 2 and **Y** to 2, the brush paints a 2 × 2 group of tiles. |
| **Number of Tiles** | Sets the number of tile sets, which are the options the random brush chooses from. |

### Place random tiles

If **Number of Tiles** is greater than 0, or you create tile sets using the **Pick** tool, Unity displays the list of tile sets and tiles.

| **Property** | **Description** |
|:--|:--|
| **Number** | The number of the tile set, for example **Tile Set 1**. |
| **Tile** | The number of the tile in the tile set, for example **Tile 1**. To select a tile, drag it from the **Project** window to this field, or select the picker (**⊙**). |

## Additional resources

- [Create an animated tile](AnimatedTile.md)

