# Entity Id Tile Inspector window reference

Explore the properties and settings you can use to customize an entity tile that uses the Job system and Burst compiler to speed up rendering.

For more information, refer to [Create an entity tile](IntroductionEntityTiles.md).

| **Property** | **Description** |
|:--|:--|
| **Preview** | Displays a preview of the tile. |
| **Sprite** | Sets the sprite Unity renders. To select a sprite, drag a sprite asset from the **Project** window, or select the picker (**⊙**). |
| **Color** | Tints the sprite with the selected color. Set the color to white to render without a tint. |
| **Collider Type** | Sets the shape Unity uses to check for collisions with the tile. The options are:<ul><li>**None**: The tile doesn't collide with anything.</li><li>**Sprite**: Unity uses the shape from the [Custom Physics Shape tab](https://docs.unity3d.com/Manual/sprite/sprite-editor/custom-physics-shape-editor-reference.html) of the Sprite Editor window.</li><li>**Grid**: Unity uses the shape of the tilemap cell.</li></ul> |
| **GameObject to Instantiate** | Sets the prefab Unity adds to the tilemap at the same position as the sprite. Drag a prefab from the **Project** window to this property. You can't drag an existing instance of a GameObject from the **Hierarchy** window. |
| **Flags** | Customizes the tile. The options are: <ul><li>**None**: Disables all the options.</li><li>**Everything**: Enables all the options.</li><li>**Lock Color**: Prevents brushes or other tools changing the **Color** of the sprite.</li><li>**Lock Transform**: Prevents brushes or other tools changing the offset position, rotation, or scale of the sprite.</li><li>**Instantiate GameObject Runtime Only**: Instantiates GameObjects only when you enter Play mode or run your built application.</li><li>**Keep GameObject Runtime Only**: In Play mode, keeps a GameObject instantiated if another tile replaces it or the tile is deleted.</li><li>**Lock All**: Enables both **Lock Color** and **Lock Transform**.</li></ul> |
| **Offset** | The fixed offset position of the tile. This property is available only if you enable **Lock Transform** or **Lock All** in the **Flags** property. |
| **Rotation** | The fixed rotation of the tile. This property is available only if you enable **Lock Transform** or **Lock All** in the **Flags** property. |
| **Scale** | The fixed scale of the tile. This property is available only if you enable **Lock Transform** or **Lock All** in the **Flags** property. |

## Additional resources

- [Create an entity tile](IntroductionEntityTiles.md)
