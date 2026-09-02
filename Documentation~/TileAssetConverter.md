# Convert tiles

To convert tiles to a different type, use the **Tile Asset Converter** window. For example, to convert between tiles and [entity tiles](EntityTiles-landing.md).

Follow these steps:

1. From the main menu, select **Window** &gt; **2D** &gt; **Tile Asset Converter** to open the **Tile Asset Converter** window.
2. Set **Find Tiles** to the type of tile you want to convert.
3. Select **Find**. Unity adds the tiles it finds to the **Tiles to Convert** list.

    You can also add tiles manually to the **Tiles to Convert** list by dragging them from the **Project** window, or by selecting the **Add** button and selecting the picker (**⊙**).

4. Set **Convert to Tile** to the type of tile to convert to.
5. Select **Convert**.
6. Save the project to finish the conversion.

# Tile Asset Converter window reference

| **Property** | **Description** |
|:--|:--|
| **Find Tiles** | Sets a tile type for Unity to search for when you select **Find**. |
| **Find** | Finds all tiles of the type specified in **Find Tiles** and adds them to the **Tiles To Convert** list. |
| **Tiles To Convert** | Sets the number of types of tile to convert. Unity lists the tiles underneath the property. To set tiles manually, drag them from the **Project** window or select the picker (**⊙**). |
| **Add** (**+**) | Adds a tile to the **Tiles To Convert** list. |
| **Remove** (**-**) | Removes the selected tile from the **Tiles To Convert** list. |
| **Convert To Tile** | Sets the type of tile to convert to. |
| **Convert** | Converts the tiles. |
| **Convert Unity Tiles** | Automatically finds and converts built-in tiles to their entity equivalent. For more information, refer to [Create an entity tile](IntroductionEntityTiles.md). |

## Additional resources

- [Speed up rendering with entity tiles](EntityTiles-landing.md)
- [Built-in tiles](Tiles.md)

