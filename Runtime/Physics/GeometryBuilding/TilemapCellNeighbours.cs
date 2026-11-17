using System;

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.U2D.Physics
{
    // Which cells share each edge and corner of a cell, worked out from the Grid's geometry.
    // Hexagon rows are offset, so the tables are kept per parity of column and row.
    internal sealed class TilemapCellNeighbours
    {
        // Work out the tables for a map's layout. Edges no cell shares, as with a Cell Gap, are left with no neighbour.
        public TilemapCellNeighbours(Tilemap map, Vector2[] cellShape)
        {
            m_EdgeCount = cellShape.Length;
            m_CellShape = (Vector2[])cellShape.Clone();

            // A cell shape vertex is placed by an addition, so two cells' copies of a shared vertex differ by rounding only.
            var size = 0f;
            foreach (var point in cellShape)
                size = Mathf.Max(size, point.magnitude);

            var epsilon = Mathf.Max(size, 1f) * MatchFraction;

            for (var parity = 0; parity < ParityCount; ++parity)
            {
                var probe = new Vector3Int(parity & 1, parity >> 1, 0);
                var origin = (Vector2)map.CellToLocal(probe);
                m_ProbeOrigins[parity] = origin;

                m_EdgeNeighbours[parity] = new EdgeNeighbour[m_EdgeCount];
                m_CornerNeighbours[parity] = new Vector3Int[m_EdgeCount][];

                for (var edge = 0; edge < m_EdgeCount; ++edge)
                {
                    var start = origin + cellShape[edge];
                    var end = origin + cellShape[(edge + 1) % m_EdgeCount];

                    m_EdgeNeighbours[parity][edge] = FindEdgeNeighbour(map, cellShape, probe, start, end, epsilon);
                    m_CornerNeighbours[parity][edge] = FindCornerNeighbours(map, cellShape, probe, start, epsilon);
                }
            }
        }

        // Whether these tables were worked out for the same cell shape and the same cell positions, so a build can keep them rather than allocate new ones.
        // The cell positions of one cell of each parity pin down where every cell sits, since every layout repeats with that period.
        public bool IsFor(Tilemap map, Vector2[] cellShape)
        {
            if (cellShape == null || cellShape.Length != m_CellShape.Length)
                return false;

            for (var i = 0; i < cellShape.Length; ++i)
            {
                if (cellShape[i] != m_CellShape[i])
                    return false;
            }

            for (var parity = 0; parity < ParityCount; ++parity)
            {
                if ((Vector2)map.CellToLocal(new Vector3Int(parity & 1, parity >> 1, 0)) != m_ProbeOrigins[parity])
                    return false;
            }

            return true;
        }

        // How many edges, and so how many corners, a cell has.
        public int edgeCount => m_EdgeCount;

        // The cell across an edge of a cell, and which of its own edges that is. False when no cell shares the edge.
        public bool TryGetEdgeNeighbour(Vector3Int cell, int edge, out Vector3Int neighbour, out int neighbourEdge)
        {
            var entry = m_EdgeNeighbours[Parity(cell)][edge];

            neighbour = cell + entry.offset;
            neighbourEdge = entry.edge;
            return entry.edge >= 0;
        }

        // Every other cell meeting at a corner of a cell, the cells across its two edges included. A corner is the vertex an edge starts from.
        public ReadOnlySpan<Vector3Int> GetCornerOffsets(Vector3Int cell, int corner) => m_CornerNeighbours[Parity(cell)][corner];

        // Write the tables out flat, for a job to read.
        // Both are indexed by a cell's parity times the edge count plus the edge or corner: the edge neighbour as an offset and its edge, and the run of corner offsets.
        public void WriteTables(NativeArray<int4> edgeNeighbours, NativeList<int3> cornerOffsets, NativeArray<int2> cornerRanges)
        {
            for (var parity = 0; parity < ParityCount; ++parity)
            {
                for (var edge = 0; edge < m_EdgeCount; ++edge)
                {
                    var index = parity * m_EdgeCount + edge;
                    var neighbour = m_EdgeNeighbours[parity][edge];
                    edgeNeighbours[index] = new int4(neighbour.offset.x, neighbour.offset.y, neighbour.offset.z, neighbour.edge);

                    var corners = m_CornerNeighbours[parity][edge];
                    cornerRanges[index] = new int2(cornerOffsets.Length, corners.Length);

                    foreach (var offset in corners)
                        cornerOffsets.Add(new int3(offset.x, offset.y, offset.z));
                }
            }
        }

        // Which of the four parity tables a cell reads: bit zero for an odd column, bit one for an odd row.
        public static int Parity(int3 cell) => (cell.x & 1) | ((cell.y & 1) << 1);

        // How many parity tables there are: odd or even column, times odd or even row.
        public const int ParityCount = 4;

        // Find the cell holding the same edge the other way round, among the cells within two steps of the probe.
        private static EdgeNeighbour FindEdgeNeighbour(Tilemap map, Vector2[] cellShape, Vector3Int probe, Vector2 start, Vector2 end, float epsilon)
        {
            for (var dy = -SearchReach; dy <= SearchReach; ++dy)
            {
                for (var dx = -SearchReach; dx <= SearchReach; ++dx)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    var offset = new Vector3Int(dx, dy, 0);
                    var origin = (Vector2)map.CellToLocal(probe + offset);

                    for (var edge = 0; edge < cellShape.Length; ++edge)
                    {
                        var otherStart = origin + cellShape[edge];
                        var otherEnd = origin + cellShape[(edge + 1) % cellShape.Length];

                        if ((otherStart - end).magnitude < epsilon && (otherEnd - start).magnitude < epsilon)
                            return new EdgeNeighbour(offset, edge);
                    }
                }
            }

            return new EdgeNeighbour(Vector3Int.zero, -1);
        }

        // Find every cell with a vertex at the corner, among the cells within two steps of the probe.
        private static Vector3Int[] FindCornerNeighbours(Tilemap map, Vector2[] cellShape, Vector3Int probe, Vector2 corner, float epsilon)
        {
            var found = new System.Collections.Generic.List<Vector3Int>();

            for (var dy = -SearchReach; dy <= SearchReach; ++dy)
            {
                for (var dx = -SearchReach; dx <= SearchReach; ++dx)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    var offset = new Vector3Int(dx, dy, 0);
                    var origin = (Vector2)map.CellToLocal(probe + offset);

                    foreach (var point in cellShape)
                    {
                        if ((origin + point - corner).magnitude >= epsilon)
                            continue;

                        found.Add(offset);
                        break;
                    }
                }
            }

            return found.ToArray();
        }

        private static int Parity(Vector3Int cell) => Parity(new int3(cell.x, cell.y, cell.z));

        // The cell across one edge, as an offset from the cell, and which of its edges it is. An edge of -1 means no cell shares it.
        private readonly struct EdgeNeighbour
        {
            public EdgeNeighbour(Vector3Int offset, int edge)
            {
                this.offset = offset;
                this.edge = edge;
            }

            public readonly Vector3Int offset;
            public readonly int edge;
        }

        #region Internal

        // How far from a cell its neighbours are searched for, in cells. A hexagon's neighbours are all within one step, so two is ample.
        private const int SearchReach = 2;

        // How close two placed vertices must be to count as the same, as a fraction of the cell's size.
        private const float MatchFraction = 1e-4f;

        private readonly int m_EdgeCount;

        // The cell shape and the position of one cell of each parity the tables were worked out from, which IsFor compares against.
        private readonly Vector2[] m_CellShape;
        private readonly Vector2[] m_ProbeOrigins = new Vector2[ParityCount];

        private readonly EdgeNeighbour[][] m_EdgeNeighbours = new EdgeNeighbour[ParityCount][];
        private readonly Vector3Int[][][] m_CornerNeighbours = new Vector3Int[ParityCount][][];

        #endregion
    }
}
