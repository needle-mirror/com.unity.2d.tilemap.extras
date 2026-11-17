using System;
using System.Text;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

using static Unity.U2D.Physics.TilemapSegmentShapes;

namespace Unity.U2D.Physics
{
    // One set of polygons per occupied cell. The tile cache's polygons are placed in the body's space once per tile, then a Burst job moves a copy to each cell.
    // An own build records each cell's shapes, so an edit replaces only those.
    internal sealed class TilemapPolygonBuilder : IDisposable
    {
        // Build a polygon shape for every polygon of every occupied cell, in one batch, so the engine validates and locks once rather than once per cell.
        public void Build(in TilemapBuildContext context)
        {
            var map = context.tilemap;
            if (map == null || context.tileCache == null)
                return;

            // An own build replaces every shape, so the tables restart; draw-only builds aren't tracked.
            var isOwnBuild = context.isOwnBuild;
            if (isOwnBuild)
            {
                EnsureTracking();
                m_CellShapes.Clear();
                m_ChangedCells.Clear();
            }

            // Every cell is read from one snapshot of the tile grid rather than from the engine one cell at a time.
            // Nothing between taking the snapshot and the try can throw, so the finally always releases it.
            TilemapGridReader grid;
            using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.PolygonReadTiles))
                grid = TilemapGridReader.Acquire(map);

            var gathered = default(GatheredCells);
            var placed = default(PlacedPolygons);

            try
            {
                gathered = new GatheredCells(grid.occupiedCount);

                if (isOwnBuild)
                    m_CellShapes.Capacity = Math.Max(m_CellShapes.Capacity, TilemapBuildContext.CapacityWithHeadroom(grid.occupiedCount));

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.PolygonGather))
                {
                    m_CellReader.Begin(map);

                    var cache = context.tileCache;
                    var clipped = context.cellClipping;

                    for (var c = 0; c < grid.chunkCount; ++c)
                    {
                        var chunk = grid.GetChunk(c);

                        for (var i = 0; i < TileChunkData.kCells; ++i)
                        {
                            if (!grid.TryGetSource(chunk[i], out var source))
                                continue;

                            var range = grid.GetPolygonRange(source, cache, clipped);
                            if (range.y == 0)
                                continue;

                            gathered.cells.Add(ToInt3(chunk.CellPositionAt(i)));
                            gathered.ranges.Add(range);
                        }
                    }
                }

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.PolygonPlace))
                    placed = Place(context, gathered);

                CreateShapes(context, placed, isOwnBuild);
            }
            finally
            {
                placed.Dispose();
                gathered.Dispose();
                grid.Dispose();
            }
        }

        // Whether a build of the component's own shapes has filled the cell table, so an update has shapes to work on.
        public bool isTracking => m_CellShapes.IsCreated;

        // Approximate memory held between builds, for the diagnostic timings.
        public string memoryReport
        {
            get
            {
                var report = new StringBuilder();

                if (m_CellShapes.IsCreated)
                    TilemapBuildTimings.AppendBytes(report, "cellShapes", TilemapBuildTimings.HashBytes<TilemapCellKey, PhysicsShape>(m_CellShapes.Capacity));

                if (m_ChangedCells.IsCreated)
                    TilemapBuildTimings.AppendBytes(report, "changedCells", TilemapBuildTimings.HashBytes<TilemapCellKey, byte>(m_ChangedCells.Capacity));

                return report.ToString();
            }
        }

        // Remember the cells that changed, so the next update replaces only their shapes.
        // Does nothing before a build of the component's own shapes, which reads the whole map anyway.
        public void ApplyTileChanges(NativeArray<Tilemap.SyncTile> entries)
        {
            if (!isTracking)
                return;

            for (var i = 0; i < entries.Length; ++i)
                m_ChangedCells.Add(entries[i].position);
        }

        // Replace the shapes of every cell that changed since the last build or update with shapes for what the cell holds now.
        // A cell that no longer holds a tile, or whose tile wants no collider, is left with no shapes.
        public void UpdateShapes(in TilemapBuildContext context)
        {
            var map = context.tilemap;
            if (!isTracking || map == null || context.tileCache == null || m_ChangedCells.Count == 0)
                return;

            var destroyed = new NativeList<PhysicsShape>(Allocator.Temp);
            var gathered = new GatheredCells(m_ChangedCells.Count);
            var placed = default(PlacedPolygons);

            try
            {
                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.PolygonGather))
                {
                    m_CellReader.Begin(map);

                    foreach (var cell in m_ChangedCells)
                    {
                        foreach (var shape in m_CellShapes.GetValuesForKey(cell))
                            destroyed.Add(shape);

                        m_CellShapes.Remove(cell);

                        if (map.HasTile(cell))
                            Gather(context, map, cell, gathered);
                    }
                }

                m_ChangedCells.Clear();

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.PolygonDestroy))
                    context.shapeContext.DestroyShapes(destroyed.AsArray().AsReadOnlySpan());

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.PolygonPlace))
                    placed = Place(context, gathered);

                CreateShapes(context, placed, record: true);
            }
            finally
            {
                placed.Dispose();
                gathered.Dispose();
                destroyed.Dispose();
            }
        }

        public void Dispose()
        {
            if (m_CellShapes.IsCreated)
                m_CellShapes.Dispose();

            if (m_ChangedCells.IsCreated)
                m_ChangedCells.Dispose();
        }

        // Read one cell's tile and find its polygons in the tile cache, adding the cell to the gathered list.
        // A cell that contributes nothing, such as one whose tile wants no collider, adds nothing.
        private void Gather(in TilemapBuildContext context, Tilemap map, Vector3Int cell, GatheredCells gathered)
        {
            if (!TilemapCellReader.TryRead(map, cell, out var content))
                return;

            var range = context.tileCache.GetPolygonRange(content, context.cellClipping);
            if (range.y == 0)
                return;

            gathered.cells.Add(ToInt3(cell));
            gathered.ranges.Add(range);
        }

        // Place every gathered cell's polygons in the body's space.
        //
        // The cache's polygons are placed in the body's space once each, by the engine, and only those that can stand as a shape are kept.
        // Each cell's copies then go to the cell's own run of the output, worked out up front so the job writes every cell's polygons in the order the cells were gathered.
        private PlacedPolygons Place(in TilemapBuildContext context, GatheredCells gathered)
        {
            var source = context.tileCache.polygons;

            var inBody = new NativeArray<PolygonGeometry>(source.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var kept = new NativeList<PolygonGeometry>(source.Length, Allocator.TempJob);
            var keptBefore = new NativeArray<int>(source.Length + 1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var runs = new NativeArray<int3>(gathered.cells.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            try
            {
                // An empty span is refused by the engine, and a map with no tiles leaves the cache without polygons.
                inBody.CopyFrom(source);
                if (inBody.Length > 0)
                    PolygonGeometry.Transform(inBody.AsSpan(), context.relative, scaleRadius: false);

                // How many kept polygons come before each cache polygon, so a cache run maps straight onto its kept polygons.
                keptBefore[0] = 0;
                for (var i = 0; i < inBody.Length; ++i)
                {
                    var isValid = inBody[i].isValid;
                    if (isValid)
                        kept.Add(inBody[i]);

                    keptBefore[i + 1] = keptBefore[i] + (isValid ? 1 : 0);
                }

                // Each cell's run: x is where its kept polygons start, y how many there are, and z where they go in the output.
                var total = 0;
                for (var c = 0; c < runs.Length; ++c)
                {
                    var range = gathered.ranges[c];
                    var start = keptBefore[range.x];
                    var count = keptBefore[range.x + range.y] - start;

                    runs[c] = new int3(start, count, total);
                    total += count;
                }

                var placed = new PlacedPolygons(total);
                if (total == 0)
                    return placed;

                // The cell's position in the Tilemap's local space becomes a move in the body's space through the linear part of the placement.
                var relative = context.relative;

                new PlaceJob
                {
                    cells = gathered.cells.AsArray(),
                    runs = runs,
                    polygons = kept.AsArray(),
                    placement = m_CellReader.placement,
                    axisX = new float2(relative.m00, relative.m10),
                    axisY = new float2(relative.m01, relative.m11),
                    axisZ = new float2(relative.m02, relative.m12),
                    outputPolygons = placed.polygons,
                    outputCells = placed.cells
                }.Schedule((runs.Length + ChunkSize - 1) / ChunkSize, 1).Complete();

                return placed;
            }
            finally
            {
                runs.Dispose();
                keptBefore.Dispose();
                kept.Dispose();
                inBody.Dispose();
            }
        }

        // Create the placed polygons in one batch, and stamp every shape with its cell, so a contact or query traces back to the tile that produced it.
        // The cells were placed alongside the geometry, so they are already in the same order as the created shapes.
        private void CreateShapes(in TilemapBuildContext context, PlacedPolygons placed, bool record)
        {
            if (!placed.polygons.IsCreated || placed.polygons.Length == 0)
                return;

            var shapeContext = context.shapeContext;

            NativeArray<PhysicsShape> created;
            using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.PolygonCreate))
                created = shapeContext.CreateShapeBatch(placed.polygons.AsReadOnlySpan());

            using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.PolygonOwnerData))
            {
                var userDatas = new NativeArray<PhysicsUserData>(created.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (var i = 0; i < created.Length; ++i)
                    userDatas[i] = new PhysicsUserData { vector3IntValue = placed.cells[i] };

                shapeContext.SetOwnerUserData(created.AsReadOnlySpan(), userDatas.AsReadOnlySpan());
                userDatas.Dispose();
            }

            if (record)
            {
                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.PolygonCellTable))
                {
                    for (var i = 0; i < created.Length; ++i)
                        m_CellShapes.Add(placed.cells[i], created[i]);
                }
            }

            created.Dispose();
        }

        private void EnsureTracking()
        {
            if (m_CellShapes.IsCreated)
                return;

            m_CellShapes = new NativeParallelMultiHashMap<TilemapCellKey, PhysicsShape>(InitialCapacity, Allocator.Persistent);
            m_ChangedCells = new NativeHashSet<TilemapCellKey>(InitialCapacity, Allocator.Persistent);
        }

        // The occupied cells a build or update read, each with its run of polygons in the tile cache.
        private struct GatheredCells : IDisposable
        {
            public GatheredCells(int capacity)
            {
                capacity = Math.Max(capacity, 1);
                cells = new NativeList<int3>(capacity, Allocator.TempJob);
                ranges = new NativeList<int2>(capacity, Allocator.TempJob);
            }

            public void Dispose()
            {
                if (cells.IsCreated)
                    cells.Dispose();

                if (ranges.IsCreated)
                    ranges.Dispose();
            }

            public NativeList<int3> cells;
            public NativeList<int2> ranges;
        }

        // Every placed polygon, with the cell it came from.
        private struct PlacedPolygons : IDisposable
        {
            public PlacedPolygons(int count)
            {
                polygons = new NativeArray<PolygonGeometry>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                cells = new NativeArray<Vector3Int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            }

            public void Dispose()
            {
                if (polygons.IsCreated)
                    polygons.Dispose();

                if (cells.IsCreated)
                    cells.Dispose();
            }

            public NativeArray<PolygonGeometry> polygons;
            public NativeArray<Vector3Int> cells;
        }

        // Move a copy of each gathered cell's polygons to the cell, one chunk of cells per execution.
        // A move changes only where the vertices and the centroid are, so the normals and the radius the engine gave each polygon are kept as they are.
        [BurstCompile]
        private struct PlaceJob : IJobParallelFor
        {
            public void Execute(int chunk)
            {
                var end = math.min((chunk + 1) * ChunkSize, cells.Length);
                for (var i = chunk * ChunkSize; i < end; ++i)
                {
                    var cell = cells[i];
                    var run = runs[i];
                    var local = (float3)placement.CellToLocal(ToVector3Int(cell));
                    Vector2 move = axisX * local.x + axisY * local.y + axisZ * local.z;

                    for (var k = 0; k < run.y; ++k)
                    {
                        var polygon = polygons[run.x + k];
                        for (var v = 0; v < polygon.count; ++v)
                            polygon.vertices[v] += move;

                        polygon.centroid += move;

                        var index = run.z + k;
                        outputPolygons[index] = polygon;
                        outputCells[index] = ToVector3Int(cell);
                    }
                }
            }

            [ReadOnly] public NativeArray<int3> cells;
            [ReadOnly] public NativeArray<int3> runs;
            [ReadOnly] public NativeArray<PolygonGeometry> polygons;
            public TilemapCellTransforms.CellPlacement placement;
            public float2 axisX;
            public float2 axisY;
            public float2 axisZ;
            [NativeDisableParallelForRestriction] public NativeArray<PolygonGeometry> outputPolygons;
            [NativeDisableParallelForRestriction] public NativeArray<Vector3Int> outputCells;
        }

        #region Internal

        // How many shapes the cell table starts out able to hold, before it has to grow.
        private const int InitialCapacity = 256;

        // How many cells one execution of the job works through.
        private const int ChunkSize = 256;

        // The shapes each cell produced, so an update can reach a changed cell's shapes. A cell can produce several.
        private NativeParallelMultiHashMap<TilemapCellKey, PhysicsShape> m_CellShapes;

        // The cells changed since the last build or update.
        private NativeHashSet<TilemapCellKey> m_ChangedCells;

        // Reads each cell's tile content with as few calls into the Tilemap as possible, taken afresh by every build and update.
        private readonly TilemapCellReader m_CellReader = new();

        #endregion
    }
}
