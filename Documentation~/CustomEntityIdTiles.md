# Scriptable Entity Id Tiles

To improve performance of setting scriptables Tiles onto the Tilemap, you can choose to create your custom Tile by deriving from EntityIdTileBase and implementing the required functions. By doing so, the Tilemap is able to make use of Unity Jobs and Burst to retrieve data from your custom Tile and improve performance. The Tilemap can utilize Unity Jobs and Burst only if all Tiles placed are valid Tiles derived from EntityIdTileBase. If a mixture of standard Tiles and EntityIdTiles are set together, Unity Jobs and Burst will not be utilized.

## Implementing EntityIdTileBase

The following describes the parts of EntityIdTileBase that should be implemented to create your custom scriptable Entity Id Tile. EntityIdTile will be used as an example of implementation.

To make full use of Unity Jobs and Burst, Tile data should be stored as a `struct` and no references to managed  objects should be used within the struct.

For EntityIdTile, its data is stored in `TileData`, which used `EntityId` to reference managed Unity objects:

    private TileData m_Data;

***

>    public abstract Type structType { get; }

Implement this to return the Type of the `struct` used to store data for your custom Tile.

For EntityIdTile, the Type of the `struct` is `TileData`:

    public override Type structType { get => typeof(TileData); }

***

>    public abstract unsafe void CopyDataStruct(void* outPtr);

Implement this to allow the Tilemap to copy the data contents of your Tile to be used in a Unity Job. The data is copied into `outPtr` and the size of the data must be equal to the size of `structType` declared above. 

For EntityIdTile, its data is directly copied into `outPtr`: 

    public override unsafe void CopyDataStruct(void* outPtr)
    {
        UnsafeUtility.CopyStructureToPtr(ref m_Data, outPtr);
    }

***

>    protected abstract unsafe RefreshTileJobDelegate refreshTileJobDelegate { get; }

This contains the delegate function for refreshing positions on the Tilemap when this Tile is placed on the Tilemap.

>     public unsafe delegate void RefreshTileJobDelegate(int count, int3* position, void* data, ref TilemapRefreshStruct tilemapRefreshStruct);

- int count
  - The number of positions on the Tilemap where this Tile is set
- int3* position
  - A pointer containing an array of positions on the Tilemap where this Tile is set with the size `count`
- void* data
  - A data pointer containing a copy of this Tile's data (from `CopyDataStruct`)
- TilemapRefreshStruct tilemapRefreshStruct
  - Contains data for the Tilemap where the Tile is placed and functions to refresh positions on the Tilemap

To fully utilize Unity Burst, a loop running an array of positions and a count of positions is expected in the implementation of the `RefreshTileJobDelegate`. The `BurstCompile` and `[MonoPInvokeCallback(typeof(RefreshTileJobDelegate))]` attributes should be added as well to ensure Unity Burst can compile and run the delegate properly.  

For EntityIdTile, the `RefreshTileJobDelegate` is implemented as RefreshTileJob: 

    protected override unsafe RefreshTileJobDelegate refreshTileJobDelegate => RefreshTileJob;

    [BurstCompile]
    [MonoPInvokeCallback(typeof(RefreshTileJobDelegate))]
    static unsafe void RefreshTileJob(int count, int3* position, void* data, ref TilemapRefreshStruct tilemapRefreshStruct)
    {
        for (var i = 0; i < count; ++i)
        {
            var pos = position + i;
            tilemapRefreshStruct.RefreshTile(*pos);
        }
    }

The EntityIdTile only updates itself at its current position and therefore refreshes only that position.

***

> protected abstract unsafe GetTileDataJobDelegate getTileDataJobDelegate { get; }

This contains the delegate function for getting Tile data from this Tile with its position on the Tilemap.

>     public unsafe delegate void GetTileDataJobDelegate(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileData* outTileData);

- int count
  - The number of positions on the Tilemap where this Tile is set
- int3* position
  - A pointer containing an array of positions on the Tilemap where this Tile is set with the size `count`
- void* data
  - A data pointer containing a copy of this Tile's data (from `CopyDataStruct`)
- TilemapDataStruct tilemapDataStruct
  - Contains data for the Tilemap where the Tile is placed and functions to retrieve Tile data from the Tilemap
- TileData* outTileData
  - A pointer containing an array of TileData on the Tilemap which should be filled by this delegate function with the size `count`

The `void* data` can be converted back to its original type using:

    TileData tileData = UnsafeUtility.AsRef<TileData>(data);

To fully utilize Unity Burst, a loop running an array of positions and a count of positions is expected in the implementation of the `RefreshTileJobDelegate`. The `BurstCompile` and `[MonoPInvokeCallback(typeof(RefreshTileJobDelegate))]` attributes should be added as well to ensure Unity Burst can compile and run the delegate properly.

For EntityIdTile, the `GetTileDataJobDelegate` is implemented as GetTileDataJob:

    [BurstCompile]
    [MonoPInvokeCallback(typeof(GetTileDataJobDelegate))]
    static unsafe void GetTileDataJob(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileData* outTileData)
    {
        for (var i = 0; i < count; ++i)
        {
            UnsafeUtility.CopyPtrToStructure(data, out *(outTileData + i));
        }
    }

The Tile's data is directly copied into each corresponding `outTileData` to be used by the Tilemap.

***

> protected abstract unsafe GetTileAnimationDataJobDelegate getTileAnimationDataJobDelegate { get; }

This contains the delegate function for getting Tile Animation data from this Tile with its position on the Tilemap. This can be set to `null` if this Tile has no Tile Animation data.

>     public unsafe delegate void GetTileAnimationDataJobDelegate(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileAnimationEntityIdData* outTileAnimationEntityIdData);

- int count
    - The number of positions on the Tilemap where this Tile is set
- int3* position
    - A pointer containing an array of positions on the Tilemap where this Tile is set with the size `count`
- void* data
    - A data pointer containing a copy of this Tile's data (from `CopyDataStruct`)
- TilemapDataStruct tilemapDataStruct
    - Contains data for the Tilemap where the Tile is placed and functions to retrieve Tile data from the Tilemap
- TileAnimationEntityIdData* outTileAnimationEntityIdData
    - A pointer containing an array of TileAnimationEntityIdData on the Tilemap which should be filled by this delegate function with the size `count`

To fully utilize Unity Burst, a loop running an array of positions and a count of positions is expected in the implementation of the `RefreshTileJobDelegate`. The `BurstCompile` and `[MonoPInvokeCallback(typeof(RefreshTileJobDelegate))]` attributes should be added as well to ensure Unity Burst can compile and run the delegate properly.

For EntityIdTile, the `GetTileAnimationDataJobDelegate` is implemented as returning null, as it has no Tile Animation data:

    protected override unsafe GetTileAnimationDataJobDelegate getTileAnimationDataJobDelegate => null;

For AnimatedEntityIdTile, which does have Tile Animation data, it is implemented as:

    [BurstCompile]
    [MonoPInvokeCallback(typeof(GetTileAnimationDataJobDelegate))]
    static unsafe void GetTileAnimationDataJob(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileAnimationEntityIdData* outTileAnimationEntityIdData)
    {
        AnimatedEntityIdData tileData = UnsafeUtility.AsRef<AnimatedEntityIdData>(data);
        float animationStartTime = tileData.m_AnimationStartTime;
        float animationFrameRate = tilemapDataStruct.GetTileAnimationFrameRate();
        if (0 < tileData.m_AnimationStartFrame
            && tileData.m_AnimationStartFrame <= tileData.m_AnimatedSpriteEntityIds.Length
            && tilemapDataStruct.GetTileAnimationFrameRate() > 0)
        {
            animationStartTime = (tileData.m_AnimationStartFrame - 1) / animationFrameRate;
        }

        for (var i = 0; i < count; ++i)
        {
            ref TileAnimationEntityIdData outTileAnimationData = ref *(outTileAnimationEntityIdData + i);
            if (tileData.m_AnimatedSpriteEntityIds.IsCreated)
                outTileAnimationData.animatedSpritesEntityIds = tileData.m_AnimatedSpriteEntityIds;
            outTileAnimationData.animationSpeed =
                tileData.m_Random.NextFloat(tileData.m_MinSpeed, tileData.m_MaxSpeed);
            outTileAnimationData.animationStartTime = animationStartTime;
            outTileAnimationData.flags = tileData.m_TileAnimationFlags;
        }
    }

The `void* data` is converted back to its original type and is used to fill in the `TileAnimationEntityIdData`.

Common code is placed outside of the loop, such as getting the animation start time, while per-Tile code is put inside of the loop.

***

If `OnEnable` is overrided to initialize any data for your custom Tile, you must call `base.OnEnable` to ensure your custom Tile is properly setup and registered for the Tilemap to work.

For EntityIdTile, `OnEnable` is overrided to store EntityIds for the managed objects it references, such as Sprite and GameObject:

    public override void OnEnable()
    {
        base.OnEnable();
        OnValidate();
    }

    private void OnValidate()
    {
        m_Data = new TileData()
        {
            spriteEntityId = m_Sprite != null ? m_Sprite.GetEntityId() : EntityId.None,
            color = m_Color,
            transform = m_Transform,
            gameObjectEntityId = m_InstancedGameObject  != null ? m_InstancedGameObject.GetEntityId() : EntityId.None,
            flags = m_Flags,
            colliderType = m_ColliderType,
        };
    }

Use `OnDisable` to handle any cleanup for data stored in `OnEnable` if required.  For example, if a NativeArray was allocated in `OnEnable`, it should be disposed properly in `OnDisable`.

***

The basic Scriptable Tile functions must also be implemented as `EntityIdTileBase` is derived from `TileBase`. These will be used if this custom Tile is set together with other non-EntityIdTiles such as RuleTile.

The basic Scriptable Tile functions are:

> RefreshTile
> 
> GetTileData
> 
> GetTileAnimationData
> 
> StartUp
 
Refer to the [Scriptable Tiles](https://docs.unity3d.com/Manual/Tilemap-ScriptableTiles.html) page for more information about implementing these basic Scriptable Tile functions. 
