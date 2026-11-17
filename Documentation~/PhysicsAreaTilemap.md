# Add a Physics Area Tilemap component

Use the Physics Area Tilemap component to give the tiles of a tilemap 2D Physics Core shapes, so bodies collide with the tiles or detect them as triggers.

The Physics Area Tilemap component creates shapes from the tiles of the Tilemap component on the same GameObject. Each tile's shape comes from the physics shape of its sprite, or from its cell when the tile's **Collider Type** is **Grid**. Tiles with a **Collider Type** of **None** get no shapes.

The component keeps the shapes up to date. When you paint or erase tiles, the component updates only the shapes of the cells you change, so editing a few tiles of a large map is fast. When you move the tilemap, change its Grid component, or reimport a sprite it uses, the component rebuilds the shapes.

## Prerequisites

Install the [2D Physics Core](https://docs.unity3d.com/Packages/com.unity.2d.physics@latest) package, version 2.0.0 or later. The Physics Area Tilemap component is available only when this package is installed in your project.

## Add a physics area to a tilemap

To add a physics area to a tilemap:

1. Select a GameObject with a Tilemap component in the **Hierarchy** window.
2. In the **Inspector** window, select **Add Component** > **Physics 2D (Core)** > **Physics Area Tilemap**.
3. If the **Inspector** window shows that the component has no Physics Pose, select **Add Physics Pose** in the message. The Physics Pose is the body the shapes belong to. For a level that never moves, set **Body Type** to **Static**.
4. In the **Geometry** section, set **Output Type** to the kind of shapes you need. For more information, refer to [Choose the kind of shapes](#choose-the-kind-of-shapes).

The shapes appear in the **Scene** view while the GameObject is selected.

## Physics Area Tilemap component properties

The **Inspector** window shows only the properties that apply to the current settings.

The **Geometry** section is specific to the Physics Area Tilemap component, and contains the following properties. For the **Pose**, **Transformation**, **Definition**, **Callbacks**, **User Data**, and **Info** sections that all physics areas share, refer to the [Physics Area Circle component reference](https://docs.unity.com/en-us/engine/latest/manual/unity2d/2d-physics-api/create-objects/reference-area-circle).

| **Property** | **Description** |
|:--|:--|
| **Output Type** | Sets the kind of shapes to create, either **Polygons** or **Segments**. The default is **Segments**. For more information, refer to [Choose the kind of shapes](#choose-the-kind-of-shapes). |
| **Cell Clipping** | Trims each tile's shapes to its own cell. Enabled by default. For more information, refer to [Fit shapes to their cells](#fit-shapes-to-their-cells). |
| **Split Z Layers** | Creates separate shapes for each Z layer. Disabled by default. Visible only when **Output Type** is **Segments**. For more information, refer to [Join or split tiles on different Z layers](#join-or-split-tiles-on-different-z-layers). |
| **Vertex Tolerance** | Sets how close two vertices must be to count as the same point. `0` uses a default. Visible when **Output Type** is **Segments**, or when **Cell Clipping** is enabled and has an effect. For more information, refer to [Close gaps between tiles](#close-gaps-between-tiles). |

## Choose the kind of shapes

The following table compares the **Output Type** options.

| **Output Type** | **Creates** | **Use it for** |
|:--|:--|:--|
| **Polygons** | A solid shape for each tile. | Tiles that act as triggers, such as water or hazards, and maps where bodies can end up inside the tiles. |
| **Segments** | Edges along the outside of the painted tiles only, with no edges between neighboring tiles. | Ground and walls that characters slide or roll along, because bodies don't catch on the seams between tiles. |

Because **Segments** creates no shapes inside the painted tiles, the edges are hollow. A body that ends up fully inside the tiles doesn't collide with them, and a trigger made of **Segments** stops detecting a body when the body is fully inside it.

## Join or split tiles on different Z layers

By default, **Segments** joins tiles on different Z layers as if they were all on one layer, which matches the Tilemap Collider 2D component. Tiles stacked in the same cell on different Z layers count as one tile.

To keep each Z layer apart, enable **Split Z Layers**. Each Z layer then gets its own shapes, as if it were a separate tilemap, so where tiles on different layers touch, each keeps its own edges.

**Split Z Layers** affects only **Segments**. **Polygons** always creates one shape for each tile.

## Fit shapes to their cells

Tiles usually fill their cells exactly. A sprite outline that strays past the edge of its cell, even slightly, leaves a small bump or gap where it meets the next tile.

**Cell Clipping** trims each tile's shapes to the tile's own cell, which removes those bumps and makes the shapes faster to build. Disable it for tiles that are meant to overhang their cells, such as tall props that reach into the row above.

**Cell Clipping** has no effect in the following cases, and the **Inspector** window shows a message that explains why:

- The Grid component has a negative **Cell Gap**, so neighboring cells overlap.
- The Grid component has no usable cell shape.

In these cases, the shapes follow the tiles' outlines as they are.

## Close gaps between tiles

Tile outlines use floating-point numbers, so the edges of two neighboring tiles that are meant to meet exactly often miss by a tiny amount. These tiny gaps and overlaps become seams that bodies catch on or bump over as they slide across the tiles.

**Vertex Tolerance** sets how close two vertices must be for the component to treat them as the same point, in the tilemap's local units. It has the following effects:

- With **Segments**, tile edges closer than the tolerance join into one continuous edge.
- When **Cell Clipping** is enabled, a tile outline that stops within the tolerance of its cell edge snaps onto that edge, and the component removes clipped pieces smaller than the tolerance.

When **Vertex Tolerance** is `0`, the component uses a default that scales with the project's physics settings. The component uses a value above `0` as you enter it, but never below the minimum distance the physics engine supports.

If neighboring tiles leave small gaps or bumps along their seams, raise **Vertex Tolerance**. A value that's too high joins edges that are meant to stay apart.

## Keep large tilemaps fast

Follow these tips to keep large tilemaps fast to build and edit, and to keep their memory use low:

- **Use Segments for large maps with big filled areas.** On a map that's mostly filled, **Segments** uses far less memory than **Polygons** and builds several times faster. On a map with lots of small holes and islands, such as caves, the difference is much smaller.
- **Edit tiles freely during gameplay.** Painting or erasing a few tiles updates only the shapes of those cells, so it stays fast however large the map is. This suits tiles that break, get dug out, or get built during play.
- **Make big changes with the component disabled.** Updating the shapes of thousands of cells at once can take longer than building the whole map again. For a big change, such as loading a new level into the same tilemap, disable the Physics Area Tilemap component, make the changes, then enable it again.
- **Keep Cell Clipping enabled unless your tiles overhang their cells.** When **Cell Clipping** is enabled, tile edits are faster, especially with **Segments**.
- **Avoid rebuilds during gameplay.** Changing the settings of the Tilemap component, its Grid component, or the Physics Area Tilemap component, or reimporting a sprite the map uses, builds every shape again. On a large map, make these changes while loading rather than during play.

## Additional resources

- [Grid component reference](https://docs.unity3d.com/Manual/tilemaps/grid-reference.html)
- [React to collisions and triggers on tiles](PhysicsAreaTilemap-events.md)
- [Tilemap component reference](https://docs.unity3d.com/Manual/tilemaps/tilemap-reference.html)
