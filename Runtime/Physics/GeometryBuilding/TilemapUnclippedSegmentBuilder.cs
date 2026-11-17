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
    // Segments output for tiles used as they are, so an outline can overlap its neighbours. A piece survives where no other outline covers it;
    // of two coincident pieces the lower cell's survives when they face the same way, and both go when they face opposite ways.
    // End points within the tolerance are then joined, and only cells in reach of each other interact, so an edit recomputes only the cells in reach.
    internal sealed class TilemapUnclippedSegmentBuilder : IDisposable
    {
        // Build a chain segment shape for every surviving piece of every occupied cell, in one batch.
        public void Build(in TilemapBuildContext context)
        {
            var map = context.tilemap;
            var cache = context.tileCache;
            if (map == null || cache == null)
                return;

            // An own build replaces every shape, so the tables restart; draw-only builds aren't tracked.
            var isOwnBuild = context.isOwnBuild;
            if (isOwnBuild)
            {
                EnsureTracking();
                m_CellShapes.Clear();
                m_ChangedCells.Clear();
            }

            var placement = TilemapCellTransforms.CellPlacement.Sample(map);

            // Every cell is read from one snapshot of the tile grid rather than from the engine one cell at a time.
            // Nothing between taking the snapshot and the try can throw, so the finally always releases it.
            TilemapGridReader grid;
            using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.SegmentReadTiles))
                grid = TilemapGridReader.Acquire(map);

            if (isOwnBuild)
                m_BuiltCellCount = grid.occupiedCount;

            var entries = default(NativeHashMap<TilemapCellKey, int>);
            var occupied = default(NativeList<int3>);
            var raw = default(RawSegments);
            var finished = default(FinishedSegments);
            var geometry = default(NativeList<ChainSegmentGeometry>);
            var cells = default(NativeList<Vector3Int>);
            var created = default(NativeArray<PhysicsShape>);

            try
            {
                entries = new NativeHashMap<TilemapCellKey, int>(math.max(grid.occupiedCount, 1), Allocator.TempJob);
                occupied = new NativeList<int3>(math.max(grid.occupiedCount, 1), Allocator.TempJob);
                geometry = new NativeList<ChainSegmentGeometry>(Allocator.Temp);
                cells = new NativeList<Vector3Int>(Allocator.Temp);

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.UnclippedEntries))
                {
                    for (var c = 0; c < grid.chunkCount; ++c)
                    {
                        var chunk = grid.GetChunk(c);

                        for (var i = 0; i < TileChunkData.kCells; ++i)
                        {
                            if (!grid.TryGetSource(chunk[i], out var source))
                                continue;

                            var entry = grid.GetOutlineEntry(source, cache);
                            if (entry < 0)
                                continue;

                            var cell = chunk.CellPositionAt(i);
                            entries.Add(cell, entry);
                            occupied.Add(ToInt3(cell));
                        }
                    }
                }

                var reach = Reach.Compute(map, placement, cache, context.splitZLayers);

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.UnclippedEmit))
                    raw = Emit(cache, placement, reach, entries, occupied.AsArray());

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.UnclippedGhosts))
                {
                    finished = Finish(raw, reach, cache.tolerance);

                    geometry.Capacity = raw.segments.Length;
                    cells.Capacity = raw.segments.Length;

                    for (var i = 0; i < raw.segments.Length; ++i)
                        AddPlaced(finished, i, ToVector3Int(raw.cells[i]), context.relative, geometry, cells);
                }

                if (geometry.Length == 0)
                    return;

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.UnclippedCreate))
                    created = context.shapeContext.CreateShapeBatch(geometry.AsArray().AsReadOnlySpan());

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.UnclippedStamp))
                {
                    TilemapSegmentShapes.StampCells(context, created, cells.AsArray());

                    if (isOwnBuild)
                    {
                        m_CellShapes.Capacity = Math.Max(m_CellShapes.Capacity, TilemapBuildContext.CapacityWithHeadroom(created.Length));
                        for (var i = 0; i < created.Length; ++i)
                            m_CellShapes.Add(cells[i], created[i]);
                    }
                }
            }
            finally
            {
                if (created.IsCreated)
                    created.Dispose();

                if (cells.IsCreated)
                    cells.Dispose();

                if (geometry.IsCreated)
                    geometry.Dispose();

                finished.Dispose();
                raw.Dispose();

                if (occupied.IsCreated)
                    occupied.Dispose();

                if (entries.IsCreated)
                    entries.Dispose();

                grid.Dispose();
            }
        }

        // Whether a build of the component's own shapes has filled the tables, so an update has shapes to work on.
        public bool isTracking => m_CellShapes.IsCreated;

        // Whether the cells changed since the last build or update are enough of the map that rebuilding it costs less than updating a region around each of them.
        public bool isRebuildCheaper => isTracking && IsRebuildCheaper(m_ChangedCells.Count, m_BuiltCellCount);

        // Remember the cells that changed, so the next update recomputes only the cells in reach of them.
        // Does nothing before a build of the component's own shapes, which reads the whole map anyway.
        public void ApplyTileChanges(NativeArray<Tilemap.SyncTile> entries)
        {
            if (!isTracking)
                return;

            for (var i = 0; i < entries.Length; ++i)
                m_ChangedCells.Add(entries[i].position);
        }

        // Bring the shapes in step with the cells changed since the last build or update.
        // An edit reaches every cell in reach of it, and their joins and ghosts one ring further, so those are recomputed from outlines read further out still.
        // Shapes whose geometry came out the same are kept.
        public void UpdateShapes(in TilemapBuildContext context)
        {
            var map = context.tilemap;
            var cache = context.tileCache;
            if (!isTracking || map == null || cache == null || m_ChangedCells.Count == 0)
                return;

            using var updateScope = TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.UnclippedUpdate);

            var placement = TilemapCellTransforms.CellPlacement.Sample(map);

            // A changed cell can hold a tile the cache has not shaped yet, and its outline can reach further than any before it, so the reach is worked out once those are in.
            foreach (var cell in m_ChangedCells)
                cache.GetOutlineEntry(map, cell);

            var reach = Reach.Compute(map, placement, cache, context.splitZLayers);

            var shapeCells = new NativeHashSet<TilemapCellKey>(UpdateCapacity(m_ChangedCells.Count, reach.RegionCellCount(2)), Allocator.Temp);
            var rawCells = new NativeHashSet<TilemapCellKey>(UpdateCapacity(m_ChangedCells.Count, reach.RegionCellCount(3)), Allocator.Temp);
            var entryCells = new NativeHashSet<TilemapCellKey>(UpdateCapacity(m_ChangedCells.Count, reach.RegionCellCount(UpdateRings)), Allocator.Temp);
            var entries = new NativeHashMap<TilemapCellKey, int>(UpdateCapacity(m_ChangedCells.Count, reach.RegionCellCount(UpdateRings)), Allocator.TempJob);
            var emitCells = new NativeList<int3>(Allocator.TempJob);
            var raw = default(RawSegments);
            var finished = default(FinishedSegments);
            var geometry = new NativeList<ChainSegmentGeometry>(16, Allocator.Temp);
            var cells = new NativeList<Vector3Int>(16, Allocator.Temp);
            var oldShapes = new NativeList<PhysicsShape>(16, Allocator.Temp);
            var kept = new NativeList<PhysicsShape>(16, Allocator.Temp);
            var keptCells = new NativeList<Vector3Int>(16, Allocator.Temp);
            var reshaped = new NativeList<PhysicsShape>(16, Allocator.Temp);
            var reshapedGeometry = new NativeList<ChainSegmentGeometry>(16, Allocator.Temp);
            var destroyed = new NativeList<PhysicsShape>(16, Allocator.Temp);
            var createGeometry = new NativeList<ChainSegmentGeometry>(16, Allocator.Temp);
            var createCells = new NativeList<Vector3Int>(16, Allocator.Temp);

            try
            {
                reach.AddRegion(m_ChangedCells, 2, shapeCells);
                reach.AddRegion(m_ChangedCells, 3, rawCells);
                reach.AddRegion(m_ChangedCells, UpdateRings, entryCells);

                // Entries are looked up fresh each update rather than kept for the whole map.
                foreach (var cell in entryCells)
                {
                    var entry = cache.GetOutlineEntry(map, cell);
                    if (entry >= 0)
                        entries.Add(cell, entry);
                }

                foreach (var cell in rawCells)
                {
                    if (entries.ContainsKey(cell))
                        emitCells.Add(ToInt3(cell));
                }

                raw = Emit(cache, placement, reach, entries, emitCells.AsArray());
                finished = Finish(raw, reach, cache.tolerance);

                foreach (var key in shapeCells)
                {
                    var cell = key.cell;

                    geometry.Clear();
                    cells.Clear();

                    if (raw.ranges.TryGetValue(key, out var range))
                    {
                        for (var i = range.x; i < range.x + range.y; ++i)
                            AddPlaced(finished, i, cell, context.relative, geometry, cells);
                    }

                    oldShapes.Clear();
                    foreach (var shape in m_CellShapes.GetValuesForKey(key))
                        oldShapes.Add(shape);

                    m_CellShapes.Remove(key);
                    TilemapSegmentShapes.MatchShapes(cell, oldShapes, geometry, kept, keptCells, reshaped, reshapedGeometry, destroyed, createGeometry, createCells);
                }

                var shapeContext = context.shapeContext;
                shapeContext.DestroyShapes(destroyed.AsArray().AsReadOnlySpan());

                if (reshaped.Length > 0)
                    PhysicsShape.SetBatchGeometry(reshaped.AsArray().AsReadOnlySpan(), reshapedGeometry.AsArray().AsReadOnlySpan());

                for (var i = 0; i < kept.Length; ++i)
                    m_CellShapes.Add(keptCells[i], kept[i]);

                if (createGeometry.Length > 0)
                {
                    var created = shapeContext.CreateShapeBatch(createGeometry.AsArray().AsReadOnlySpan());
                    TilemapSegmentShapes.StampCells(context, created, createCells.AsArray());

                    for (var i = 0; i < created.Length; ++i)
                        m_CellShapes.Add(createCells[i], created[i]);

                    created.Dispose();
                }

                m_ChangedCells.Clear();
            }
            finally
            {
                createCells.Dispose();
                createGeometry.Dispose();
                destroyed.Dispose();
                reshapedGeometry.Dispose();
                reshaped.Dispose();
                keptCells.Dispose();
                kept.Dispose();
                oldShapes.Dispose();
                cells.Dispose();
                geometry.Dispose();
                finished.Dispose();
                raw.Dispose();
                emitCells.Dispose();
                entries.Dispose();
                entryCells.Dispose();
                rawCells.Dispose();
                shapeCells.Dispose();
            }
        }

        // Approximate memory held between builds, for the diagnostic timings.
        public string memoryReport
        {
            get
            {
                var report = new StringBuilder();
                if (!isTracking)
                    return report.ToString();

                TilemapBuildTimings.AppendBytes(report, "cellShapes", TilemapBuildTimings.HashBytes<TilemapCellKey, PhysicsShape>(m_CellShapes.Capacity));
                TilemapBuildTimings.AppendBytes(report, "changedCells", TilemapBuildTimings.HashBytes<TilemapCellKey, byte>(m_ChangedCells.Capacity));

                return report.ToString();
            }
        }

        public void Dispose()
        {
            if (m_CellShapes.IsCreated)
                m_CellShapes.Dispose();

            if (m_ChangedCells.IsCreated)
                m_ChangedCells.Dispose();
        }

        private void EnsureTracking()
        {
            if (m_CellShapes.IsCreated)
                return;

            m_CellShapes = new NativeParallelMultiHashMap<TilemapCellKey, PhysicsShape>(InitialCapacity, Allocator.Persistent);
            m_ChangedCells = new NativeHashSet<TilemapCellKey>(InitialCapacity, Allocator.Persistent);
        }

        // Work out the surviving pieces of the given cells, every cell in reach of them having its entry in the table.
        private static RawSegments Emit(TilemapTileCache cache, TilemapCellTransforms.CellPlacement placement, Reach reach, NativeHashMap<TilemapCellKey, int> entries, NativeArray<int3> cells)
        {
            var chunkCount = (cells.Length + EmitChunkSize - 1) / EmitChunkSize;
            var stream = new NativeStream(math.max(chunkCount, 1), Allocator.TempJob);
            var offsets = reach.CreateOffsets(includeOwnCell: false, Allocator.TempJob);

            try
            {
                new EmitJob
                {
                    cells = cells,
                    entries = entries,
                    vertices = cache.outlineVertices,
                    contours = cache.outlineContours,
                    outlines = cache.outlineEntries,
                    offsets = offsets,
                    placement = placement,
                    tolerance = cache.tolerance,
                    epsilon = cache.edgeEpsilon,
                    output = stream.AsWriter()
                }.Schedule(chunkCount, 1).Complete();

                return RawSegments.Read(stream, chunkCount);
            }
            finally
            {
                offsets.Dispose();
                stream.Dispose();
            }
        }

        // Join the surviving end points and find every segment's ghost vertices.
        private static FinishedSegments Finish(RawSegments raw, Reach reach, float tolerance)
        {
            var finished = new FinishedSegments(raw.segments.Length);
            if (raw.segments.Length == 0)
                return finished;

            var offsets = reach.CreateOffsets(includeOwnCell: true, Allocator.TempJob);

            try
            {
                var chunkCount = (raw.segments.Length + FinishChunkSize - 1) / FinishChunkSize;

                var joining = new JoinJob
                {
                    segments = raw.segments.AsArray(),
                    cells = raw.cells.AsArray(),
                    ranges = raw.ranges,
                    offsets = offsets,
                    tolerance = tolerance,
                    joined = finished.joined
                }.Schedule(chunkCount, 1);

                // A ghost vertex is the far end of the segment carrying on from a joined end point, so it is read from the joined segments once every join is done.
                new GhostJob
                {
                    joined = finished.joined,
                    cells = raw.cells.AsArray(),
                    ranges = raw.ranges,
                    offsets = offsets,
                    tolerance = tolerance,
                    ghosts = finished.ghosts
                }.Schedule(chunkCount, 1, joining).Complete();
            }
            finally
            {
                offsets.Dispose();
            }

            return finished;
        }

        // Place one finished segment in the body's space and add it to the lists, unless it is too short to stand as a shape.
        private static void AddPlaced(FinishedSegments finished, int index, Vector3Int cell, Matrix4x4 relative, NativeList<ChainSegmentGeometry> geometry, NativeList<Vector3Int> cells)
        {
            var joined = finished.joined[index];
            var ghosts = finished.ghosts[index];

            var placed = Place(joined, ghosts, relative);
            if (!placed.isValid)
                return;

            geometry.Add(placed);
            cells.Add(cell);
        }

        // A cell in reach of the one being worked out: where its outline is and how far it extends, in the Tilemap's local space.
        private struct Candidate
        {
            public int3 cell;
            public float2 origin;
            public float2 boundsMin;
            public float2 boundsMax;
            public int contourStart;
            public int contourCount;
        }

        // The box of cell offsets within which one cell's outline can come within the tolerance of another's, either side.
        // Worked out from the largest outline in the cache, measured in cells along the Grid's two cell axes.
        private readonly struct Reach
        {
            private Reach(int3 low, int3 high)
            {
                this.low = low;
                this.high = high;
            }

            // Split Z layers keep the reach to a cell's own layer, so a tile only ever joins tiles on its layer.
            public static Reach Compute(Tilemap map, TilemapCellTransforms.CellPlacement placement, TilemapTileCache cache, bool splitZLayers)
            {
                var bounds = map.cellBounds;
                var zSpan = splitZLayers ? 0 : math.max(bounds.size.z - 1, 0);

                var origin = ((float3)placement.CellToLocal(Vector3Int.zero)).xy;
                var axisX = ((float3)placement.CellToLocal(Vector3Int.right)).xy - origin;
                var axisY = ((float3)placement.CellToLocal(Vector3Int.up)).xy - origin;

                // Cells that do not spread out over the plane give no way to measure in cells, so only the nearest cells are taken.
                var determinant = axisX.x * axisY.y - axisX.y * axisY.x;
                if (math.abs(determinant) < 1e-12f)
                    return new Reach(new int3(-1, -1, -zSpan), new int3(1, 1, zSpan));

                // The rows of the inverse of the matrix whose columns are the cell axes, which turn an offset in the Tilemap's local space into one in cells.
                var row0 = new float2(axisY.y, -axisY.x) / determinant;
                var row1 = new float2(-axisX.y, axisX.x) / determinant;

                var min = new float2(float.MaxValue, float.MaxValue);
                var max = new float2(float.MinValue, float.MinValue);
                var outlines = cache.outlineEntries;

                for (var i = 0; i < outlines.Length; ++i)
                {
                    var outline = outlines[i];
                    if (outline.contourCount == 0)
                        continue;

                    for (var corner = 0; corner < 4; ++corner)
                    {
                        var point = new float2((corner & 1) == 0 ? outline.boundsMin.x : outline.boundsMax.x, (corner & 2) == 0 ? outline.boundsMin.y : outline.boundsMax.y);
                        var inCells = new float2(math.dot(row0, point), math.dot(row1, point));
                        min = math.min(min, inCells);
                        max = math.max(max, inCells);
                    }
                }

                if (min.x > max.x)
                    return new Reach(new int3(0, 0, -zSpan), new int3(0, 0, zSpan));

                var toleranceInCells = cache.tolerance * math.max(math.length(row0), math.length(row1));
                var lowCells = (int2)math.ceil(min - max - toleranceInCells);
                var highCells = (int2)math.floor(max - min + toleranceInCells);

                // A hexagonal layout offsets every other row or column by half a cell, which a measure along two fixed axes does not follow, so one more cell is taken either side.
                if (map.layoutGrid != null && map.layoutGrid.cellLayout == GridLayout.CellLayout.Hexagon)
                {
                    lowCells -= 1;
                    highCells += 1;
                }

                return new Reach(new int3(lowCells, -zSpan), new int3(highCells, zSpan));
            }

            // How many cells the given number of reaches out from one cell covers, that cell included.
            public int RegionCellCount(int rings)
            {
                var size = (high - low) * rings + 1;
                return math.max(size.x * size.y * size.z, 1);
            }

            // Add every cell within the given number of reaches of any of the cells.
            public void AddRegion(NativeHashSet<TilemapCellKey> cells, int rings, NativeHashSet<TilemapCellKey> region)
            {
                var from = low * rings;
                var to = high * rings;

                foreach (var key in cells)
                {
                    var cell = ToInt3(key.cell);

                    for (var z = from.z; z <= to.z; ++z)
                    {
                        for (var y = from.y; y <= to.y; ++y)
                        {
                            for (var x = from.x; x <= to.x; ++x)
                                region.Add(ToKey(cell + new int3(x, y, z)));
                        }
                    }
                }
            }

            // Every offset in the reach, with or without the cell's own.
            public NativeArray<int3> CreateOffsets(bool includeOwnCell, Allocator allocator)
            {
                var offsets = new NativeList<int3>(RegionCellCount(1), allocator);

                for (var z = low.z; z <= high.z; ++z)
                {
                    for (var y = low.y; y <= high.y; ++y)
                    {
                        for (var x = low.x; x <= high.x; ++x)
                        {
                            var offset = new int3(x, y, z);
                            if (includeOwnCell || !offset.Equals(int3.zero))
                                offsets.Add(offset);
                        }
                    }
                }

                var array = new NativeArray<int3>(offsets.Length, allocator, NativeArrayOptions.UninitializedMemory);
                array.CopyFrom(offsets.AsArray());
                offsets.Dispose();
                return array;
            }

            public readonly int3 low;
            public readonly int3 high;
        }

        // Every piece with its end points joined, and its two ghost vertices, start and end packed into one value each.
        private struct FinishedSegments : IDisposable
        {
            public FinishedSegments(int count)
            {
                joined = new NativeArray<float4>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                ghosts = new NativeArray<float4>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            }

            public void Dispose()
            {
                if (joined.IsCreated)
                    joined.Dispose();

                if (ghosts.IsCreated)
                    ghosts.Dispose();
            }

            public NativeArray<float4> joined;
            public NativeArray<float4> ghosts;
        }

        // Work out the surviving pieces of a run of cells, one chunk of them per execution, so each chunk makes its working lists once.
        [BurstCompile]
        private struct EmitJob : IJobParallelFor
        {
            public void Execute(int chunk)
            {
                var candidates = new NativeList<Candidate>(offsets.Length, Allocator.Temp);
                var events = new NativeList<float3>(32, Allocator.Temp);

                output.BeginForEachIndex(chunk);

                var end = math.min((chunk + 1) * EmitChunkSize, cells.Length);
                for (var i = chunk * EmitChunkSize; i < end; ++i)
                    EmitCell(cells[i], candidates, events);

                output.EndForEachIndex();

                events.Dispose();
                candidates.Dispose();
            }

            // Write the surviving pieces of one cell's outline.
            private void EmitCell(int3 cell, NativeList<Candidate> candidates, NativeList<float3> events)
            {
                if (!entries.TryGetValue(ToKey(cell), out var id))
                    return;

                var outline = outlines[id];
                var origin = Origin(cell);
                var reachMin = origin + outline.boundsMin - tolerance;
                var reachMax = origin + outline.boundsMax + tolerance;

                // The cells whose outlines come within the tolerance of this one, which are the only ones that can cover any of it.
                candidates.Clear();
                for (var o = 0; o < offsets.Length; ++o)
                {
                    var other = cell + offsets[o];
                    if (!entries.TryGetValue(ToKey(other), out var otherId))
                        continue;

                    var otherOutline = outlines[otherId];
                    var otherOrigin = Origin(other);
                    var otherMin = otherOrigin + otherOutline.boundsMin;
                    var otherMax = otherOrigin + otherOutline.boundsMax;

                    if (math.any(otherMax < reachMin) || math.any(otherMin > reachMax))
                        continue;

                    candidates.Add(new Candidate
                    {
                        cell = other,
                        origin = otherOrigin,
                        boundsMin = otherMin,
                        boundsMax = otherMax,
                        contourStart = otherOutline.contourStart,
                        contourCount = otherOutline.contourCount
                    });
                }

                for (var c = outline.contourStart; c < outline.contourStart + outline.contourCount; ++c)
                {
                    var contour = contours[c];
                    for (var v = 0; v < contour.y; ++v)
                    {
                        var a = origin + vertices[contour.x + v];
                        var b = origin + vertices[contour.x + (v + 1) % contour.y];

                        if (candidates.Length == 0)
                            Write(cell, a, b);
                        else
                            EmitPiece(cell, a, b, candidates, events);
                    }
                }
            }

            // Split one piece where another outline crosses it or has a vertex lying on it, then write each run of it no other outline covers.
            // A split at another outline's vertex is placed at that vertex, so the surviving run ends exactly where the other outline's surviving pieces begin.
            private void EmitPiece(int3 cell, float2 a, float2 b, NativeList<Candidate> candidates, NativeList<float3> events)
            {
                var direction = b - a;
                var lengthSquared = math.lengthsq(direction);
                if (lengthSquared <= epsilon * epsilon)
                    return;

                var toleranceSquared = tolerance * tolerance;
                var pieceMin = math.min(a, b) - tolerance;
                var pieceMax = math.max(a, b) + tolerance;

                // Every split along the piece: x is how far along it, from zero at its start to one at its end, and yz is the point.
                events.Clear();
                events.Add(new float3(0f, a));
                events.Add(new float3(1f, b));

                for (var k = 0; k < candidates.Length; ++k)
                {
                    var candidate = candidates[k];
                    if (math.any(candidate.boundsMax < pieceMin) || math.any(candidate.boundsMin > pieceMax))
                        continue;

                    for (var c = candidate.contourStart; c < candidate.contourStart + candidate.contourCount; ++c)
                    {
                        var contour = contours[c];
                        for (var v = 0; v < contour.y; ++v)
                        {
                            var start = candidate.origin + vertices[contour.x + v];
                            var end = candidate.origin + vertices[contour.x + (v + 1) % contour.y];

                            if (TryCross(a, direction, start, end, out var crossing))
                                events.Add(new float3(crossing, a + direction * crossing));

                            var along = math.dot(start - a, direction) / lengthSquared;
                            if (along > 0f && along < 1f && math.distancesq(a + direction * along, start) <= toleranceSquared)
                                events.Add(new float3(along, start));
                        }
                    }
                }

                SortByDistance(events);

                var length = math.sqrt(lengthSquared);
                var runOpen = false;
                var runStart = float2.zero;
                var runEnd = float2.zero;

                for (var i = 0; i + 1 < events.Length; ++i)
                {
                    var from = events[i];
                    var to = events[i + 1];
                    if ((to.x - from.x) * length <= epsilon)
                        continue;

                    if (!IsCovered(cell, from.yz, to.yz, direction / length, candidates))
                    {
                        if (!runOpen)
                            runStart = from.yz;

                        runOpen = true;
                        runEnd = to.yz;
                        continue;
                    }

                    if (runOpen)
                        Write(cell, runStart, runEnd);

                    runOpen = false;
                }

                if (runOpen)
                    Write(cell, runStart, runEnd);
            }

            // Whether another outline covers the stretch of a piece between two splits, which no other outline crosses.
            // Lying along another outline's edge settles it by the edge's facing: the other way means the solids meet and the stretch is covered,
            // and the same way leaves it to the lower of the two cells. Otherwise the stretch is covered when its middle is inside the other outline.
            private bool IsCovered(int3 cell, float2 from, float2 to, float2 direction, NativeList<Candidate> candidates)
            {
                var middle = (from + to) * 0.5f;

                for (var k = 0; k < candidates.Length; ++k)
                {
                    var candidate = candidates[k];
                    if (math.any(middle < candidate.boundsMin - tolerance) || math.any(middle > candidate.boundsMax + tolerance))
                        continue;

                    if (TryFindEdgeAlong(candidate, from, to, middle, direction, out var sameFacing))
                    {
                        if (!sameFacing || TilemapSegmentShapes.IsLower(candidate.cell, cell))
                            return true;

                        continue;
                    }

                    if (Contains(candidate, middle))
                        return true;
                }

                return false;
            }

            // Find an edge of the candidate's outline the stretch lies along, within the tolerance throughout and running parallel, and say whether it faces the same way.
            private bool TryFindEdgeAlong(Candidate candidate, float2 from, float2 to, float2 middle, float2 direction, out bool sameFacing)
            {
                sameFacing = false;

                for (var c = candidate.contourStart; c < candidate.contourStart + candidate.contourCount; ++c)
                {
                    var contour = contours[c];
                    for (var v = 0; v < contour.y; ++v)
                    {
                        var start = candidate.origin + vertices[contour.x + v];
                        var end = candidate.origin + vertices[contour.x + (v + 1) % contour.y];
                        var edge = end - start;
                        var edgeLength = math.length(edge);
                        if (edgeLength <= epsilon)
                            continue;

                        var edgeDirection = edge / edgeLength;
                        if (math.abs(Cross(direction, edgeDirection)) > ParallelLimit)
                            continue;

                        if (DistanceToEdge(middle, start, end) > tolerance || DistanceToEdge(from, start, end) > tolerance || DistanceToEdge(to, start, end) > tolerance)
                            continue;

                        sameFacing = math.dot(direction, edgeDirection) > 0f;
                        return true;
                    }
                }

                return false;
            }

            // Crossing-number test of a point against every contour of the candidate's outline, which counts holes as outside.
            private bool Contains(Candidate candidate, float2 point)
            {
                var inside = false;

                for (var c = candidate.contourStart; c < candidate.contourStart + candidate.contourCount; ++c)
                {
                    var contour = contours[c];
                    for (var v = 0; v < contour.y; ++v)
                    {
                        var a = candidate.origin + vertices[contour.x + v];
                        var b = candidate.origin + vertices[contour.x + (v + 1) % contour.y];

                        if ((a.y > point.y) != (b.y > point.y) && point.x < a.x + (point.y - a.y) / (b.y - a.y) * (b.x - a.x))
                            inside = !inside;
                    }
                }

                return inside;
            }

            private void Write(int3 cell, float2 start, float2 end) => output.Write(new CellSegment { cell = cell, segment = new float4(start, end) });

            private float2 Origin(int3 cell) => ((float3)placement.CellToLocal(new Vector3Int(cell.x, cell.y, cell.z))).xy;

            // How far along a piece another edge crosses it, strictly between the piece's ends and anywhere along the edge. False when they are parallel or do not cross.
            private static bool TryCross(float2 a, float2 direction, float2 start, float2 end, out float along)
            {
                along = 0f;

                var edge = end - start;
                var denominator = Cross(direction, edge);
                if (math.abs(denominator) <= 1e-12f)
                    return false;

                var offset = start - a;
                along = Cross(offset, edge) / denominator;
                var alongEdge = Cross(offset, direction) / denominator;

                return along > 0f && along < 1f && alongEdge >= 0f && alongEdge <= 1f;
            }

            // The distance from a point to the nearest point of an edge.
            private static float DistanceToEdge(float2 point, float2 start, float2 end)
            {
                var edge = end - start;
                var along = math.saturate(math.dot(point - start, edge) / math.max(math.lengthsq(edge), 1e-12f));
                return math.distance(point, start + edge * along);
            }

            private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

            // Insertion sort by distance along the piece, since a piece has only a handful of splits.
            private static void SortByDistance(NativeList<float3> events)
            {
                for (var i = 1; i < events.Length; ++i)
                {
                    var value = events[i];
                    var j = i - 1;

                    while (j >= 0 && events[j].x > value.x)
                    {
                        events[j + 1] = events[j];
                        --j;
                    }

                    events[j + 1] = value;
                }
            }

            [ReadOnly] public NativeArray<int3> cells;
            [ReadOnly] public NativeHashMap<TilemapCellKey, int> entries;
            [ReadOnly] public NativeArray<float2> vertices;
            [ReadOnly] public NativeArray<int2> contours;
            [ReadOnly] public NativeArray<TilemapTileCache.OutlineEntry> outlines;
            [ReadOnly] public NativeArray<int3> offsets;
            public TilemapCellTransforms.CellPlacement placement;
            public float tolerance;
            public float epsilon;
            public NativeStream.Writer output;
        }

        // Join each piece's end points, one chunk of pieces per execution, so each chunk makes its working list once.
        // An end point is joined to the lowest of the end points within the tolerance of it, x first then y, among the pieces of the cells in reach.
        // Every cell with an end point that close is in reach, so two end points that join are joined to the same point whichever cell asks.
        [BurstCompile]
        private struct JoinJob : IJobParallelFor
        {
            public void Execute(int chunk)
            {
                var nearby = new NativeList<int2>(offsets.Length, Allocator.Temp);
                var gatheredFor = new int3(int.MinValue, int.MinValue, int.MinValue);

                var end = math.min((chunk + 1) * FinishChunkSize, segments.Length);
                for (var i = chunk * FinishChunkSize; i < end; ++i)
                {
                    GatherNearby(cells[i], ranges, offsets, nearby, ref gatheredFor);

                    var segment = segments[i];
                    joined[i] = new float4(Join(nearby, i, segment.xy), Join(nearby, i, segment.zw));
                }

                nearby.Dispose();
            }

            // The lowest end point within the tolerance of a point, the point itself included.
            private float2 Join(NativeList<int2> nearby, int self, float2 point)
            {
                var best = point;
                var toleranceSquared = tolerance * tolerance;

                for (var r = 0; r < nearby.Length; ++r)
                {
                    var range = nearby[r];
                    for (var i = range.x; i < range.x + range.y; ++i)
                    {
                        if (i == self)
                            continue;

                        var other = segments[i];
                        if (math.distancesq(other.xy, point) <= toleranceSquared && IsBefore(other.xy, best))
                            best = other.xy;

                        if (math.distancesq(other.zw, point) <= toleranceSquared && IsBefore(other.zw, best))
                            best = other.zw;
                    }
                }

                return best;
            }

            private static bool IsBefore(float2 a, float2 b) => a.x < b.x || (a.x == b.x && a.y < b.y);

            [ReadOnly] public NativeArray<float4> segments;
            [ReadOnly] public NativeArray<int3> cells;
            [ReadOnly] public NativeHashMap<TilemapCellKey, int2> ranges;
            [ReadOnly] public NativeArray<int3> offsets;
            public float tolerance;
            [NativeDisableParallelForRestriction] public NativeArray<float4> joined;
        }

        // Find each joined piece's ghost vertices from the joined pieces, so a ghost is where its neighbour ends after joining.
        [BurstCompile]
        private struct GhostJob : IJobParallelFor
        {
            public void Execute(int chunk)
            {
                var nearby = new NativeList<int2>(offsets.Length, Allocator.Temp);
                var gatheredFor = new int3(int.MinValue, int.MinValue, int.MinValue);

                var end = math.min((chunk + 1) * FinishChunkSize, joined.Length);
                for (var i = chunk * FinishChunkSize; i < end; ++i)
                {
                    GatherNearby(cells[i], ranges, offsets, nearby, ref gatheredFor);

                    var segment = joined[i];
                    var start = segment.xy;
                    var finish = segment.zw;

                    ghosts[i] = new float4(
                        TilemapSegmentShapes.FindGhost(joined, nearby, i, start, finish, tolerance, arriving: true),
                        TilemapSegmentShapes.FindGhost(joined, nearby, i, finish, start, tolerance, arriving: false));
                }

                nearby.Dispose();
            }

            [ReadOnly] public NativeArray<float4> joined;
            [ReadOnly] public NativeArray<int3> cells;
            [ReadOnly] public NativeHashMap<TilemapCellKey, int2> ranges;
            [ReadOnly] public NativeArray<int3> offsets;
            public float tolerance;
            [NativeDisableParallelForRestriction] public NativeArray<float4> ghosts;
        }

        // The pieces of every cell in reach, gathered again only when the cell changes, since a chunk's pieces arrive cell by cell.
        private static void GatherNearby(int3 cell, NativeHashMap<TilemapCellKey, int2> ranges, NativeArray<int3> offsets, NativeList<int2> nearby, ref int3 gatheredFor)
        {
            if (cell.Equals(gatheredFor))
                return;

            nearby.Clear();
            for (var o = 0; o < offsets.Length; ++o)
            {
                if (ranges.TryGetValue(ToKey(cell + offsets[o]), out var range))
                    nearby.Add(range);
            }

            gatheredFor = cell;
        }

        #region Internal

        // How many cells the tables start out able to hold, before they have to grow.
        private const int InitialCapacity = 256;

        // How many cells and how many pieces one execution of a job works through.
        private const int EmitChunkSize = 256;
        private const int FinishChunkSize = 256;

        // How far out from a changed cell an update has to read outlines: its own reach, the joins of those cells, the pieces those read, and the outlines those are cut by.
        private const int UpdateRings = 4;

        // The largest sine of the angle between a piece and an edge for the piece to count as lying along it.
        private const float ParallelLimit = 0.05f;

        // The shapes each cell produced, and the cells changed since the last build or update.
        private NativeParallelMultiHashMap<TilemapCellKey, PhysicsShape> m_CellShapes;
        private NativeHashSet<TilemapCellKey> m_ChangedCells;

        // How many cells the last build of the component's own shapes read, which an update's batch is weighed against.
        private int m_BuiltCellCount;

        #endregion
    }
}
