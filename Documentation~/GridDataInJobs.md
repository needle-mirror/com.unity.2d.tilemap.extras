# Call Grid functions from Unity Jobs

Use `GridData` and `GridUtility` to call the coordinate conversion functions of a [Grid component](https://docs.unity3d.com/Manual/tilemaps/grid-reference.html) from inside a [Burst-compiled job](https://docs.unity3d.com/Manual/job-system-overview.html).

The `Grid` and `GridLayout` components are managed types, so you can't access them from a job. `GridData` is a blittable copy of the grid's layout, cell size, cell gap, and cell swizzle. `GridUtility` is a static, Burst-compatible class that uses a `GridData` to perform the same coordinate conversions as the `Grid` component.

## Use GridData and GridUtility in a job

Follow these steps:

1. On the main thread, create a `GridData` from a `Grid` or `GridLayout` component.

    ```csharp
    var gridData = new GridData(myGrid);
    ```

2. Pass the `GridData` into your job by value. To convert between cell space and world space, also pass the `TransformHandle` from the Grid's tilemap.

    ```csharp
    [BurstCompile]
    struct MyGridJob : IJob
    {
        public GridData gridData;
        public TransformHandle transformHandle;
        public NativeArray<float3> worldPositions;

        public void Execute()
        {
            var cellPosition = new int3(2, 3, 0);
            GridUtility.CellToWorld(in gridData, ref transformHandle, in cellPosition, out var world);
            worldPositions[0] = world;
        }
    }
    ```

3. Schedule the job as normal.

Inside an entity tile job delegate, the `TilemapDataStruct` parameter exposes a `transformHandle` property you can pass to `GridUtility`. For more information, refer to [Create a custom entity tile](CustomEntityIdTiles.md).

> [!NOTE]
> A `GridData` is a snapshot. If you change the cell size, cell gap, cell layout, or cell swizzle of the Grid component on the main thread, create a new `GridData` before scheduling the next job.

## GridUtility methods

Each `GridUtility` method matches a function on the `Grid` or `GridLayout` component. For the parameters and return values of each method, refer to the [GridUtility](xref:Unity.Tilemaps.Experimental.GridUtility) API.

| **GridUtility method** | **Grid component equivalent** |
|:--|:--|
| `CellToLocal`, `CellToLocalInterpolated` | [`GridLayout.CellToLocal`](https://docs.unity3d.com/ScriptReference/GridLayout.CellToLocal.html), [`CellToLocalInterpolated`](https://docs.unity3d.com/ScriptReference/GridLayout.CellToLocalInterpolated.html) |
| `LocalToCell`, `LocalToCellInterpolated` | [`GridLayout.LocalToCell`](https://docs.unity3d.com/ScriptReference/GridLayout.LocalToCell.html), [`LocalToCellInterpolated`](https://docs.unity3d.com/ScriptReference/GridLayout.LocalToCellInterpolated.html) |
| `LocalToWorld`, `WorldToLocal` | [`GridLayout.LocalToWorld`](https://docs.unity3d.com/ScriptReference/GridLayout.LocalToWorld.html), [`WorldToLocal`](https://docs.unity3d.com/ScriptReference/GridLayout.WorldToLocal.html) |
| `CellToWorld`, `WorldToCell` | [`GridLayout.CellToWorld`](https://docs.unity3d.com/ScriptReference/GridLayout.CellToWorld.html), [`WorldToCell`](https://docs.unity3d.com/ScriptReference/GridLayout.WorldToCell.html) |
| `GetBoundsLocal` | [`GridLayout.GetBoundsLocal`](https://docs.unity3d.com/ScriptReference/GridLayout.GetBoundsLocal.html) |
| `GetCellCenterLocal` | [`Grid.GetCellCenterLocal`](https://docs.unity3d.com/ScriptReference/Grid.GetCellCenterLocal.html) |
| `GetLayoutCellCenter` | [`GridLayout.GetLayoutCellCenter`](https://docs.unity3d.com/ScriptReference/GridLayout.GetLayoutCellCenter.html) |
| `CellSwizzle`, `InverseCellSwizzle` | [`Grid.Swizzle`](https://docs.unity3d.com/ScriptReference/Grid.Swizzle.html), [`Grid.InverseSwizzle`](https://docs.unity3d.com/ScriptReference/Grid.InverseSwizzle.html) |

## Additional resources

- [GridData](xref:Unity.Tilemaps.Experimental.GridData)
- [GridUtility](xref:Unity.Tilemaps.Experimental.GridUtility)
- [Create a custom entity tile](CustomEntityIdTiles.md)
- [Grid component reference](https://docs.unity3d.com/Manual/tilemaps/grid-reference.html)
- [Job system](https://docs.unity3d.com/Manual/job-system-overview.html)
- [Burst compiler](https://docs.unity3d.com/Packages/com.unity.burst@latest)
