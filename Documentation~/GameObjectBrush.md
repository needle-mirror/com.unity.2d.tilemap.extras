# Paint GameObjects as tiles

To paint GameObjects instead of tiles onto a tilemap grid, use the GameObject brush in the **Tile Palette** window.

Follow these steps:

1. Open the [**Tile Palette** window](https://docs.unity3d.com/Manual/tilemaps/tile-palettes/tile-palette-editor-reference.html).
2. In the Brush Inspector section at the bottom, set the brush to **GameObject Brush**.
3. Open the **Cells** dropdown, then open the **Element 0** dropdown.
4. Drag the GameObject you want to paint with from the **Hierarchy** window to the **Game Object** field.

    **Note:** The GameObject must be a child of the active grid.

5. Select the **Paint with Active Brush** (<span class="iconClr-Animation-Brush" aria-label="Brush"></span>) tool.
6. Paint onto the **Scene** view. Unity creates a new instance of the GameObject in the **Hierarchy** window for each cell you draw.

To add GameObjects to the top level of the **Hierarchy** window instead of making them children of the active grid, in the **Tile Palette** window, set the active tilemap to **(Paint on Scene Root)**.

## GameObject brush Inspector window properties

### Cells

The **Cells** section lists the GameObjects the brush paints with. 

Each GameObject has its own **Element** section. The number of GameObjects depends on the **Size** property.

| **Property** | **Sub-property** | **Description** |
|:--|:--|:--|
| **Number** | N/A | The number of the GameObject, for example **Element 0**. | 
| N/A | **Game Object** | Sets the GameObject the brush paints with. To select a GameObject, drag it from the **Hierarchy** window, or select the picker (**⊙**). |
| N/A | **Offset** | Sets the position the brush paints the GameObject, relative to its element position. For example, (0.5, 0.5, 0) paints the GameObject half a cell to the right and up. |
| N/A | **Scale** | Sets the scale of the GameObject. The default is (1, 1, 1), which is the size of one tilemap cell. |
| N/A | **Orientation** | Sets the rotation of the GameObject. |
| **Add** (**+**) | N/A | Adds a new **Element**. |
| **Remove** (**-**) | N/A | Clears an **Element**. Select the element, then select **Remove**. |

## Other properties

| **Property** | **Description** |
|:--|:--|
| **Size** | Sets the size of the grid of GameObjects to draw. For example, if you set **X** to 2 and **Y** to 3, Unity paints with a 2 × 3 grid of GameObjects. Each GameObject has its own **Element** field. |
| **Pivot** | Sets which GameObject in the grid Unity centers where you click to draw. |
| **Anchor** | Sets the position of the GameObjects relative to the bottom-left of the tilemap cell. The default is (0.5, 0.5, 0), which is the center of the cell. |
| **Lock Z Position** | Locks all the GameObjects you paint to the z position of the GameObject. |
| **Scene View Z Position** | Paints the GameObject at this z position. This property is available only if you disable **Lock Z Position**. |
| **Palette Z Position** | Paints the GameObject at this z position in the tile palette. This property is available only if you disable **Lock Z Position**. |

### Scene Root Grid

The properties in this section are available only if you set the active tilemap to **(Paint on Scene Root)**.

| **Property** | **Description** |
|:--|:--|
| **Cell Size** | Sets the size of the cells in the grid. |
| **Cell Gap** | Sets the gap between cells. |
| **Cell Layout** | Sets the layout of the cells. The options are: <ul><li>**Rectangle**: Lays out the cells as a rectangular grid.</li><li>**Hexagon**: Lays out the cells as a hexagonal grid.</li><li>**Isometric**: Lays out the cells as an isometric grid.</li><li>**Isometric Z as Y**: Lays out the cells as an isometric grid, where each tile has a z value that represents its 3D height.</li></ul> |
| **Cell Swizzle** | Changes the orientation of the grid. The options are: <ul><li>**XYZ**: Leaves the x, y, and z values unchanged.</li><li>**XZY**: Swaps the y and z values. This usually means the grid lies on the ground.</li><li>**YXZ**: Swaps the x and y values.</li><li>**YZX**: Shuffles all three values. This usually means the grid lies flat.</li><li>**ZXY**: Shuffles all three values. This usually means the grid faces to the right.</li><li>**ZYX**: Reverses the values.</li></ul> |

## Additional resources

- [Automating tile layouts](RuleTile-landing.md)
