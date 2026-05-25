# Create a custom entity tile

Create a tile that stores data in native memory, so the Jobs system and the Burst compiler can store and retrieve the tile data to improve performance.

> [!NOTE]
> You can't mix entity tiles and normal tiles. If you do, entity tiles fall back to not using the Job system or the Burst compiler.

## Prerequisites

To create a custom entity tile, you must enable `unsafe` code methods in your project. Follow these steps:

1. From the main menu, select **Edit** &gt; **Project Settings**.
2. Select the **Player** tab to open the Player settings window.
3. Enable **Allow 'unsafe' Code**.

> [!NOTE]
> Use `unsafe` methods with extreme caution to avoid memory leaks, access violations, or data corruption. The `UnsafeUtility` class is intended for scenarios where performance is critical, and the overhead of managed memory safety is prohibitive. For more information, refer to the [UnsafeUtility](https://docs.unity3d.com/ScriptReference/Unity.Collections.LowLevel.Unsafe.UnsafeUtility.html) API.

## Write a custom entity tile script

Follow these steps:

1. Create a new C# script and include the following namespaces:

    ```csharp
    using System;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Burst;
    using Unity.Mathematics;
    using AOT;
    using Unity.Tilemaps.Experimental;
    ```

2. Create a class that implements `EntityIdTileBase`.

3. Inside the class, define a struct that stores the tile data in native memory. For example:

    ```csharp
    public struct MyTileData
    {
        public EntityId spriteEntityId;
        public Color color;
        public Matrix4x4 transform;
    }
    ```

3. Create an abstract property that returns the type of the struct. For example:

    ```csharp
    public override Type structType { get => typeof(MyTileData); }
    ```
    
4. Override the `OnEnable` method to initialize the tile data. For example:

    ```csharp
    public override void OnEnable()
    {
        base.OnEnable();
        tileData = new MyTileData()
        {
            spriteEntityId = mySprite != null ? mySprite.GetEntityId() : EntityId.None,
            color = myColor,
            transform = myTransform,
        };
    }
    ```

    You must also override `OnDisable` if you allocate native memory, such as a `NativeArray`.

5. Implement the `CopyDataStruct` method. In the method, use the `CopyStructureToPtr` API to copy data from your tile data instance to a pointer for the Jobs system.

    ```csharp
    public override unsafe void CopyDataStruct(void* pointerForJobsSystem)
    {
        UnsafeUtility.CopyStructureToPtr(ref tileData, pointerForJobsSystem);
    }
    ```

6. Implement the `RefreshTileJobDelegate` delegate method that the Jobs system uses to refresh tilemap positions when you place a tile. For example:

    ```csharp
    protected override unsafe RefreshTileJobDelegate refreshTileJobDelegate => RefreshTileJob;

    // Indicate that the Burst Compiler can compile this method
    [BurstCompile]
    // Required attribute for the Jobs system
    [MonoPInvokeCallback(typeof(RefreshTileJobDelegate))]
    static unsafe void RefreshTileJob(int count, int3* position, void* data, ref TilemapRefreshStruct tilemapRefreshStruct)
    {
        // Call the RefreshTile method for each tile instance on the tilemap
        for (var i = 0; i < count; ++i) {
            tilemapRefreshStruct.RefreshTile(*(position + i));
        }
    }
    ```

    The parameters Unity passes into the method are the following:

    - `count`: The number of tilemap positions that have this tile.
    - `position`: The pointer to an array of the tilemap positions.
    - `data`: The tile data from the `CopyDataStruct` method.
    - `tilemapRefreshStruct`: A struct that contains tilemap methods.

7. Implement the `GetTileDataJobDelegate` delegate method that the Jobs system uses to get tile data. For example:

    ```csharp
    protected override unsafe GetTileDataJobDelegate getTileDataJobDelegate => GetTileDataJob;

    [BurstCompile]
    [MonoPInvokeCallback(typeof(GetTileDataJobDelegate))]
    static unsafe void GetTileDataJob(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileData* outTileData)
    {
        var myTileData = UnsafeUtility.AsRef<MyTileData>(data);

        // Get the data for each tilemap position that has the tile
        for (var i = 0; i < count; ++i) { 
            ref TileData tileData = ref *(outTileData + i);
            tileData.spriteEntityId = myTileData.spriteEntityId;
            tileData.color = myTileData.color;
            tileData.transform = myTileData.transform;
        }
    }
    ```

    The `CopyPtrToStructure` method fills the `outTileData` parameter with the tile data.

8. Implement the `GetTileAnimationDataJobDelegate` delegate method if your tile has animated properties. Otherwise implement `null`. For more information, refer to the [GetTileAnimationDataJobDelegate](xref:Unity.Tilemaps.Experimental.AnimatedEntityIdTile.getTileAnimationDataJobDelegate) API.

The recommended best practice is to also implement the following `TileBase` methods in case the entity tile falls back to not using the Jobs system:

- `RefreshTile`
- `GetTileData`
- `GetTileAnimationData`
- `StartUp`

For more information, refer to [Scriptable tiles](https://docs.unity3d.com/Manual/tilemaps/tiles-for-tilemaps/scriptable-tiles/scriptable-tiles.html) and the [`TileBase`](xref:Unity.Tilemaps.Experimental.EntityIdTileBase) API. 

## Example

For a full example, in the **Project** window, go to the `Packages/com.unity.2d.tilemap.extras/Tilemap/Runtime/Tiles/AnimatedEntityIdTile` folder, then open the `AnimatedEntityIdTile.cs` script. 

## Additional resources

- [Scriptable tiles](https://docs.unity3d.com/Manual/tilemaps/tiles-for-tilemaps/scriptable-tiles/scriptable-tiles.html)
- [Create an entity tile](IntroductionEntityTiles.md)
