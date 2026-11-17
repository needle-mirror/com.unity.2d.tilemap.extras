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
    // Segments output for tiles clipped to their cells. Two cells meet only along a shared edge, so each cell's answer depends on it and its neighbours:
    // pieces through the cell always survive, and pieces along an edge survive only where the neighbour has no solid there.
    // Points along a shared edge within the tolerance are joined first, then each segment takes its ghost vertices from the segments meeting its ends.
    // Runs in Burst jobs, and an edit recomputes only the cells around it.
    internal sealed class TilemapClippedSegmentBuilder : IDisposable
    {
        // Build a chain segment shape for every surviving piece of every occupied cell, in one batch.
        public void Build(in TilemapBuildContext context)
        {
            var map = context.tilemap;
            var cache = context.tileCache;
            if (map == null || cache == null || cache.cellShape == null)
                return;

            if (m_Neighbours == null || !m_Neighbours.IsFor(map, cache.cellShape))
                m_Neighbours = new TilemapCellNeighbours(map, cache.cellShape);

            m_Placement = TilemapCellTransforms.CellPlacement.Sample(map);

            var joinLayers = JoinsLayers(map, context.splitZLayers);

            // An own build replaces every shape, so the tables restart; draw-only builds aren't tracked.
            var isOwnBuild = context.isOwnBuild;
            if (isOwnBuild)
            {
                EnsureTracking();
                m_CellShapes.Clear();
                m_ChangedCells.Clear();
                m_ColumnCells.Clear();
                m_JoinLayers = joinLayers;
            }

            // Every cell is read from one snapshot of the tile grid rather than from the engine one cell at a time.
            // Nothing between taking the snapshot and the try can throw, so the finally always releases it.
            TilemapGridReader grid;
            using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.SegmentReadTiles))
                grid = TilemapGridReader.Acquire(map);

            if (isOwnBuild)
                m_BuiltCellCount = grid.occupiedCount;

            var entries = default(NativeHashMap<TilemapCellKey, int>);
            var occupied = default(NativeList<int3>);
            var layout = default(Layout);
            var raw = default(RawSegments);
            var ghosts = default(NativeArray<float4>);
            var geometry = default(NativeList<ChainSegmentGeometry>);
            var cells = default(NativeList<Vector3Int>);
            var created = default(NativeArray<PhysicsShape>);

            // A draw-only build must not disturb the cells the own build's shapes report, so it notes them in a table of its own.
            var columnCells = isOwnBuild ? m_ColumnCells : new NativeHashMap<TilemapCellKey, Vector3Int>(16, Allocator.Temp);
            var stacked = new NativeHashSet<TilemapCellKey>(16, Allocator.Temp);

            try
            {
                entries = new NativeHashMap<TilemapCellKey, int>(math.max(grid.occupiedCount, 1), Allocator.TempJob);
                occupied = new NativeList<int3>(math.max(grid.occupiedCount, 1), Allocator.TempJob);
                geometry = new NativeList<ChainSegmentGeometry>(Allocator.Temp);
                cells = new NativeList<Vector3Int>(Allocator.Temp);

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ClippedEntries))
                {
                    entries.Capacity = Math.Max(entries.Capacity, TilemapBuildContext.CapacityWithHeadroom(grid.occupiedCount));

                    for (var c = 0; c < grid.chunkCount; ++c)
                    {
                        var chunk = grid.GetChunk(c);

                        for (var i = 0; i < TileChunkData.kCells; ++i)
                        {
                            if (!grid.TryGetSource(chunk[i], out var source))
                                continue;

                            var entry = grid.GetSegmentEntry(source, cache);
                            if (entry < 0)
                                continue;

                            var cell = chunk.CellPositionAt(i);
                            if (!joinLayers)
                            {
                                entries[cell] = entry;
                                occupied.Add(ToInt3(cell));
                                continue;
                            }

                            // Joined layers key every table by the cell's column, so tiles on different layers meet as neighbors do on one layer.
                            var column = ToColumn(cell);
                            var first = entries.TryAdd(column, entry);
                            if (first)
                                occupied.Add(ToInt3(column));
                            else
                                stacked.Add(column);

                            NoteColumnCell(columnCells, column, cell, first);
                        }
                    }

                    // A column holding several tiles takes the union of their outlines, read once the whole grid has been seen.
                    foreach (var column in stacked)
                    {
                        var entry = ReadColumnEntry(map, cache, column, out _);
                        if (entry >= 0)
                            entries[column] = entry;
                        else
                            entries.Remove(column);
                    }
                }

                layout = new Layout(cache, m_Neighbours);

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ClippedEmit))
                    raw = Emit(cache, layout, m_Placement, entries, occupied.AsArray());

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ClippedGhosts))
                {
                    ghosts = FindGhosts(raw, layout, cache.tolerance);

                    geometry.Capacity = raw.segments.Length;
                    cells.Capacity = raw.segments.Length;

                    for (var i = 0; i < raw.segments.Length; ++i)
                        AddPlaced(raw.segments[i], ghosts[i], ToVector3Int(raw.cells[i]), context.relative, geometry, cells);
                }

                if (geometry.Length == 0)
                    return;

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ClippedCreate))
                    created = context.shapeContext.CreateShapeBatch(geometry.AsArray().AsReadOnlySpan());

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ClippedStamp))
                {
                    StampColumns(context, created, cells.AsArray(), columnCells);

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

                if (ghosts.IsCreated)
                    ghosts.Dispose();

                raw.Dispose();
                layout.Dispose();

                if (occupied.IsCreated)
                    occupied.Dispose();

                if (entries.IsCreated)
                    entries.Dispose();

                stacked.Dispose();

                if (!isOwnBuild)
                    columnCells.Dispose();

                grid.Dispose();
            }
        }

        // Whether a build of the component's own shapes has filled the tables, so an update has shapes to work on.
        public bool isTracking => m_CellShapes.IsCreated;

        // Whether the cells changed since the last build or update are enough of the map that rebuilding it costs less than updating a region around each of them.
        public bool isRebuildCheaper => isTracking && IsRebuildCheaper(m_ChangedCells.Count, m_BuiltCellCount);

        // Remember the cells that changed, so the next update recomputes only them and the cells around them.
        // Does nothing before a build of the component's own shapes, which reads the whole map anyway.
        public void ApplyTileChanges(NativeArray<Tilemap.SyncTile> entries)
        {
            if (!isTracking)
                return;

            for (var i = 0; i < entries.Length; ++i)
                m_ChangedCells.Add(m_JoinLayers ? ToColumn(entries[i].position) : entries[i].position);
        }

        // Bring the shapes in step with the cells changed since the last build or update.
        // An edit changes its cell and the cells across its edges; every cell meeting it at an edge or corner has ghosts reaching into it, so those are recomputed too.
        // Shapes whose geometry came out the same are kept.
        public void UpdateShapes(in TilemapBuildContext context)
        {
            var map = context.tilemap;
            var cache = context.tileCache;
            if (!isTracking || map == null || cache == null || m_Neighbours == null || m_ChangedCells.Count == 0)
                return;

            using var updateScope = TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ClippedDiff);

            // The Grid's placement is read again, so recomputed cells sit where the Tilemap places them now.
            m_Placement = TilemapCellTransforms.CellPlacement.Sample(map);

            var affected = new NativeHashSet<TilemapCellKey>(UpdateCapacity(m_ChangedCells.Count, 16), Allocator.Temp);
            var read = new NativeHashSet<TilemapCellKey>(UpdateCapacity(m_ChangedCells.Count, 64), Allocator.Temp);
            var looked = new NativeHashSet<TilemapCellKey>(UpdateCapacity(m_ChangedCells.Count, 128), Allocator.Temp);
            var entries = new NativeHashMap<TilemapCellKey, int>(UpdateCapacity(m_ChangedCells.Count, 128), Allocator.TempJob);
            var emitCells = new NativeList<int3>(Allocator.TempJob);
            var layout = default(Layout);
            var raw = default(RawSegments);
            var ghosts = default(NativeArray<float4>);
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
                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ClippedUpdateEmit))
                {
                    foreach (var cell in m_ChangedCells)
                        AddWithNeighbours(cell, affected);

                    // The ghost vertices of an affected cell's segments are read from the cells around it, so those are recomputed as well, though their shapes are not touched.
                    foreach (var cell in affected)
                        AddWithNeighbours(cell, read);

                    // Recomputing reads the neighbours' entries, so entries are looked up one ring further out, fresh from the Tilemap each update.
                    foreach (var cell in read)
                        AddWithNeighbours(cell, looked);

                    foreach (var cell in looked)
                    {
                        var lowest = cell.cell;
                        var entry = m_JoinLayers ? ReadColumnEntry(map, cache, cell, out lowest) : cache.GetSegmentEntry(map, cell);
                        if (entry >= 0)
                            entries.Add(cell, entry);

                        // A column's tiles are read fresh, so the cell its shapes report is noted again, and forgotten once the column is empty.
                        if (m_JoinLayers)
                        {
                            m_ColumnCells.Remove(cell);
                            if (entry >= 0)
                                NoteColumnCell(m_ColumnCells, cell, lowest, first: true);
                        }
                    }

                    foreach (var cell in read)
                    {
                        if (entries.ContainsKey(cell))
                            emitCells.Add(ToInt3(cell.cell));
                    }

                    layout = new Layout(cache, m_Neighbours);
                    raw = Emit(cache, layout, m_Placement, entries, emitCells.AsArray());
                    ghosts = FindGhosts(raw, layout, cache.tolerance);

                    foreach (var cell in affected)
                    {
                        geometry.Clear();
                        cells.Clear();

                        if (raw.ranges.TryGetValue(cell, out var range))
                        {
                            for (var i = range.x; i < range.x + range.y; ++i)
                                AddPlaced(raw.segments[i], ghosts[i], cell, context.relative, geometry, cells);
                        }

                        oldShapes.Clear();
                        foreach (var shape in m_CellShapes.GetValuesForKey(cell))
                            oldShapes.Add(shape);

                        m_CellShapes.Remove(cell);
                        MatchShapes(cell, oldShapes, geometry, kept, keptCells, reshaped, reshapedGeometry, destroyed, createGeometry, createCells);
                    }
                }

                var shapeContext = context.shapeContext;
                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ClippedUpdateDestroy))
                    shapeContext.DestroyShapes(destroyed.AsArray().AsReadOnlySpan());

                using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ClippedUpdateReshape))
                {
                    if (reshaped.Length > 0)
                        PhysicsShape.SetBatchGeometry(reshaped.AsArray().AsReadOnlySpan(), reshapedGeometry.AsArray().AsReadOnlySpan());

                    for (var i = 0; i < kept.Length; ++i)
                        m_CellShapes.Add(keptCells[i], kept[i]);

                    // A column's lowest tile can change while its outline stays the same, so a kept shape reports its column's cell again.
                    if (m_JoinLayers && kept.Length > 0)
                        StampColumns(context, kept.AsArray(), keptCells.AsArray(), m_ColumnCells);
                }

                if (createGeometry.Length > 0)
                {
                    using var createScope = TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ClippedUpdateCreate);
                    var created = shapeContext.CreateShapeBatch(createGeometry.AsArray().AsReadOnlySpan());
                    StampColumns(context, created, createCells.AsArray(), m_ColumnCells);

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

                if (ghosts.IsCreated)
                    ghosts.Dispose();

                raw.Dispose();
                layout.Dispose();
                emitCells.Dispose();
                entries.Dispose();
                looked.Dispose();
                read.Dispose();
                affected.Dispose();
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

            if (m_ColumnCells.IsCreated)
                m_ColumnCells.Dispose();
        }

        private void EnsureTracking()
        {
            if (m_CellShapes.IsCreated)
                return;

            m_CellShapes = new NativeParallelMultiHashMap<TilemapCellKey, PhysicsShape>(InitialCapacity, Allocator.Persistent);
            m_ChangedCells = new NativeHashSet<TilemapCellKey>(InitialCapacity, Allocator.Persistent);
            m_ColumnCells = new NativeHashMap<TilemapCellKey, Vector3Int>(InitialCapacity, Allocator.Persistent);
        }

        // Whether tiles on different Z layers are joined: unless the layers are split, and only when a cell's Z layer does not move it within the Tilemap's plane.
        // Joined, every table is keyed by a cell's column, its x and y on layer zero, which only stands for where its tiles sit when the layer leaves that place unchanged.
        private static bool JoinsLayers(Tilemap map, bool splitZLayers)
        {
            if (splitZLayers)
                return false;

            var layerStep = map.CellToLocal(new Vector3Int(0, 0, 1)) - map.CellToLocal(Vector3Int.zero);
            return Mathf.Approximately(layerStep.x, 0f) && Mathf.Approximately(layerStep.y, 0f);
        }

        // The column a cell's tile is joined in: the same x and y, on layer zero.
        private static Vector3Int ToColumn(Vector3Int cell) => new(cell.x, cell.y, 0);

        // Note the cell a column's shapes report, its lowest tile, so a contact still names a tile that is really there.
        // A column whose lowest tile is on layer zero reports itself, so only the others are kept.
        // Without first, the cell only replaces the one already noted when it is lower.
        private static void NoteColumnCell(NativeHashMap<TilemapCellKey, Vector3Int> columnCells, Vector3Int column, Vector3Int cell, bool first)
        {
            if (!first)
            {
                var noted = columnCells.TryGetValue(column, out var lowest) ? lowest : column;
                if (cell.z >= noted.z)
                    return;
            }

            if (cell.z == 0)
                columnCells.Remove(column);
            else
                columnCells[column] = cell;
        }

        // The entry of every tile in a column, read from the Tilemap layer by layer, with the union of their outlines when several are stacked.
        // Also returns the column's lowest tile, the cell its shapes report. Returns -1 when no tile in the column contributes anything.
        private int ReadColumnEntry(Tilemap map, TilemapTileCache cache, Vector3Int column, out Vector3Int lowest)
        {
            var bounds = map.cellBounds;
            var single = -1;

            lowest = column;
            m_StackContents.Clear();

            for (var z = bounds.zMin; z < bounds.zMax; ++z)
            {
                var cell = new Vector3Int(column.x, column.y, z);
                if (!TilemapCellReader.TryRead(map, cell, out var content))
                    continue;

                var entry = cache.GetSegmentEntry(content);
                if (entry < 0)
                    continue;

                if (m_StackContents.Count == 0)
                {
                    lowest = cell;
                    single = entry;
                }

                m_StackContents.Add(content);
            }

            return m_StackContents.Count > 1 ? cache.GetStackedSegmentEntry(m_StackContents) : single;
        }

        // Record which cell each shape came from, the column's lowest tile when layers are joined, so a contact names a tile that is really there.
        private static void StampColumns(in TilemapBuildContext context, NativeArray<PhysicsShape> shapes, NativeArray<Vector3Int> columns, NativeHashMap<TilemapCellKey, Vector3Int> columnCells)
        {
            if (columnCells.IsEmpty)
            {
                StampCells(context, shapes, columns);
                return;
            }

            var cells = new NativeArray<Vector3Int>(columns.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < columns.Length; ++i)
                cells[i] = columnCells.TryGetValue(columns[i], out var cell) ? cell : columns[i];

            StampCells(context, shapes, cells);
            cells.Dispose();
        }

        // Add a cell and every cell meeting it at an edge or a corner. The corner lists already hold the cells across the edges.
        private void AddWithNeighbours(Vector3Int cell, NativeHashSet<TilemapCellKey> cells)
        {
            cells.Add(cell);

            for (var corner = 0; corner < m_Neighbours.edgeCount; ++corner)
            {
                foreach (var offset in m_Neighbours.GetCornerOffsets(cell, corner))
                    cells.Add(cell + offset);
            }
        }

        // Work out the surviving pieces of the given cells, every cell sharing an edge with them having its entry in the table.
        private static RawSegments Emit(TilemapTileCache cache, Layout layout, TilemapCellTransforms.CellPlacement placement, NativeHashMap<TilemapCellKey, int> entries,
            NativeArray<int3> cells)
        {
            var chunkCount = (cells.Length + ChunkSize - 1) / ChunkSize;
            var stream = new NativeStream(math.max(chunkCount, 1), Allocator.TempJob);

            try
            {
                new EmitJob
                {
                    cells = cells,
                    entries = entries,
                    vertices = cache.segmentVertices,
                    pieces = cache.segmentPieces,
                    segmentEntries = cache.segmentEntries,
                    cellShape = layout.cellShape,
                    edgeDirections = layout.edgeDirections,
                    edgeLengths = layout.edgeLengths,
                    edgeNeighbours = layout.edgeNeighbours,
                    edgeCount = layout.edgeCount,
                    placement = placement,
                    tolerance = cache.tolerance,
                    output = stream.AsWriter()
                }.Schedule(chunkCount, 1).Complete();

                return RawSegments.Read(stream, chunkCount);
            }
            finally
            {
                stream.Dispose();
            }
        }

        // Find every surviving piece's ghost vertices, start and end packed into one value per piece.
        private static NativeArray<float4> FindGhosts(RawSegments raw, Layout layout, float tolerance)
        {
            var ghosts = new NativeArray<float4>(raw.segments.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            if (raw.segments.Length == 0)
                return ghosts;

            new GhostJob
            {
                segments = raw.segments.AsArray(),
                cells = raw.cells.AsArray(),
                ranges = raw.ranges,
                cornerOffsets = layout.cornerOffsets,
                cornerRanges = layout.cornerRanges,
                edgeCount = layout.edgeCount,
                tolerance = tolerance,
                ghosts = ghosts
            }.Schedule((raw.segments.Length + ChunkSize - 1) / ChunkSize, 1).Complete();

            return ghosts;
        }

        // Place one piece with its ghost vertices in the body's space and add it to the lists, unless it is too short to stand as a shape.
        private static void AddPlaced(float4 segment, float4 ghosts, Vector3Int cell, Matrix4x4 relative, NativeList<ChainSegmentGeometry> geometry, NativeList<Vector3Int> cells)
        {
            var placed = Place(segment, ghosts, relative);
            if (!placed.isValid)
                return;

            geometry.Add(placed);
            cells.Add(cell);
        }

        // The cell shape, its edges, and which cells meet each edge and corner, copied into native arrays a job can read.
        private struct Layout : IDisposable
        {
            public Layout(TilemapTileCache cache, TilemapCellNeighbours neighbours)
            {
                edgeCount = neighbours.edgeCount;

                var shape = cache.cellShape;
                cellShape = new NativeArray<float2>(edgeCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                edgeDirections = new NativeArray<float2>(edgeCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                edgeLengths = new NativeArray<float>(edgeCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                for (var edge = 0; edge < edgeCount; ++edge)
                {
                    cellShape[edge] = shape[edge];
                    edgeLengths[edge] = cache.EdgeLength(edge);
                    edgeDirections[edge] = cache.EdgePoint(edge, 1f) - shape[edge];
                }

                var tableLength = TilemapCellNeighbours.ParityCount * edgeCount;
                edgeNeighbours = new NativeArray<int4>(tableLength, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                cornerRanges = new NativeArray<int2>(tableLength, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var offsets = new NativeList<int3>(tableLength * 4, Allocator.Temp);
                neighbours.WriteTables(edgeNeighbours, offsets, cornerRanges);

                cornerOffsets = new NativeArray<int3>(offsets.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                cornerOffsets.CopyFrom(offsets.AsArray());
                offsets.Dispose();
            }

            public void Dispose()
            {
                if (cellShape.IsCreated)
                    cellShape.Dispose();

                if (edgeDirections.IsCreated)
                    edgeDirections.Dispose();

                if (edgeLengths.IsCreated)
                    edgeLengths.Dispose();

                if (edgeNeighbours.IsCreated)
                    edgeNeighbours.Dispose();

                if (cornerOffsets.IsCreated)
                    cornerOffsets.Dispose();

                if (cornerRanges.IsCreated)
                    cornerRanges.Dispose();
            }

            public int edgeCount;
            public NativeArray<float2> cellShape;
            public NativeArray<float2> edgeDirections;
            public NativeArray<float> edgeLengths;
            public NativeArray<int4> edgeNeighbours;
            public NativeArray<int3> cornerOffsets;
            public NativeArray<int2> cornerRanges;
        }

        // Work out the surviving pieces of a run of cells, one chunk of them per execution, so each chunk makes its working lists once.
        [BurstCompile]
        private struct EmitJob : IJobParallelFor
        {
            public void Execute(int chunk)
            {
                var scratch = new Scratch(0);

                output.BeginForEachIndex(chunk);

                var end = math.min((chunk + 1) * ChunkSize, cells.Length);
                for (var i = chunk * ChunkSize; i < end; ++i)
                    EmitCell(cells[i], scratch);

                output.EndForEachIndex();

                scratch.Dispose();
            }

            // Write a cell's surviving segments, in the Tilemap's local space, before ghost vertices are found.
            // Each edge joins its points with the neighbour's and removes what both cover; pieces through the cell are written last, from the joined vertices.
            // An edge both cells fill cancels outright, so a cell inside a solid area writes nothing.
            private void EmitCell(int3 cell, Scratch scratch)
            {
                if (!entries.TryGetValue(ToKey(cell), out var id))
                    return;

                var entry = segmentEntries[id];

                var allCancel = true;
                scratch.cancelling.Clear();

                for (var edge = 0; edge < edgeCount; ++edge)
                {
                    var cancels = CancelsAcross(cell, edge, entry);
                    scratch.cancelling.Add(cancels);
                    allCancel &= cancels;
                }

                if (allCancel && !entry.hasThroughPieces)
                    return;

                var origin = ((float3)placement.CellToLocal(ToVector3Int(cell))).xy;

                scratch.placed.ResizeUninitialized(entry.vertexCount);
                var placed = scratch.placed.AsArray();
                for (var v = 0; v < entry.vertexCount; ++v)
                    placed[v] = vertices[entry.vertexStart + v];

                for (var edge = 0; edge < edgeCount; ++edge)
                {
                    if (!scratch.cancelling[edge])
                    {
                        EmitEdge(cell, edge, entry, placed, origin, scratch);
                        continue;
                    }

                    // A filled edge has a single piece along it.
                    for (var p = 0; p < entry.pieceCount; ++p)
                    {
                        var piece = pieces[entry.pieceStart + p];
                        if (piece.edge != edge)
                            continue;

                        placed[piece.start] = cellShape[edge];
                        placed[piece.end] = cellShape[(edge + 1) % edgeCount];
                        break;
                    }
                }

                for (var p = 0; p < entry.pieceCount; ++p)
                {
                    var piece = pieces[entry.pieceStart + p];
                    if (piece.edge < 0)
                        Write(cell, origin + placed[piece.start], origin + placed[piece.end]);
                }
            }

            // Whether the cell and the neighbor across an edge both fill that edge, so everything along it cancels.
            private bool CancelsAcross(int3 cell, int edge, TilemapTileCache.SegmentEntry entry)
            {
                if ((entry.fullEdges & (1 << edge)) == 0)
                    return false;

                if (!TryGetEdgeNeighbour(cell, edge, out var neighbour, out var neighbourEdge) || !entries.TryGetValue(ToKey(neighbour), out var neighbourEntry))
                    return false;

                return (segmentEntries[neighbourEntry].fullEdges & (1 << neighbourEdge)) != 0;
            }

            // Join the cell's points along an edge with the neighbour's, then write what the neighbour does not cover.
            // Both sides measure from the lower cell, so they agree on the joined points; a point near a corner joins the corner, so corners never move.
            private void EmitEdge(int3 cell, int edge, TilemapTileCache.SegmentEntry entry, NativeArray<float2> placed, float2 origin, Scratch scratch)
            {
                var hasOwnPiece = false;
                for (var p = 0; p < entry.pieceCount && !hasOwnPiece; ++p)
                    hasOwnPiece = pieces[entry.pieceStart + p].edge == edge;

                if (!hasOwnPiece)
                    return;

                var hasNeighbour = TryGetEdgeNeighbour(cell, edge, out var neighbour, out var neighbourEdge) && entries.ContainsKey(ToKey(neighbour));
                var neighbourFrame = hasNeighbour && IsLower(neighbour, cell);
                var length = neighbourFrame ? edgeLengths[neighbourEdge] : edgeLengths[edge];

                // Every point along the edge in the shared frame: x is the distance, y is zero for the cell's own vertex and one for the neighbor's, z is the cell's vertex index.
                var points = scratch.points;
                points.Clear();
                points.Add(new float3(0f, 2f, -1f));
                points.Add(new float3(length, 2f, -1f));

                for (var p = 0; p < entry.pieceCount; ++p)
                {
                    var piece = pieces[entry.pieceStart + p];
                    if (piece.edge != edge)
                        continue;

                    points.Add(new float3(ToShared(EdgeParameter(vertices[entry.vertexStart + piece.start], edge), length, neighbourFrame), 0f, piece.start));
                    points.Add(new float3(ToShared(EdgeParameter(vertices[entry.vertexStart + piece.end], edge), length, neighbourFrame), 0f, piece.end));
                }

                var neighbourIntervals = scratch.intervals;
                neighbourIntervals.Clear();
                if (hasNeighbour)
                {
                    var neighbourEntry = segmentEntries[entries[ToKey(neighbour)]];
                    for (var p = 0; p < neighbourEntry.pieceCount; ++p)
                    {
                        var piece = pieces[neighbourEntry.pieceStart + p];
                        if (piece.edge != neighbourEdge)
                            continue;

                        var from = FromNeighbour(EdgeParameter(vertices[neighbourEntry.vertexStart + piece.start], neighbourEdge), length, neighbourFrame);
                        var to = FromNeighbour(EdgeParameter(vertices[neighbourEntry.vertexStart + piece.end], neighbourEdge), length, neighbourFrame);

                        points.Add(new float3(from, 1f, -1f));
                        points.Add(new float3(to, 1f, -1f));
                        neighbourIntervals.Add(new float2(from, to));
                    }
                }

                SortPoints(points);

                // Join each run of points closer than the tolerance to its first point, or to the corner it contains.
                scratch.joined.ResizeUninitialized(points.Length);
                scratch.joinedCorner.ResizeUninitialized(points.Length);
                var joined = scratch.joined.AsArray();
                var joinedCorner = scratch.joinedCorner.AsArray();

                for (var first = 0; first < points.Length;)
                {
                    var last = first;
                    while (last + 1 < points.Length && points[last + 1].x - points[last].x <= tolerance)
                        ++last;

                    var target = points[first].x;
                    var corner = -1;
                    for (var i = first; i <= last; ++i)
                    {
                        if (points[i].y < 1.5f)
                            continue;

                        target = points[i].x;
                        corner = points[i].x > 0f ? 1 : 0;
                    }

                    for (var i = first; i <= last; ++i)
                    {
                        joined[i] = target;
                        joinedCorner[i] = corner;
                    }

                    first = last + 1;
                }

                // Move the cell's own vertices to their joined points, and remember each one's joined distance in the shared frame.
                // A vertex joined to a corner takes the cell shape's own corner, so the pieces either side of the corner meet it exactly whichever edge placed it.
                scratch.vertexShared.Clear();
                scratch.vertexShared.Resize(entry.vertexCount, NativeArrayOptions.ClearMemory);
                var vertexShared = scratch.vertexShared.AsArray();

                for (var i = 0; i < points.Length; ++i)
                {
                    var point = points[i];
                    if (point.y > 0.5f)
                        continue;

                    var vertex = (int)point.z;
                    vertexShared[vertex] = joined[i];

                    if (joinedCorner[i] < 0)
                    {
                        placed[vertex] = EdgePoint(edge, FromShared(joined[i], length, neighbourFrame));
                        continue;
                    }

                    // The shared frame's start is the cell's own edge start, unless the frame is the neighbor's, which runs the other way.
                    var isEdgeStart = (joinedCorner[i] == 0) != neighbourFrame;
                    placed[vertex] = cellShape[isEdgeStart ? edge : (edge + 1) % edgeCount];
                }

                // The neighbor's stretches, joined the same way, so both sides compare identical distances.
                for (var n = 0; n < neighbourIntervals.Length; ++n)
                {
                    var interval = neighbourIntervals[n];
                    var low = Joined(points, joined, math.min(interval.x, interval.y), 1f);
                    var high = Joined(points, joined, math.max(interval.x, interval.y), 1f);
                    neighbourIntervals[n] = new float2(low, high);
                }

                for (var p = 0; p < entry.pieceCount; ++p)
                {
                    var piece = pieces[entry.pieceStart + p];
                    if (piece.edge != edge)
                        continue;

                    EmitSurvivors(cell, edge, length, neighbourFrame, vertexShared[piece.start], vertexShared[piece.end], placed[piece.start], placed[piece.end], neighbourIntervals, origin);
                }
            }

            // Write what is left of one of the cell's pieces along an edge once every stretch the neighbor covers is taken away.
            // The ends of the piece that survive are written from the cell's joined vertices, so they meet the pieces running through the cell exactly.
            private void EmitSurvivors(int3 cell, int edge, float length, bool neighbourFrame, float startShared, float endShared, float2 startPoint, float2 endPoint,
                NativeList<float2> neighbourIntervals, float2 origin)
            {
                // Walk the shared frame from low to high, and write the piece back the right way round.
                var low = math.min(startShared, endShared);
                var high = math.max(startShared, endShared);
                var lowPoint = startShared <= endShared ? startPoint : endPoint;
                var highPoint = startShared <= endShared ? endPoint : startPoint;
                var reversed = startShared > endShared;

                var cursor = low;
                var cursorPoint = lowPoint;

                while (cursor < high)
                {
                    // The next stretch the neighbor covers that reaches past the cursor.
                    var coverStart = high;
                    var coverEnd = high;
                    for (var n = 0; n < neighbourIntervals.Length; ++n)
                    {
                        var interval = neighbourIntervals[n];
                        if (interval.y <= cursor || interval.x >= coverStart)
                            continue;

                        coverStart = math.max(interval.x, cursor);
                        coverEnd = interval.y;
                    }

                    if (coverStart > cursor)
                    {
                        var stopPoint = coverStart >= high ? highPoint : EdgePoint(edge, FromShared(coverStart, length, neighbourFrame));
                        var from = origin + cursorPoint;
                        var to = origin + stopPoint;

                        if (reversed)
                            Write(cell, to, from);
                        else
                            Write(cell, from, to);
                    }

                    if (coverStart >= high)
                        break;

                    cursor = math.min(coverEnd, high);
                    cursorPoint = cursor >= high ? highPoint : EdgePoint(edge, FromShared(cursor, length, neighbourFrame));
                }
            }

            // The cell across an edge of a cell, and which of its own edges that is. False when no cell shares the edge.
            private bool TryGetEdgeNeighbour(int3 cell, int edge, out int3 neighbour, out int neighbourEdge)
            {
                var entry = edgeNeighbours[TilemapCellNeighbours.Parity(cell) * edgeCount + edge];

                neighbour = cell + entry.xyz;
                neighbourEdge = entry.w;
                return entry.w >= 0;
            }

            // How far along a cell edge a point on it lies, from the vertex the edge starts at.
            private float EdgeParameter(float2 point, int edge) => math.dot(point - cellShape[edge], edgeDirections[edge]);

            // The point a distance along a cell edge, in cell space.
            private float2 EdgePoint(int edge, float parameter) => cellShape[edge] + edgeDirections[edge] * parameter;

            private void Write(int3 cell, float2 start, float2 end) => output.Write(new CellSegment { cell = cell, segment = new float4(start, end) });

            // A distance along the cell's own edge, in the shared frame.
            private static float ToShared(float parameter, float length, bool neighbourFrame) => neighbourFrame ? length - parameter : parameter;

            // A distance along the neighbor's edge, in the shared frame.
            private static float FromNeighbour(float parameter, float length, bool neighbourFrame) => neighbourFrame ? parameter : length - parameter;

            // A distance in the shared frame, back along the cell's own edge.
            private static float FromShared(float shared, float length, bool neighbourFrame) => neighbourFrame ? length - shared : shared;

            // The joined distance of the point of the given side nearest a distance, in the shared frame.
            private static float Joined(NativeList<float3> points, NativeArray<float> joined, float shared, float side)
            {
                var best = shared;
                var bestDistance = float.MaxValue;

                for (var i = 0; i < points.Length; ++i)
                {
                    if (math.abs(points[i].y - side) > 0.5f)
                        continue;

                    var distance = math.abs(points[i].x - shared);
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    best = joined[i];
                }

                return best;
            }

            // Insertion sort of the points along an edge by distance, then the cell's own before the neighbor's, then by vertex, so the order never depends on how they were gathered.
            // An edge has only a handful of points.
            private static void SortPoints(NativeList<float3> points)
            {
                for (var i = 1; i < points.Length; ++i)
                {
                    var value = points[i];
                    var j = i - 1;

                    while (j >= 0 && IsAfter(points[j], value))
                    {
                        points[j + 1] = points[j];
                        --j;
                    }

                    points[j + 1] = value;
                }
            }

            private static bool IsAfter(float3 a, float3 b)
            {
                if (a.x != b.x)
                    return a.x > b.x;

                return a.y != b.y ? a.y > b.y : a.z > b.z;
            }

            [ReadOnly] public NativeArray<int3> cells;
            [ReadOnly] public NativeHashMap<TilemapCellKey, int> entries;
            [ReadOnly] public NativeArray<Vector2> vertices;
            [ReadOnly] public NativeArray<TilemapTileCache.SegmentPiece> pieces;
            [ReadOnly] public NativeArray<TilemapTileCache.SegmentEntry> segmentEntries;
            [ReadOnly] public NativeArray<float2> cellShape;
            [ReadOnly] public NativeArray<float2> edgeDirections;
            [ReadOnly] public NativeArray<float> edgeLengths;
            [ReadOnly] public NativeArray<int4> edgeNeighbours;
            public int edgeCount;
            public TilemapCellTransforms.CellPlacement placement;
            public float tolerance;
            public NativeStream.Writer output;
        }

        // Find each piece's ghost vertices from the pieces of its own cell and the cells meeting it at an edge or corner, one chunk of pieces per execution.
        [BurstCompile]
        private struct GhostJob : IJobParallelFor
        {
            public void Execute(int chunk)
            {
                var nearby = new NativeList<int2>(16, Allocator.Temp);
                var gatheredFor = new int3(int.MinValue, int.MinValue, int.MinValue);

                var end = math.min((chunk + 1) * ChunkSize, segments.Length);
                for (var i = chunk * ChunkSize; i < end; ++i)
                {
                    var cell = cells[i];
                    if (!cell.Equals(gatheredFor))
                    {
                        GatherNearby(cell, nearby);
                        gatheredFor = cell;
                    }

                    var segment = segments[i];
                    var start = segment.xy;
                    var finish = segment.zw;

                    ghosts[i] = new float4(
                        FindGhost(segments, nearby, i, start, finish, tolerance, arriving: true),
                        FindGhost(segments, nearby, i, finish, start, tolerance, arriving: false));
                }

                nearby.Dispose();
            }

            // The cell's own run of pieces, then the runs of every cell meeting it at a corner, each once.
            private void GatherNearby(int3 cell, NativeList<int2> nearby)
            {
                nearby.Clear();
                nearby.Add(ranges[ToKey(cell)]);

                var parity = TilemapCellNeighbours.Parity(cell);
                for (var corner = 0; corner < edgeCount; ++corner)
                {
                    var run = cornerRanges[parity * edgeCount + corner];
                    for (var o = run.x; o < run.x + run.y; ++o)
                    {
                        if (ranges.TryGetValue(ToKey(cell + cornerOffsets[o]), out var range) && !Contains(nearby, range))
                            nearby.Add(range);
                    }
                }
            }

            private static bool Contains(NativeList<int2> ranges, int2 range)
            {
                for (var i = 0; i < ranges.Length; ++i)
                {
                    if (ranges[i].Equals(range))
                        return true;
                }

                return false;
            }

            [ReadOnly] public NativeArray<float4> segments;
            [ReadOnly] public NativeArray<int3> cells;
            [ReadOnly] public NativeHashMap<TilemapCellKey, int2> ranges;
            [ReadOnly] public NativeArray<int3> cornerOffsets;
            [ReadOnly] public NativeArray<int2> cornerRanges;
            public int edgeCount;
            public float tolerance;
            [NativeDisableParallelForRestriction] public NativeArray<float4> ghosts;
        }

        // Working storage for one chunk of cells, made once per chunk and reused for every cell in it, so a cell costs no allocation of its own.
        private struct Scratch : IDisposable
        {
            public Scratch(int unused)
            {
                placed = new NativeList<float2>(32, Allocator.Temp);
                points = new NativeList<float3>(32, Allocator.Temp);
                intervals = new NativeList<float2>(16, Allocator.Temp);
                joined = new NativeList<float>(32, Allocator.Temp);
                joinedCorner = new NativeList<int>(32, Allocator.Temp);
                vertexShared = new NativeList<float>(32, Allocator.Temp);
                cancelling = new NativeList<bool>(8, Allocator.Temp);
            }

            public void Dispose()
            {
                cancelling.Dispose();
                vertexShared.Dispose();
                joinedCorner.Dispose();
                joined.Dispose();
                intervals.Dispose();
                points.Dispose();
                placed.Dispose();
            }

            public NativeList<float2> placed;
            public NativeList<float3> points;
            public NativeList<float2> intervals;
            public NativeList<float> joined;
            public NativeList<int> joinedCorner;
            public NativeList<float> vertexShared;

            // Whether each of the cell's edges cancels against its neighbor.
            public NativeList<bool> cancelling;
        }

        #region Internal

        // How many cells the tables start out able to hold, before they have to grow.
        private const int InitialCapacity = 256;

        // How many cells, or pieces, one execution of a job works through.
        private const int ChunkSize = 256;

        // Which cells share each edge and corner of a cell, worked out again by a build only when the layout has changed since.
        private TilemapCellNeighbours m_Neighbours;

        // Where the map's cells sit, sampled at the start of every build and update.
        private TilemapCellTransforms.CellPlacement m_Placement;

        // The shapes each cell produced, and the cells changed since the last build or update.
        private NativeParallelMultiHashMap<TilemapCellKey, PhysicsShape> m_CellShapes;
        private NativeHashSet<TilemapCellKey> m_ChangedCells;

        // How many cells the last build of the component's own shapes read, which an update's batch is weighed against.
        private int m_BuiltCellCount;

        // Whether the last build of the component's own shapes joined tiles on different Z layers, which decides how its updates key cells.
        private bool m_JoinLayers;

        // The cell each column's shapes report, for the columns whose lowest tile is not on layer zero. Empty unless layers are joined.
        private NativeHashMap<TilemapCellKey, Vector3Int> m_ColumnCells;

        // The tiles of one column, refilled for each column read, so reading allocates nothing once it has grown.
        private readonly System.Collections.Generic.List<TilemapCellReader.Content> m_StackContents = new();

        #endregion
    }
}
