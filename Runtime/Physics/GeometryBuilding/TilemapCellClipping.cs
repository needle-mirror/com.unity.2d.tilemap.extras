using System;

using Unity.Collections;
using UnityEngine;

namespace Unity.U2D.Physics
{
    // Trims a tile's outline to its own cell, so a cell's shapes never reach into a neighbouring cell.
    // Every cell shape a Grid produces is convex, so an outline is clipped against one edge of it at a time and what is left is exactly the part inside the cell.
    // Outlines that stop just short of an edge are pulled onto it, so neighbouring cells meet exactly rather than leaving a hairline gap between them.
    internal static class TilemapCellClipping
    {
        // Clip an outline to the cell shape, leaving only the part inside the cell (the Sutherland–Hodgman method).
        // The cell shape is wound anti-clockwise, as TilemapCellTransforms.GetGridCellShape returns it, and both are in the same space.
        public static void Clip(ReadOnlySpan<Vector2> outline, ReadOnlySpan<Vector2> cellShape, NativeList<Vector2> clipped, NativeList<Vector2> scratch)
        {
            clipped.Clear();

            for (var i = 0; i < outline.Length; ++i)
                clipped.Add(outline[i]);

            for (var edge = 0; edge < cellShape.Length && clipped.Length > 0; ++edge)
            {
                var edgeStart = cellShape[edge];
                var edgeEnd = cellShape[(edge + 1) % cellShape.Length];

                scratch.Clear();
                scratch.AddRange(clipped.AsArray());
                clipped.Clear();

                for (var i = 0; i < scratch.Length; ++i)
                {
                    var current = scratch[i];
                    var previous = scratch[(i + scratch.Length - 1) % scratch.Length];

                    var currentSide = Side(edgeStart, edgeEnd, current);
                    var previousSide = Side(edgeStart, edgeEnd, previous);

                    // Crossing the edge in either direction adds the crossing point; a vertex inside the edge is kept.
                    if ((currentSide >= 0f) != (previousSide >= 0f))
                        clipped.Add(previous + (current - previous) * (previousSide / (previousSide - currentSide)));

                    if (currentSide >= 0f)
                        clipped.Add(current);
                }
            }
        }

        // Move vertices within the tolerance of a cell corner onto it, and others within the tolerance of an edge onto that edge, so tiles traced slightly inside their sprite still meet.
        // Corners first, since a vertex near a corner is near two edges.
        public static void SnapToCellEdges(NativeList<Vector2> vertices, ReadOnlySpan<Vector2> cellShape, float tolerance)
        {
            for (var i = 0; i < vertices.Length; ++i)
            {
                var vertex = vertices[i];
                var snapped = false;

                for (var corner = 0; corner < cellShape.Length; ++corner)
                {
                    if ((vertex - cellShape[corner]).sqrMagnitude >= tolerance * tolerance)
                        continue;

                    vertex = cellShape[corner];
                    snapped = true;
                    break;
                }

                for (var edge = 0; edge < cellShape.Length && !snapped; ++edge)
                {
                    var edgeStart = cellShape[edge];
                    var edgeVector = cellShape[(edge + 1) % cellShape.Length] - edgeStart;
                    var length = edgeVector.magnitude;
                    if (length <= 0f)
                        continue;

                    var direction = edgeVector / length;
                    var along = Vector2.Dot(vertex - edgeStart, direction);
                    var across = direction.x * (vertex.y - edgeStart.y) - direction.y * (vertex.x - edgeStart.x);

                    if (Mathf.Abs(across) >= tolerance || along < -tolerance || along > length + tolerance)
                        continue;

                    vertex = edgeStart + direction * Mathf.Clamp(along, 0f, length);
                    break;
                }

                vertices[i] = vertex;
            }
        }

        // Add one clipped convex outline as polygons. Slivers below the minimum area are dropped, and an outline with too many vertices is split in two along its balancing diagonal.
        public static void AddPolygons(ReadOnlySpan<Vector2> vertices, float minimumArea, NativeList<PolygonGeometry> polygons)
        {
            if (vertices.Length < 3 || Mathf.Abs(SignedArea(vertices)) * 0.5f < minimumArea)
                return;

            if (vertices.Length <= PhysicsConstants.MaxPolygonVertices)
            {
                var polygon = PolygonGeometry.Create(vertices);
                if (polygon.isValid)
                    polygons.Add(polygon);

                return;
            }

            // The diagonal from the first vertex to the middle one shares both of its ends between the halves.
            var middle = vertices.Length / 2;
            AddPolygons(vertices.Slice(0, middle + 1), minimumArea, polygons);

            var secondHalf = new NativeArray<Vector2>(vertices.Length - middle + 1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = middle; i < vertices.Length; ++i)
                secondHalf[i - middle] = vertices[i];

            secondHalf[secondHalf.Length - 1] = vertices[0];
            AddPolygons(secondHalf.AsReadOnlySpan(), minimumArea, polygons);
            secondHalf.Dispose();
        }

        // Which side of the line through an edge a point lies on: positive to the left, which is inside an anti-clockwise cell.
        private static float Side(Vector2 edgeStart, Vector2 edgeEnd, Vector2 point) =>
            (edgeEnd.x - edgeStart.x) * (point.y - edgeStart.y) - (edgeEnd.y - edgeStart.y) * (point.x - edgeStart.x);

        // Twice the enclosed area, signed: positive is anti-clockwise.
        private static float SignedArea(ReadOnlySpan<Vector2> vertices)
        {
            var area = 0f;

            for (var i = 0; i < vertices.Length; ++i)
            {
                var next = vertices[(i + 1) % vertices.Length];
                area += vertices[i].x * next.y - next.x * vertices[i].y;
            }

            return area;
        }
    }
}
