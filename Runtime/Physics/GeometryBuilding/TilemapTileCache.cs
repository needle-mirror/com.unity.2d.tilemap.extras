using System;
using System.Text;

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.U2D.Physics
{
    // The geometry of every distinct tile content (sprite and transform) a map uses, in cell space, shaped once per content rather than per cell.
    // Cleared on every own build, since it depends on the Grid's layout and the tolerance; updates reuse it.
    internal sealed class TilemapTileCache : IDisposable
    {
        public TilemapTileCache()
        {
            m_Polygons = new NativeList<PolygonGeometry>(InitialCapacity, Allocator.Persistent);
            m_PolygonRanges = new NativeHashMap<TileKey, int2>(InitialCapacity, Allocator.Persistent);
            m_UnclippedPolygonRanges = new NativeHashMap<TileKey, int2>(InitialCapacity, Allocator.Persistent);

            m_SegmentVertices = new NativeList<Vector2>(InitialCapacity * 8, Allocator.Persistent);
            m_SegmentPieces = new NativeList<SegmentPiece>(InitialCapacity * 8, Allocator.Persistent);
            m_SegmentEntries = new NativeList<SegmentEntry>(InitialCapacity, Allocator.Persistent);
            m_SegmentEntryIds = new NativeHashMap<TileKey, int>(InitialCapacity, Allocator.Persistent);

            m_OutlineVertices = new NativeList<float2>(InitialCapacity * 8, Allocator.Persistent);
            m_OutlineContours = new NativeList<int2>(InitialCapacity, Allocator.Persistent);
            m_OutlineEntries = new NativeList<OutlineEntry>(InitialCapacity, Allocator.Persistent);
            m_OutlineEntryIds = new NativeHashMap<TileKey, int>(InitialCapacity, Allocator.Persistent);

            m_StackedEntryIds = new NativeHashMap<int2, int>(InitialCapacity, Allocator.Persistent);
            m_StackedPolygonRanges = new NativeHashMap<int, int2>(InitialCapacity, Allocator.Persistent);
        }

        // Forget every entry and take the map's current layout, ready for a build.
        public void Reset(Tilemap map, float tolerance)
        {
            m_Polygons.Clear();
            m_PolygonRanges.Clear();
            m_UnclippedPolygonRanges.Clear();

            m_SegmentVertices.Clear();
            m_SegmentPieces.Clear();
            m_SegmentEntries.Clear();
            m_SegmentEntryIds.Clear();

            m_OutlineVertices.Clear();
            m_OutlineContours.Clear();
            m_OutlineEntries.Clear();
            m_OutlineEntryIds.Clear();

            m_StackedEntryIds.Clear();
            m_StackedPolygonRanges.Clear();

            m_CellShape = TilemapCellTransforms.GetGridCellShape(map, m_CellShape);
            m_CellCenterOffset = TilemapCellTransforms.GetCellCenterOffset(map);
            m_Orientation = map.orientationMatrix;
            m_Tolerance = tolerance;
            m_EdgeEpsilon = EdgeEpsilonFraction * CellSize();

            // Every cell edge's direction and length, worked out once here rather than on every question about a point along an edge.
            // The arrays are kept between builds and replaced only when the edge count changes, so a rebuild allocates nothing.
            var edgeCount = m_CellShape?.Length ?? 0;
            if (m_EdgeLengths == null || m_EdgeLengths.Length != edgeCount)
            {
                m_EdgeDirections = new Vector2[edgeCount];
                m_EdgeLengths = new float[edgeCount];
            }

            for (var edge = 0; edge < edgeCount; ++edge)
            {
                var edgeVector = m_CellShape[(edge + 1) % edgeCount] - m_CellShape[edge];
                m_EdgeLengths[edge] = edgeVector.magnitude;
                m_EdgeDirections[edge] = edgeVector.normalized;
            }
        }

        // Where a cell's polygons are in polygons: the first and how many, zero when the content has none.
        // Clipped polygons are cut to the cell. A Grid cell is its cell shape either way, or nothing when the Grid has none.
        public int2 GetPolygonRange(in TilemapCellReader.Content content, bool clipped)
        {
            if (!TryGetKey(content, out var key) || (m_CellShape == null && (clipped || content.sprite == null)))
                return int2.zero;

            var ranges = clipped || content.sprite == null ? m_PolygonRanges : m_UnclippedPolygonRanges;
            if (!ranges.TryGetValue(key, out var range))
            {
                range = content.sprite == null ? AddGridPolygons() : clipped ? AddSpritePolygons(content.sprite, key.tileMatrix) : AddUnclippedSpritePolygons(content.sprite, key.tileMatrix);
                ranges.Add(key, range);
            }

            return range;
        }

        // The outline entry a cell's tile contributes to Segments output, clipped to the cell, or -1 when the tile contributes nothing.
        // Read the entry with segmentEntries, and its vertices and pieces with segmentVertices and segmentPieces.
        public int GetSegmentEntry(Tilemap map, Vector3Int cell) => TilemapCellReader.TryRead(map, cell, out var content) ? GetSegmentEntry(content) : -1;

        // The same, for a cell's content already read.
        public int GetSegmentEntry(in TilemapCellReader.Content content)
        {
            if (m_CellShape == null || !TryGetKey(content, out var key))
                return -1;

            if (!m_SegmentEntryIds.TryGetValue(key, out var id))
            {
                id = m_SegmentEntries.Length;
                m_SegmentEntries.Add(content.sprite == null ? AddGridSegments() : AddSpriteSegments(content.sprite, key.tileMatrix));
                m_SegmentEntryIds.Add(key, id);
            }

            return m_SegmentEntries[id].pieceCount > 0 ? id : -1;
        }

        // The outline entry for tiles stacked at one x and y on different Z layers, clipped to the cell, or -1 when none of them contributes anything.
        // The stack is one solid, the union of its tiles' outlines, so it builds the edges a single tile of that outline would.
        // A Grid tile fills the cell, so a stack holding one is the cell shape, and tiles with the same outline add nothing to each other.
        public int GetStackedSegmentEntry(System.Collections.Generic.List<TilemapCellReader.Content> contents)
        {
            var stacked = -1;
            var isGrid = false;

            foreach (var content in contents)
            {
                var entry = GetSegmentEntry(content);
                if (entry < 0 || entry == stacked || isGrid)
                    continue;

                if (content.colliderType == Tile.ColliderType.Grid)
                {
                    stacked = entry;
                    isGrid = true;
                    continue;
                }

                if (!m_StackedPolygonRanges.ContainsKey(entry))
                    m_StackedPolygonRanges.Add(entry, GetPolygonRange(content, clipped: true));

                stacked = stacked < 0 ? entry : GetStackedPair(stacked, entry);
            }

            return stacked >= 0 && m_SegmentEntries[stacked].pieceCount > 0 ? stacked : -1;
        }

        // The outline entry of two stacked entries, made the first time the pair is stacked. Both entries' clipped polygons are known.
        private int GetStackedPair(int first, int second)
        {
            var pair = new int2(first, second);
            if (m_StackedEntryIds.TryGetValue(pair, out var id))
                return id;

            // The pair's polygons are both entries' polygons end to end, so the pair can be stacked again with a further entry.
            var firstRange = m_StackedPolygonRanges[first];
            var secondRange = m_StackedPolygonRanges[second];
            var range = new int2(m_Polygons.Length, firstRange.y + secondRange.y);

            for (var i = 0; i < firstRange.y; ++i)
                m_Polygons.Add(m_Polygons[firstRange.x + i]);

            for (var i = 0; i < secondRange.y; ++i)
                m_Polygons.Add(m_Polygons[secondRange.x + i]);

            id = m_SegmentEntries.Length;
            m_SegmentEntries.Add(AddStackedSegments(range));
            m_StackedEntryIds.Add(pair, id);
            m_StackedPolygonRanges.Add(id, range);

            return id;
        }

        // The outline entry a cell's tile contributes to Segments output while cells are not clipped, or -1 when the tile contributes nothing.
        // A Grid cell needs the Grid's cell shape, so it contributes nothing when the Grid has none, while a Sprite cell needs only its sprite.
        // Read the entry with outlineEntries, its contours with outlineContours, and their vertices with outlineVertices.
        public int GetOutlineEntry(Tilemap map, Vector3Int cell) => TilemapCellReader.TryRead(map, cell, out var content) ? GetOutlineEntry(content) : -1;

        // The same, for a cell's content already read.
        public int GetOutlineEntry(in TilemapCellReader.Content content)
        {
            if (!TryGetKey(content, out var key) || (content.sprite == null && m_CellShape == null))
                return -1;

            if (!m_OutlineEntryIds.TryGetValue(key, out var id))
            {
                id = m_OutlineEntries.Length;
                m_OutlineEntries.Add(content.sprite == null ? AddGridOutline() : AddSpriteOutline(content.sprite, key.tileMatrix));
                m_OutlineEntryIds.Add(key, id);
            }

            return m_OutlineEntries[id].contourCount > 0 ? id : -1;
        }

        // How far along a cell edge a point on it lies, from the vertex the edge starts at.
        public float EdgeParameter(Vector2 point, int edge) => Vector2.Dot(point - m_CellShape[edge], EdgeDirection(edge));

        // The point a distance along a cell edge, in cell space.
        public Vector2 EdgePoint(int edge, float parameter) => m_CellShape[edge] + EdgeDirection(edge) * parameter;

        // The length of a cell edge.
        public float EdgeLength(int edge) => m_EdgeLengths[edge];

        // Approximate memory held between builds, for the diagnostic timings.
        public void AppendMemoryReport(StringBuilder report)
        {
            TilemapBuildTimings.AppendBytes(report, "tileCachePolygons", TilemapBuildTimings.ListBytes(m_Polygons) +
                                                                        TilemapBuildTimings.HashBytes<TileKey, int2>(m_PolygonRanges.Capacity) +
                                                                        TilemapBuildTimings.HashBytes<TileKey, int2>(m_UnclippedPolygonRanges.Capacity));
            TilemapBuildTimings.AppendBytes(report, "tileCacheSegments", TilemapBuildTimings.ListBytes(m_SegmentVertices) + TilemapBuildTimings.ListBytes(m_SegmentPieces) +
                                                                        TilemapBuildTimings.ListBytes(m_SegmentEntries) +
                                                                        TilemapBuildTimings.HashBytes<TileKey, int>(m_SegmentEntryIds.Capacity));
            TilemapBuildTimings.AppendBytes(report, "tileCacheOutlines", TilemapBuildTimings.ListBytes(m_OutlineVertices) + TilemapBuildTimings.ListBytes(m_OutlineContours) +
                                                                        TilemapBuildTimings.ListBytes(m_OutlineEntries) +
                                                                        TilemapBuildTimings.HashBytes<TileKey, int>(m_OutlineEntryIds.Capacity));
        }

        public void Dispose()
        {
            if (m_Polygons.IsCreated)
                m_Polygons.Dispose();

            if (m_PolygonRanges.IsCreated)
                m_PolygonRanges.Dispose();

            if (m_UnclippedPolygonRanges.IsCreated)
                m_UnclippedPolygonRanges.Dispose();

            if (m_SegmentVertices.IsCreated)
                m_SegmentVertices.Dispose();

            if (m_SegmentPieces.IsCreated)
                m_SegmentPieces.Dispose();

            if (m_SegmentEntries.IsCreated)
                m_SegmentEntries.Dispose();

            if (m_SegmentEntryIds.IsCreated)
                m_SegmentEntryIds.Dispose();

            if (m_OutlineVertices.IsCreated)
                m_OutlineVertices.Dispose();

            if (m_OutlineContours.IsCreated)
                m_OutlineContours.Dispose();

            if (m_OutlineEntries.IsCreated)
                m_OutlineEntries.Dispose();

            if (m_OutlineEntryIds.IsCreated)
                m_OutlineEntryIds.Dispose();

            if (m_StackedEntryIds.IsCreated)
                m_StackedEntryIds.Dispose();

            if (m_StackedPolygonRanges.IsCreated)
                m_StackedPolygonRanges.Dispose();
        }

        // The key for a cell's content already read. False when the cell contributes nothing.
        // A Grid cell has no sprite, which is what tells GetPolygonRange to shape the cell itself.
        private static bool TryGetKey(in TilemapCellReader.Content content, out TileKey key)
        {
            key = default;

            switch (content.colliderType)
            {
                case Tile.ColliderType.None:
                    return false;

                case Tile.ColliderType.Grid:
                    key = TileKey.grid;
                    return true;

                default:
                    if (ReferenceEquals(content.sprite, null))
                        return false;

                    key = new TileKey(content.sprite.GetEntityId(), content.tileMatrix);
                    return true;
            }
        }

        // The cell shape is already convex and already in cell space, so it is cached as it is.
        private int2 AddGridPolygons()
        {
            var start = m_Polygons.Length;
            TilemapCellClipping.AddPolygons(m_CellShape, MinimumArea, m_Polygons);

            return new int2(start, m_Polygons.Length - start);
        }

        // Place each polygon of the sprite's convex decomposition about the cell's center, by the tile's own transform, then clip it to the cell.
        // A convex polygon clipped to a convex cell stays convex, so each one stays a single polygon unless clipping gave it more vertices than a polygon can hold.
        private int2 AddSpritePolygons(Sprite sprite, Matrix4x4 tileMatrix)
        {
            var start = m_Polygons.Length;
            var placement = TilemapCellTransforms.GetSpriteCellMatrix(m_CellCenterOffset, m_Orientation, tileMatrix);

            var source = PhysicsSpriteCache.GetPolygons(sprite, PhysicsConstants.MaxPolygonVertices, useDelaunay: true, out _);
            if (source.Length == 0)
                return new int2(start, 0);

            var outline = new NativeList<Vector2>(PhysicsConstants.MaxPolygonVertices, Allocator.Temp);
            var clipped = new NativeList<Vector2>(PhysicsConstants.MaxPolygonVertices * 2, Allocator.Temp);
            var scratch = new NativeList<Vector2>(PhysicsConstants.MaxPolygonVertices * 2, Allocator.Temp);

            for (var i = 0; i < source.Length; ++i)
            {
                var vertices = source[i].AsReadOnlySpan();

                outline.Clear();
                for (var v = 0; v < vertices.Length; ++v)
                    outline.Add(placement.MultiplyPoint3x4(vertices[v]));

                TilemapCellClipping.Clip(outline.AsArray().AsReadOnlySpan(), m_CellShape, clipped, scratch);
                TilemapCellClipping.SnapToCellEdges(clipped, m_CellShape, m_Tolerance);
                TilemapCellClipping.AddPolygons(clipped.AsArray().AsReadOnlySpan(), MinimumArea, m_Polygons);
            }

            scratch.Dispose();
            clipped.Dispose();
            outline.Dispose();

            return new int2(start, m_Polygons.Length - start);
        }

        // Place each polygon of the sprite's convex decomposition about the cell's center, by the tile's own transform, without clipping it.
        private int2 AddUnclippedSpritePolygons(Sprite sprite, Matrix4x4 tileMatrix)
        {
            var start = m_Polygons.Length;
            var placement = TilemapCellTransforms.GetSpriteCellMatrix(m_CellCenterOffset, m_Orientation, tileMatrix);

            var source = PhysicsSpriteCache.GetPolygons(sprite, PhysicsConstants.MaxPolygonVertices, useDelaunay: true, out _);
            for (var i = 0; i < source.Length; ++i)
                m_Polygons.Add(source[i].Transform(placement, scaleRadius: false));

            return new int2(start, m_Polygons.Length - start);
        }

        // The cell shape as an outline: one piece per cell edge, each lying along that edge.
        private SegmentEntry AddGridSegments()
        {
            var vertexStart = m_SegmentVertices.Length;
            var pieceStart = m_SegmentPieces.Length;

            for (var edge = 0; edge < m_CellShape.Length; ++edge)
            {
                m_SegmentVertices.Add(m_CellShape[edge]);
                m_SegmentPieces.Add(new SegmentPiece(edge, (edge + 1) % m_CellShape.Length, edge));
            }

            return DescribeEntry(vertexStart, m_CellShape.Length, pieceStart, m_CellShape.Length);
        }

        // Place the sprite's outlines at the cell, wind them so the solid is on their left (after placing, since a flipped tile reverses them),
        // clip them to the cell, then cancel what clipping doubled along the cell edges.
        private SegmentEntry AddSpriteSegments(Sprite sprite, Matrix4x4 tileMatrix)
        {
            var vertexStart = m_SegmentVertices.Length;
            var pieceStart = m_SegmentPieces.Length;

            var group = PhysicsSpriteCache.GetContourGroup(sprite);
            if (group == null || !group.isValid)
                return new SegmentEntry(vertexStart, 0, pieceStart, 0, 0, false);

            var placement = TilemapCellTransforms.GetSpriteCellMatrix(m_CellCenterOffset, m_Orientation, tileMatrix);
            var placed = new NativeList<Vector2>(64, Allocator.Temp);
            var ranges = new NativeList<int2>(group.contourCount, Allocator.Temp);

            for (var c = 0; c < group.contourCount; ++c)
            {
                var contour = group[c];
                if (!contour.isValid)
                    continue;

                var start = placed.Length;
                for (var v = 0; v < contour.vertexCount; ++v)
                    placed.Add(placement.MultiplyPoint3x4(contour[v]));

                ranges.Add(new int2(start, contour.vertexCount));
            }

            var entry = AddClippedOutlines(placed, ranges, vertexStart, pieceStart);

            ranges.Dispose();
            placed.Dispose();

            return entry;
        }

        // The outline of the union of a run of polygons already clipped to the cell, as an outline entry.
        // The composer joins the polygons into closed outlines, which are then shaped the same as a sprite's.
        private SegmentEntry AddStackedSegments(int2 polygonRange)
        {
            var vertexStart = m_SegmentVertices.Length;
            var pieceStart = m_SegmentPieces.Length;

            var placed = new NativeList<Vector2>(64, Allocator.Temp);
            var ranges = new NativeList<int2>(8, Allocator.Temp);

            using (var composer = PhysicsComposer.Create(Allocator.Temp))
            {
                // Each polygon is a layer of its own, so polygons of different tiles that overlap are joined rather than kept as separate solids.
                var polygons = m_Polygons.AsArray().AsReadOnlySpan();
                for (var p = 0; p < polygonRange.y; ++p)
                    composer.AddLayer(polygons.Slice(polygonRange.x + p, 1), PhysicsTransform.identity, PhysicsComposer.Operation.OR, p, PhysicsComposer.DefaultCurveStride, false);

                var chains = composer.CreateChainGeometry(out var chainVertices, Vector2.one);

                if (chains.IsCreated)
                {
                    for (var c = 0; c < chains.Length; ++c)
                    {
                        var vertices = chains[c].vertices;
                        var start = placed.Length;

                        for (var v = 0; v < vertices.Length; ++v)
                            placed.Add(vertices[v]);

                        ranges.Add(new int2(start, vertices.Length));
                    }

                    chains.Dispose();
                }

                if (chainVertices.IsCreated)
                    chainVertices.Dispose();
            }

            var entry = AddClippedOutlines(placed, ranges, vertexStart, pieceStart);

            ranges.Dispose();
            placed.Dispose();

            return entry;
        }

        // Wind outlines so the solid is on their left, clip them to the cell, then cancel what clipping doubled along the cell edges, as an outline entry.
        private SegmentEntry AddClippedOutlines(NativeList<Vector2> placed, NativeList<int2> ranges, int vertexStart, int pieceStart)
        {
            OrientContours(placed, ranges);

            var clipped = new NativeList<Vector2>(64, Allocator.Temp);
            var scratch = new NativeList<Vector2>(64, Allocator.Temp);

            for (var c = 0; c < ranges.Length; ++c)
            {
                TilemapCellClipping.Clip(placed.AsArray().AsReadOnlySpan().Slice(ranges[c].x, ranges[c].y), m_CellShape, clipped, scratch);
                TilemapCellClipping.SnapToCellEdges(clipped, m_CellShape, m_Tolerance);
                AddOutlinePieces(clipped, vertexStart);
            }

            scratch.Dispose();
            clipped.Dispose();

            CancelDoubledBorderPieces(vertexStart, pieceStart);

            return DescribeEntry(vertexStart, m_SegmentVertices.Length - vertexStart, pieceStart, m_SegmentPieces.Length - pieceStart);
        }

        // An entry over the given ranges, noting which cell edges it fills and whether any of its pieces run through the cell.
        // An edge counts as filled when exactly one piece lies along it, running the same way as the edge, with each end within the tolerance of the edge's corner.
        // Joining moves such ends onto the corners, so an edge both neighbours fill cancels. An edge shorter than the join reach never counts, since its ends could join to the wrong corner.
        private SegmentEntry DescribeEntry(int vertexStart, int vertexCount, int pieceStart, int pieceCount)
        {
            var fullEdges = 0;
            var hasThroughPieces = false;

            for (var p = pieceStart; p < pieceStart + pieceCount; ++p)
                hasThroughPieces |= m_SegmentPieces[p].edge < 0;

            for (var edge = 0; edge < m_CellShape.Length; ++edge)
            {
                var length = EdgeLength(edge);
                if (length <= m_Tolerance * 4f)
                    continue;

                var along = 0;
                var fills = false;

                for (var p = pieceStart; p < pieceStart + pieceCount; ++p)
                {
                    var piece = m_SegmentPieces[p];
                    if (piece.edge != edge)
                        continue;

                    ++along;
                    var from = EdgeParameter(m_SegmentVertices[vertexStart + piece.start], edge);
                    var to = EdgeParameter(m_SegmentVertices[vertexStart + piece.end], edge);
                    fills = Mathf.Abs(from) <= m_Tolerance && Mathf.Abs(to - length) <= m_Tolerance;
                }

                if (along == 1 && fills)
                    fullEdges |= 1 << edge;
            }

            return new SegmentEntry(vertexStart, vertexCount, pieceStart, pieceCount, fullEdges, hasThroughPieces);
        }

        // The cell shape as an unclipped outline: one contour, already wound anti-clockwise.
        private OutlineEntry AddGridOutline()
        {
            var contourStart = m_OutlineContours.Length;
            var vertexStart = m_OutlineVertices.Length;

            foreach (var point in m_CellShape)
                m_OutlineVertices.Add(point);

            m_OutlineContours.Add(new int2(vertexStart, m_CellShape.Length));
            return DescribeOutline(contourStart);
        }

        // Place the sprite's outlines about the cell's center by the tile's own transform and wind them so the solid is on their left, without clipping them.
        // Vertices closer together than the matching distance are dropped, and a contour left with fewer than three vertices encloses nothing, so it is dropped too.
        private OutlineEntry AddSpriteOutline(Sprite sprite, Matrix4x4 tileMatrix)
        {
            var contourStart = m_OutlineContours.Length;

            var group = PhysicsSpriteCache.GetContourGroup(sprite);
            if (group == null || !group.isValid)
                return DescribeOutline(contourStart);

            var placement = TilemapCellTransforms.GetSpriteCellMatrix(m_CellCenterOffset, m_Orientation, tileMatrix);
            var placed = new NativeList<Vector2>(64, Allocator.Temp);
            var ranges = new NativeList<int2>(group.contourCount, Allocator.Temp);

            for (var c = 0; c < group.contourCount; ++c)
            {
                var contour = group[c];
                if (!contour.isValid)
                    continue;

                var start = placed.Length;
                for (var v = 0; v < contour.vertexCount; ++v)
                    placed.Add(placement.MultiplyPoint3x4(contour[v]));

                ranges.Add(new int2(start, contour.vertexCount));
            }

            OrientContours(placed, ranges);

            var epsilonSquared = m_EdgeEpsilon * m_EdgeEpsilon;
            foreach (var range in ranges)
            {
                var vertexStart = m_OutlineVertices.Length;
                for (var v = 0; v < range.y; ++v)
                {
                    var point = (float2)placed[range.x + v];
                    if (m_OutlineVertices.Length > vertexStart && math.distancesq(point, m_OutlineVertices[m_OutlineVertices.Length - 1]) <= epsilonSquared)
                        continue;

                    m_OutlineVertices.Add(point);
                }

                var count = m_OutlineVertices.Length - vertexStart;
                if (count >= 2 && math.distancesq(m_OutlineVertices[vertexStart], m_OutlineVertices[m_OutlineVertices.Length - 1]) <= epsilonSquared)
                    --count;

                if (count < 3)
                {
                    m_OutlineVertices.Resize(vertexStart, NativeArrayOptions.UninitializedMemory);
                    continue;
                }

                m_OutlineVertices.Resize(vertexStart + count, NativeArrayOptions.UninitializedMemory);
                m_OutlineContours.Add(new int2(vertexStart, count));
            }

            ranges.Dispose();
            placed.Dispose();

            return DescribeOutline(contourStart);
        }

        // An outline entry over the contours added since the given one, with the bounds of all their vertices.
        private OutlineEntry DescribeOutline(int contourStart)
        {
            var contourCount = m_OutlineContours.Length - contourStart;
            var min = new float2(float.MaxValue, float.MaxValue);
            var max = new float2(float.MinValue, float.MinValue);

            for (var c = contourStart; c < m_OutlineContours.Length; ++c)
            {
                var contour = m_OutlineContours[c];
                for (var v = contour.x; v < contour.x + contour.y; ++v)
                {
                    min = math.min(min, m_OutlineVertices[v]);
                    max = math.max(max, m_OutlineVertices[v]);
                }
            }

            return contourCount > 0 ? new OutlineEntry(contourStart, contourCount, min, max) : new OutlineEntry(contourStart, 0, float2.zero, float2.zero);
        }

        // Wind every outline so the solid is on its left: an outline inside an even number of the others is an outer one and runs anti-clockwise, and one inside an odd number is a hole and runs clockwise.
        private static void OrientContours(NativeList<Vector2> placed, NativeList<int2> ranges)
        {
            for (var c = 0; c < ranges.Length; ++c)
            {
                var range = ranges[c];
                var first = placed[range.x];

                var containing = 0;
                for (var other = 0; other < ranges.Length; ++other)
                {
                    if (other != c && Contains(placed, ranges[other], first))
                        ++containing;
                }

                var wantAntiClockwise = (containing & 1) == 0;
                if (SignedArea(placed, range) > 0f == wantAntiClockwise)
                    continue;

                for (int low = range.x, high = range.x + range.y - 1; low < high; ++low, --high)
                    (placed[low], placed[high]) = (placed[high], placed[low]);
            }
        }

        // Add one clipped outline as pieces from each vertex to the next, dropping any of no length, and tag each piece with the cell edge it lies along.
        private void AddOutlinePieces(NativeList<Vector2> outline, int entryVertexStart)
        {
            if (outline.Length < 2)
                return;

            var start = m_SegmentVertices.Length;
            for (var i = 0; i < outline.Length; ++i)
            {
                var previous = outline[(i + outline.Length - 1) % outline.Length];
                if ((outline[i] - previous).sqrMagnitude > m_EdgeEpsilon * m_EdgeEpsilon || i == 0)
                    m_SegmentVertices.Add(outline[i]);
            }

            var count = m_SegmentVertices.Length - start;
            if (count >= 2 && (m_SegmentVertices[start] - m_SegmentVertices[start + count - 1]).sqrMagnitude <= m_EdgeEpsilon * m_EdgeEpsilon)
            {
                m_SegmentVertices.RemoveAt(start + count - 1);
                --count;
            }

            if (count < 2)
            {
                m_SegmentVertices.Resize(start, NativeArrayOptions.UninitializedMemory);
                return;
            }

            for (var i = 0; i < count; ++i)
            {
                var from = start + i;
                var to = start + (i + 1) % count;
                m_SegmentPieces.Add(new SegmentPiece(from - entryVertexStart, to - entryVertexStart, EdgeOf(m_SegmentVertices[from], m_SegmentVertices[to])));
            }
        }

        // Where a concave outline leaves the cell and comes back, clipping runs it along the cell edge and back again over the same stretch.
        // Those two runs face opposite ways, so wherever both cover a stretch it is inside the solid and is removed; a stretch covered by one alone is kept.
        // A cell edge with no piece running against the edge has nothing doubled, which is the usual case, and is left as it is.
        private void CancelDoubledBorderPieces(int vertexStart, int pieceStart)
        {
            for (var edge = 0; edge < m_CellShape.Length; ++edge)
            {
                var hasBackward = false;
                for (var p = pieceStart; p < m_SegmentPieces.Length; ++p)
                {
                    var piece = m_SegmentPieces[p];
                    if (piece.edge == edge && !IsForward(piece, vertexStart, edge))
                    {
                        hasBackward = true;
                        break;
                    }
                }

                if (hasBackward)
                    SweepEdge(edge, vertexStart, pieceStart);
            }
        }

        // Replace the pieces along one cell edge with the stretches covered by one facing only, each stretch as a piece of its own.
        private void SweepEdge(int edge, int vertexStart, int pieceStart)
        {
            var events = new NativeList<float2>(16, Allocator.Temp);
            var forward = new NativeList<float2>(8, Allocator.Temp);
            var backward = new NativeList<float2>(8, Allocator.Temp);

            for (var p = m_SegmentPieces.Length - 1; p >= pieceStart; --p)
            {
                var piece = m_SegmentPieces[p];
                if (piece.edge != edge)
                    continue;

                var from = EdgeParameter(m_SegmentVertices[vertexStart + piece.start], edge);
                var to = EdgeParameter(m_SegmentVertices[vertexStart + piece.end], edge);

                if (to > from)
                    forward.Add(new float2(from, to));
                else
                    backward.Add(new float2(to, from));

                events.Add(new float2(from, 0f));
                events.Add(new float2(to, 0f));
                m_SegmentPieces.RemoveAt(p);
            }

            events.Sort(new EventComparer());

            for (var i = 0; i + 1 < events.Length; ++i)
            {
                var low = events[i].x;
                var high = events[i + 1].x;
                if (high - low <= m_EdgeEpsilon)
                    continue;

                var middle = (low + high) * 0.5f;
                var isForward = Covers(forward, middle);
                var isBackward = Covers(backward, middle);
                if (isForward == isBackward)
                    continue;

                var lowVertex = FindOrAddEdgeVertex(edge, low, vertexStart);
                var highVertex = FindOrAddEdgeVertex(edge, high, vertexStart);

                m_SegmentPieces.Add(isForward
                    ? new SegmentPiece(lowVertex, highVertex, edge)
                    : new SegmentPiece(highVertex, lowVertex, edge));
            }

            backward.Dispose();
            forward.Dispose();
            events.Dispose();
        }

        // The entry's vertex at a distance along a cell edge, added when the entry has none there yet. Returns its index within the entry.
        private int FindOrAddEdgeVertex(int edge, float parameter, int vertexStart)
        {
            for (var v = vertexStart; v < m_SegmentVertices.Length; ++v)
            {
                var vertex = m_SegmentVertices[v];
                if (IsOnEdge(vertex, edge) && Mathf.Abs(EdgeParameter(vertex, edge) - parameter) <= m_EdgeEpsilon)
                    return v - vertexStart;
            }

            m_SegmentVertices.Add(EdgePoint(edge, parameter));
            return m_SegmentVertices.Length - 1 - vertexStart;
        }

        // Whether a piece along a cell edge runs the same way as the edge, which is the way an outline bounding solid inside the cell runs.
        private bool IsForward(SegmentPiece piece, int vertexStart, int edge) =>
            EdgeParameter(m_SegmentVertices[vertexStart + piece.end], edge) > EdgeParameter(m_SegmentVertices[vertexStart + piece.start], edge);

        // The cell edge a piece lies along, or -1 when it runs through the cell.
        private int EdgeOf(Vector2 from, Vector2 to)
        {
            for (var edge = 0; edge < m_CellShape.Length; ++edge)
            {
                if (IsOnEdge(from, edge) && IsOnEdge(to, edge))
                    return edge;
            }

            return -1;
        }

        // Whether a point lies on a cell edge, within the rounding clipping and snapping leave.
        private bool IsOnEdge(Vector2 point, int edge)
        {
            var direction = EdgeDirection(edge);
            var offset = point - m_CellShape[edge];
            var across = direction.x * offset.y - direction.y * offset.x;
            var along = Vector2.Dot(offset, direction);

            return Mathf.Abs(across) <= m_EdgeEpsilon && along >= -m_EdgeEpsilon && along <= EdgeLength(edge) + m_EdgeEpsilon;
        }

        // The unit direction a cell edge runs in, anti-clockwise around the cell.
        private Vector2 EdgeDirection(int edge) => m_EdgeDirections[edge];

        // The largest distance from a cell's position to a vertex of its cell shape, used to scale the matching distances.
        private float CellSize()
        {
            var size = 1f;
            if (m_CellShape == null)
                return size;

            foreach (var point in m_CellShape)
                size = Mathf.Max(size, point.magnitude);

            return size;
        }

        // Whether any of the intervals covers a distance.
        private static bool Covers(NativeList<float2> intervals, float parameter)
        {
            foreach (var interval in intervals)
            {
                if (parameter > interval.x && parameter < interval.y)
                    return true;
            }

            return false;
        }

        // Crossing-number test of a point against one outline.
        private static bool Contains(NativeList<Vector2> placed, int2 range, Vector2 point)
        {
            var inside = false;

            for (var i = 0; i < range.y; ++i)
            {
                var a = placed[range.x + i];
                var b = placed[range.x + (i + 1) % range.y];

                if ((a.y > point.y) != (b.y > point.y) && point.x < a.x + (point.y - a.y) / (b.y - a.y) * (b.x - a.x))
                    inside = !inside;
            }

            return inside;
        }

        // Twice the enclosed area of one outline, signed: positive is anti-clockwise.
        private static float SignedArea(NativeList<Vector2> placed, int2 range)
        {
            var area = 0f;

            for (var i = 0; i < range.y; ++i)
            {
                var a = placed[range.x + i];
                var b = placed[range.x + (i + 1) % range.y];
                area += a.x * b.y - b.x * a.y;
            }

            return area;
        }

        // The smallest area a clipped polygon can enclose and still be kept, the square of the tolerance: anything smaller is a sliver clipping left behind.
        private float MinimumArea => m_Tolerance * m_Tolerance;

        // Every polygon entry's polygons, in cell space, for Polygons output to read directly. Find a cell's with GetPolygonRange.
        public NativeArray<PolygonGeometry> polygons => m_Polygons.AsArray();

        // Every entry's outline vertices, pieces and ranges, for Segments output to read directly.
        public NativeArray<Vector2> segmentVertices => m_SegmentVertices.AsArray();
        public NativeArray<SegmentPiece> segmentPieces => m_SegmentPieces.AsArray();
        public NativeArray<SegmentEntry> segmentEntries => m_SegmentEntries.AsArray();

        // Every unclipped outline entry, its contours as a start and a count of vertices, and the vertices, for unclipped Segments output to read directly.
        public NativeArray<float2> outlineVertices => m_OutlineVertices.AsArray();
        public NativeArray<int2> outlineContours => m_OutlineContours.AsArray();
        public NativeArray<OutlineEntry> outlineEntries => m_OutlineEntries.AsArray();

        // The cell shape the entries were clipped against, wound anti-clockwise, in cell space.
        public Vector2[] cellShape => m_CellShape;

        // How close two distances along a cell edge, or two points, have to be to count as the same once clipping and snapping have rounded them.
        public float edgeEpsilon => m_EdgeEpsilon;

        // The tolerance the entries were shaped with, which also decides how near two neighboring cells' points must be to be joined.
        public float tolerance => m_Tolerance;

        // One clipped tile outline for Segments output: where its vertices and its pieces are held, which cell edges it fills, and whether any piece runs through the cell.
        // Full edges holds one bit per cell edge, set when the outline fills that edge with a single piece.
        internal readonly struct SegmentEntry
        {
            public SegmentEntry(int vertexStart, int vertexCount, int pieceStart, int pieceCount, int fullEdges, bool hasThroughPieces)
            {
                this.vertexStart = vertexStart;
                this.vertexCount = vertexCount;
                this.pieceStart = pieceStart;
                this.pieceCount = pieceCount;
                this.fullEdges = fullEdges;
                this.hasThroughPieces = hasThroughPieces;
            }

            public readonly int vertexStart;
            public readonly int vertexCount;
            public readonly int pieceStart;
            public readonly int pieceCount;
            public readonly int fullEdges;
            public readonly bool hasThroughPieces;
        }

        // One unclipped tile outline: its run of contours, and the bounds of its vertices, all in cell space.
        internal readonly struct OutlineEntry
        {
            public OutlineEntry(int contourStart, int contourCount, float2 boundsMin, float2 boundsMax)
            {
                this.contourStart = contourStart;
                this.contourCount = contourCount;
                this.boundsMin = boundsMin;
                this.boundsMax = boundsMax;
            }

            public readonly int contourStart;
            public readonly int contourCount;
            public readonly float2 boundsMin;
            public readonly float2 boundsMax;
        }

        // One piece of a clipped outline, from one of its entry's vertices to another, with the solid on its left.
        // Edge is the cell edge the piece lies along, or -1 when it runs through the cell.
        internal readonly struct SegmentPiece
        {
            public SegmentPiece(int start, int end, int edge)
            {
                this.start = start;
                this.end = end;
                this.edge = edge;
            }

            public readonly int start;
            public readonly int end;
            public readonly int edge;
        }

        // What a tile's shaping depends on: its sprite and its transform, or that it is a Grid cell.
        private readonly struct TileKey : IEquatable<TileKey>
        {
            public TileKey(EntityId sprite, Matrix4x4 tileMatrix) : this(sprite, tileMatrix, false)
            {
            }

            private TileKey(EntityId sprite, Matrix4x4 tileMatrix, bool isGrid)
            {
                this.sprite = sprite;
                this.tileMatrix = tileMatrix;
                this.isGrid = isGrid;
            }

            public bool Equals(TileKey other) => isGrid == other.isGrid && sprite.Equals(other.sprite) && tileMatrix.Equals(other.tileMatrix);

            public override bool Equals(object other) => other is TileKey key && Equals(key);

            // Mixed, because a tile matrix is mostly whole numbers whose hashes vary only in their high bits, and a bucket is chosen from the low ones.
            public override int GetHashCode() => TilemapCellKey.Mix((uint)sprite.GetHashCode() * 397u ^ (uint)tileMatrix.GetHashCode() ^ (isGrid ? 1u : 0u));

            // The key every Grid-type cell shares.
            public static TileKey grid => new(default, Matrix4x4.identity, true);

            public readonly EntityId sprite;
            public readonly Matrix4x4 tileMatrix;
            public readonly bool isGrid;
        }

        // Orders sweep events by their distance along the edge.
        private struct EventComparer : System.Collections.Generic.IComparer<float2>
        {
            public int Compare(float2 x, float2 y) => x.x.CompareTo(y.x);
        }

        #region Internal

        // How many distinct tile contents a map holds, as a start, before the cache has to grow.
        private const int InitialCapacity = 64;

        // How close two points must be to count as the same, as a fraction of the cell's size.
        private const float EdgeEpsilonFraction = 1e-5f;

        // Every polygon entry's polygons, end to end, and where each entry's run of them starts and how long it is, clipped and unclipped.
        // Grid cells are the cell shape either way, so they are only ever kept with the clipped ones.
        private NativeList<PolygonGeometry> m_Polygons;
        private NativeHashMap<TileKey, int2> m_PolygonRanges;
        private NativeHashMap<TileKey, int2> m_UnclippedPolygonRanges;

        // Every outline entry's vertices and pieces, end to end, each entry's ranges, and which entry a tile content has.
        private NativeList<Vector2> m_SegmentVertices;
        private NativeList<SegmentPiece> m_SegmentPieces;
        private NativeList<SegmentEntry> m_SegmentEntries;
        private NativeHashMap<TileKey, int> m_SegmentEntryIds;

        // Every unclipped outline entry's vertices and contours, end to end, each entry, and which entry a tile content has.
        private NativeList<float2> m_OutlineVertices;
        private NativeList<int2> m_OutlineContours;
        private NativeList<OutlineEntry> m_OutlineEntries;
        private NativeHashMap<TileKey, int> m_OutlineEntryIds;

        // The outline entry of each pair of entries stacked in one cell, keyed by the pair, and the clipped polygons of each entry that has been stacked, whose union is the stack's outline.
        private NativeHashMap<int2, int> m_StackedEntryIds;
        private NativeHashMap<int, int2> m_StackedPolygonRanges;

        // The layout the entries were clipped against, taken by Reset.
        private Vector2[] m_CellShape;
        private Vector2 m_CellCenterOffset;
        private Matrix4x4 m_Orientation;
        private float m_Tolerance;
        private float m_EdgeEpsilon;
        private Vector2[] m_EdgeDirections;
        private float[] m_EdgeLengths;

        #endregion
    }
}
