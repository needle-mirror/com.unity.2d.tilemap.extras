# Create an entity tile

Entity tiles are alternate versions of built-in tiles that improve performance by optimizing how Unity stores and fetches tile data. Entity tiles store tile data in native memory instead of managed memory, so Unity can use the following:

- The [Burst compiler](https://docs.unity3d.com/Packages/com.unity.burst@latest) to compile faster code.
- The [Job system](https://docs.unity3d.com/Manual/job-system-overview.html) to get data for multiple tiles simultaneously across multiple CPU cores.

> [!NOTE]
> You can't mix entity tiles and normal tiles. If you do, entity tiles fall back to not using the Job system or the Burst compiler.

The following built-in tiles have entity versions:

- Tiles that Unity creates automatically when you drag a sprite into the **Tile Palette** window
- Animated tiles
- Auto tiles
- Rule tiles

## Create entity tiles by default

To create entity tiles by default when you drag a sprite into the **Tile Palette** window, follow these steps:

1. From the main menu, select **Edit** &gt; **Preferences** to open the **Preferences** window.
2. Select **2D** &gt; **Tile Palette**.
3. Set **Create Tile Method** to **DefaultEntityIdTile**.

## Create an entity tile

To create an entity version of a built-in tile, follow these steps:

1. From the main menu, select **Assets** > **Create** > **2D** > **Tiles**.
2. Select the entity version of the tile you want to create. For example, select **Isometric Rule Entity Id Tile** for the entity version of the isometric rule tile.

You can also [create a custom entity tile](CustomEntityIdTiles.md).

## Convert existing tiles to entity tiles

To convert existing tiles to entity tiles, use the **Tile Asset Converter** window. Tile palettes continue to work with the converted tiles.

1. From the main menu, select **Window** &gt; **2D** &gt; **Tile Asset Converter** to open the **Tile Asset Converter** window.
2. Select **Convert Unity Tiles**.

Unity converts the following:

- Tiles to Entity Id tiles
- Animated tiles to Animated Entity Id tiles
- Rule tiles to Rule Entity Id tiles
- Auto tiles to Auto Entity Id tiles

## Additional resources

- [Entity Id Tile Inspector window reference](EntityIdTile.md)
- [Burst compiler](https://docs.unity3d.com/Packages/com.unity.burst@latest)
- [Job system](https://docs.unity3d.com/Manual/job-system-overview.html)
- [Native memory](https://docs.unity3d.com/Manual/performance-native-memory.html)
- [2D preferences reference](https://docs.unity3d.com/Manual/preferences-2d.html)
