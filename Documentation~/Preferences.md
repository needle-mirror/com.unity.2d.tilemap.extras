# Choose which tiles the Create menu lists

The **Assets** &gt; **Create** &gt; **2D** &gt; **Tiles** menu lists both the [entity tiles](EntityTiles-landing.md) and the [other built-in tiles](Tiles.md). If your project only uses one of these, you can leave the other out to shorten the menu.

Follow these steps:

1. From the main menu, select **Edit** &gt; **Project Settings** to open the **Project Settings** window.
2. Select **2D** &gt; **Tilemap Extras**.
3. Set **Tile Creation Menu** to the tiles you want the menu to list.

Unity recompiles the scripts, and updates the menu once that finishes.

This setting is a [Scripting Define Symbol](https://docs.unity3d.com/Manual/custom-scripting-symbols.html), so it applies to the whole project rather than to one person, and everyone who opens the project gets the same menu. It is stored per build target, and carried over when you switch to a different one.

# Tilemap Extras project settings reference

| **Property** | **Description** |
|:--|:--|
| **Tile Creation Menu** | Sets which tiles the **Assets** &gt; **Create** &gt; **2D** &gt; **Tiles** menu lists. |
| &nbsp;&nbsp;&nbsp;&nbsp;Show both | Lists every tile. This is the default. |
| &nbsp;&nbsp;&nbsp;&nbsp;Show only Entity Id Tiles | Lists only the entity tiles. |
| &nbsp;&nbsp;&nbsp;&nbsp;Show only non-Entity Id Tiles | Lists only the tiles which aren't entity tiles. |

## Set the define directly

The setting adds one of the following to the **Scripting Define Symbols** of the active build target, in **Project Settings** &gt; **Player**. You can set them there instead, or from a script, which is useful if you want to apply the same setting across several projects.

| **Define** | **Effect** |
|:--|:--|
| *(neither)* | The menu lists every tile. |
| `TILEMAP_EXTRAS_HIDE_TILE_MENU` | The menu lists only the entity tiles. |
| `TILEMAP_EXTRAS_HIDE_ENTITY_ID_TILE_MENU` | The menu lists only the tiles which aren't entity tiles. |

Setting both defines leaves no tiles in the menu at all. The **Tile Creation Menu** property can't produce that combination, and shows **Show both** if it finds it.

Unity defines these symbols for every assembly it compiles, not only for the ones in this package, because scripting define symbols have no per-assembly scope. Only this package tests them, and the `TILEMAP_EXTRAS_` prefix keeps them from clashing with symbols of your own.

## Additional resources

- [Built-in tiles](Tiles.md)
- [Speed up rendering with entity tiles](EntityTiles-landing.md)
- [Convert tiles](TileAssetConverter.md)
