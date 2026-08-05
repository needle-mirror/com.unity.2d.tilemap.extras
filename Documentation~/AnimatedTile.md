# Create an animated tile

To create a tile that plays an animation of different sprites, create an animated tile asset. Follow these steps:

1. In the **Project** window, right-click and select **Create** &gt; **2D** &gt; **Tiles** &gt; **Animated Tile**.
2. Select **Lock** (<span class="icon-EditorUI-Unlocked" aria-label="Unlocked"></span>) at the top of the **Inspector** window to keep the Animated Tile **Inspector** window open.
3. Select multiple sprites from the **Project** window and drag them to the **Inspector** window. Each sprite becomes a frame of animation.
4. To paint the tile into the **Scene** view, drag the tile asset onto a [tile palette](https://docs.unity3d.com/Manual/tilemaps/tile-palettes/tile-palette-editor-reference.html).

The animation plays only when you enter Play mode.

### Properties

| **Property** | **Description** |
|:--|:--|
| **Number of Animated Sprites** | Sets the number of sprites the animation uses. |
| **Sprite list** | Displays the sprites Unity uses for the animation. Each sprite is a frame of animation, and Unity plays them in order. Use the following to edit the list:<ul><li>To change the order, click and drag the handle (**=**) to the left of a sprite.</li><li>To add a sprite, select **Add** (**+**).</li><li>To remove a sprite, select a sprite then select **Remove** (**-**).</li></ul> |
| **Minimum Speed** | The minimum speed Unity sets for the animation, in frames per second. Unity randomly selects a speed between **Minimum Speed** and **Maximum Speed**. |
| **Maximum Speed** | The maximum speed Unity sets for the animation, in frames per second. |
| **Start Time** | Sets the initial time offset into the animation in seconds. |
| **Start Frame** | Sets the frame the animation starts with. |
| **Collider Type** | Sets the shape Unity uses to check for collisions with the tile. The options are:<ul><li>**None**: The tile doesn't collide with anything.</li><li>**Sprite**: Unity uses the shape from the [Custom Physics Shape tab](https://docs.unity3d.com/Manual/sprite/sprite-editor/custom-physics-shape-editor-reference.html) of the Sprite Editor window.</li><li>**Grid**: Unity uses the shape of the tilemap cell.</li></ul> |
| **Flags** | Customizes the animation. The options are: <ul><li>**Everything**: Enables all the options.</li><li>**Loop Once**: Plays the animation once then stops.</li><li>**Pause Animation**: Stops the animation.</li><li>**Update Physics**: Updates the shape Unity uses for collisions for each sprite.</li><li>**Unscaled Time**: Always plays at the speed Unity selects, and ignores the value of the `Time.timeScale` API.</li><li>**Sync Animation**: Synchronizes the animation with other identical animations, so they all play the same frame at the same time.</li></ul> |

## Additional resources

- [Create an auto tile](AutoTile.md)
