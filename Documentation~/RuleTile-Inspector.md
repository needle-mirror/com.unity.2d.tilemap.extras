# Rule Tile Inspector window reference

Explore the properties and settings you use to change which tile Unity paints based on its surrounding tiles.

For more information, refer to [Create a rule tile](RuleTile.md).

| **Property** | **Description** |
|:--|:--|
| **Default Sprite** | Sets the sprite Unity paints if the surrounding tiles don't match the rules in the **Tiling Rules** section. |
| **Default GameObject** | Sets the prefab Unity adds if the surrounding tiles don't match the rules in the **Tiling Rules** section. Drag a prefab from the **Project** window to this property. You can't drag an existing instance of a GameObject from the **Hierarchy** window. | 
| **Default Collider** | Sets the shape Unity uses to check for collisions with the tile. The options are:<ul><li>**None**: The tile doesn't collide with anything.</li><li>**Sprite**: Unity uses the shape from the [Custom Physics Shape tab](https://docs.unity3d.com/Manual/sprite/sprite-editor/custom-physics-shape-editor-reference.html) of the Sprite Editor window.</li><li>**Grid**: Unity uses the shape of the tilemap cell.</li></ul> |
| **Number of Tiling Rules** | Sets the number of rules in the **Tiling Rules** section. |
| **Extend Neighbor** | Extends the 3 × 3 rule grid when you change a rule in the grid, so you can set rules for surrounding tiles further away. | 
| **Add** (**+**) | Adds a new rule to the **Tiling Rules** section. |
| **Remove** (**-**) | Removes a rule. Select a rule section then select this button. |

## Tiling Rules

Unity displays a tiling rule section for each rule.

| **Property** | **Description** |
|:--|:--|
| **GameObject** | Sets the prefab Unity adds if the surrounding tiles match the rules in the rule grid. Drag a prefab from the **Project** window to this property. You can't drag an existing instance of a GameObject from the **Hierarchy** window. |
| **Collider** | Sets the shape Unity uses to check for collisions with the tile, if the surrounding tiles match the rules in the rule grid. The options are:<ul><li>**None**: The tile doesn't collide with anything.</li><li>**Sprite**: Unity uses the shape from the [Custom Physics Shape tab](https://docs.unity3d.com/Manual/sprite/sprite-editor/custom-physics-shape-editor-reference.html) of the Sprite Editor window.</li><li>**Grid**: Unity uses the shape of the tilemap cell.</li></ul> |
| **Output** | Sets how Unity paints the sprite if the surrounding tiles match the rules in the rule grid. The options are:<ul><li>**Single**: Paints the single sprite.</li><li>**Random**: Chooses randomly from a list of sprites.</li><li>**Animation**: Creates an animated sprite.</li></ul> |
| **Rule Grid** | Sets the rules Unity uses to check surrounding tiles. If the rules match, Unity paints the **Sprite** or adds the **GameObject** in this rule section. Otherwise Unity paints the **Default Sprite** or adds the **Default GameObject**. Click a cell to change the rule for that neighboring tile. The options are: <ul><li>Empty: Ignores the tile at this position.</li><li>Green arrow: The rule passes if the tile at this position is this rule tile.</li><li>Red cross: The rule passes if the tile at this position isn't this rule tile.</li></ul> |
| **Sprite** | Sets the sprite Unity paints if the surrounding tiles match the rules in the rule grid. |

## Random

| **Property** | **Description** |
|:--|:--|
| **Noise** | Sets how often Unity switches to a different sprite. Lower values mean Unity is more likely to give neighboring tiles the same sprites. |
| **Shuffle** | Rotates or mirrors the sprite to try to make the **Rule Grid** criteria match. The options are: <ul><li>**Fixed**: Doesn't rotate or mirror the sprite.</li><li>**Rotated**: Tries rotating the sprite by 90° repeatedly.</li><li>**Mirror X**: Tries mirroring the sprite across the x-axis.</li><li>**Mirror Y**: Tries mirroring the sprite across the y-axis.</li><li>**Mirror XY**: Tries mirroring the sprite across both axes.</li><li>**Rotated Mirror**: Tries rotating the sprite and mirroring it across both axes.</li></ul> |
| **Size** | Sets the number of sprites for Unity to choose from. Unity adds a property for each sprite in the list. To add a sprite, drag it from the **Project** window or select the picker (**⊙**). |
| **Sprite list** | Displays the sprites Unity chooses from. To add a sprite, drag it from the **Project** window or select the picker (**⊙**). |

## Animation

| **Property** | **Description** |
|:--|:--|
| **Min Speed** | The minimum speed Unity can select for the animation, in frames per second. Unity randomly selects a speed between **Minimum Speed** and **Maximum Speed**. |
| **Max Speed** | The maximum speed Unity can select for the animation, in frames per second. |
| **Size** | Sets the number of sprites Unity uses in the animation.  |
| **Sprite list** | Displays the sprites Unity uses for the animation. Each sprite is a frame of animation, and Unity plays them in order. To add a sprite, drag it from the **Project** window or select the picker (**⊙**). |

## More (⋮) menu

| **Option** | **Description** |
|:--|:--|
| **Copy All Rules** | Copies all the rules in the **Tiling Rules** section, so you can paste them into another rule tile. |
| **Create Rule Tile Template** | Saves the rules as a template asset, so you can reuse the rules for another rule tile. For example to use with a different set of sprites. For more information, refer to [`RuleTileTemplateUtility`](ScriptRef:UnityEditor.Tilemaps.RuleTileTemplateUtility). |
| **Paste Rules** | Paste the rules you copied from another rule tile. |

## Additional resources

- [Create a custom rule tile](CustomRulesForRuleTile.md)
