# Create a custom rule tile

Create a custom rule tile that changes what happens when a tile checks its neighbors.

## Create a custom rule tile class

Follow these steps:

1. Create an empty C# script that inherits from `UnityEngine.Tilemaps`.

2. Create a class that inherits from `RuleTile`.

3. Add `[CreateAssetMenu]` above the class to add an item in the main menu that instantiates the tile.

    For example:

    ```csharp
    using UnityEngine;
    using UnityEngine.Tilemaps;

    [CreateAssetMenu]
    public class CustomRuleTile : RuleTile {
    }
    ```

## Add a custom property

Add a custom property to the top of the `RuleTile` class. The property appears in the **Inspector** window of the rule tile.

For example:

```csharp
public class MyTile : RuleTile {
    public bool isWater;
}
```

## Create a custom matching rule

Create a new type of rule for the grid in the **Rule Tile** Inspector window.

1. Inherit `RuleTile<CustomRuleTile.Neighbor>` instead of `RuleTile`.

2. Create a subclass that inherits from `RuleTile.TilingRule`, and add a new rule number. Unity uses 0, 1, and 2 for the default rules, so custom rules start at 3. For example:

    ```csharp
    public class Neighbor : RuleTile.TilingRule.Neighbor {
        public const int tileIsNull = 3;
    }
    ```

    When you create an instance of the new rule tile, select **3** as the rule when you click the grid in the **Tiling Rules** section of the **Inspector** window. 

3. Override the `RuleMatch` method to define the new rule. `RuleMatch` runs every time Unity checks the neighbor of a tile. Use a `switch` statement to return `true` or `false` depending on a condition. For example:

    ```csharp
        public override bool RuleMatch(int neighbor, TileBase tile) {
            switch (neighbor) {
                case 3:
                    if (tile == null)
                        return true;
                    else
                        return false;
            }
            return base.RuleMatch(neighbor, tile);
        }
    ```

For a full example, from the main menu, select **Assets** &gt; **Create** &gt; **2D** &gt; **Tiles** &gt; **Custom Rule Tile Script**.

### Check for tiles from a list

To check for tiles from a list, follow these steps:

1. In your custom tile class, create a `List` of `TileBase` objects that contains the tiles you want to check for. For example:

    ```csharp
    public List<TileBase> tilesToCheckFor = new List<TileBase>();
    ```

2. When you define the rule, use `Contains` to check if the neighbor tile is in the list. For example:

    ```csharp
    public override bool RuleMatch(int neighbor, TileBase tile) {
        switch (neighbor) {
            case Neighbor.IsInList: return tilesToCheckFor.Contains(tile);
        }
        return base.RuleMatch(neighbor, tile);
    }
    ```

3. After you create an instance of the custom rule tile, add tiles to the list in the **Inspector** window.

## Additional resources

- [RuleTile](xref:UnityEngine.RuleTile) API

