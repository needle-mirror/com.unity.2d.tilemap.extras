# Speed up rendering with entity tiles

Create alternate versions of built-in tiles that improve performance by optimizing how Unity stores and fetches tile data. You can also create custom entity tiles.

| **Topic** | **Description** |
|:--|:--|
| [Create an entity tile](IntroductionEntityTiles.md) | Create an entity version of a built-in tile, such as a rule tile or an animated tile. |
| [Create a custom entity tile](CustomEntityIdTiles.md) | Create a custom entity tile by inheriting from the `EntityIdTileBase` class. |
| [Call Grid functions from Unity Jobs](GridDataInJobs.md) | Use `GridData` and `GridUtility` to call the coordinate conversion functions of a Grid component from inside a job. |
| [Entity Id Tile Inspector window reference](EntityIdTile.md) | Explore the properties and settings you can use to customize an entity tile. |

## Additional resources

- [Burst compiler](https://docs.unity3d.com/Packages/com.unity.burst@latest)
- [Job system](https://docs.unity3d.com/Manual/job-system-overview.html)
- [Native memory](https://docs.unity3d.com/Manual/performance-native-memory.html)
