using System;

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.U2D.Physics
{
    // What both Segments builders do the same way once each has worked out a cell's surviving segments: the order cells settle ties by,
    // the ghost vertices of a segment, keeping existing shapes through an edit, and stamping each shape with its cell.
    // The geometry methods only read native containers, so a Burst job can call them as well as managed code.
    internal static class TilemapSegmentShapes
    {
        // A fixed order on cells, so two cells agree which of them is lower without being told: x first, then y, then z.
        public static bool IsLower(int3 a, int3 b)
        {
            if (a.x != b.x)
                return a.x < b.x;

            return a.y != b.y ? a.y < b.y : a.z < b.z;
        }

        public static bool IsLower(Vector3Int a, Vector3Int b) => IsLower(new int3(a.x, a.y, a.z), new int3(b.x, b.y, b.z));

        // An update table's starting size: a region per changed cell, capped, since regions overlap and the table grows.
        public static int UpdateCapacity(int changedCells, int cellsPerChange) =>
            (int)math.clamp((long)changedCells * cellsPerChange, 1L, MaxUpdateCapacity);

        // Whether a batch is big enough that rebuilding is cheaper than updating. Below the minimum it always updates.
        public static bool IsRebuildCheaper(int changedCells, int builtCells) =>
            changedCells >= MinRebuildCells && changedCells > builtCells / RebuildFraction;

        // The far end of the segment that continues the surface past a vertex, the one the smallest angle away, or our own far end when none meets it.
        // The solver reads it so a body sliding across the join doesn't catch. Arriving segments end at the vertex and give their start; leaving ones give their end.
        // Segments are start and end packed into one value, and nearby holds the runs of them to look through.
        public static float2 FindGhost(NativeArray<float4> segments, NativeList<int2> nearby, int self, float2 vertex, float2 far, float tolerance, bool arriving)
        {
            var ours = math.atan2(far.y - vertex.y, far.x - vertex.x);
            var bestAngle = float.MaxValue;
            var best = far;

            for (var r = 0; r < nearby.Length; ++r)
            {
                var range = nearby[r];
                for (var i = range.x; i < range.x + range.y; ++i)
                {
                    if (i == self)
                        continue;

                    var candidate = segments[i];
                    var meeting = arriving ? candidate.zw : candidate.xy;
                    if (math.distancesq(meeting, vertex) > tolerance * tolerance)
                        continue;

                    var candidateFar = arriving ? candidate.xy : candidate.zw;
                    var theirs = math.atan2(candidateFar.y - vertex.y, candidateFar.x - vertex.x);

                    var delta = arriving ? ours - theirs : theirs - ours;
                    while (delta <= AngleEpsilon)
                        delta += 2f * math.PI;

                    while (delta > 2f * math.PI)
                        delta -= 2f * math.PI;

                    if (delta < bestAngle)
                    {
                        bestAngle = delta;
                        best = candidateFar;
                    }
                }
            }

            return best;
        }

        // Pair a cell's existing shapes with its new geometry: identical geometry keeps its shape untouched, so the shape keeps its contacts;
        // the rest of the existing shapes take the rest of the geometry in order; and only what is left over is created or destroyed.
        public static void MatchShapes(Vector3Int cell, NativeList<PhysicsShape> oldShapes, NativeList<ChainSegmentGeometry> geometry,
            NativeList<PhysicsShape> kept, NativeList<Vector3Int> keptCells, NativeList<PhysicsShape> reshaped, NativeList<ChainSegmentGeometry> reshapedGeometry,
            NativeList<PhysicsShape> destroyed, NativeList<ChainSegmentGeometry> createGeometry, NativeList<Vector3Int> createCells)
        {
            var oldGeometry = new NativeArray<ChainSegmentGeometry>(oldShapes.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var oldMatched = new NativeArray<bool>(oldShapes.Length, Allocator.Temp);
            var newMatched = new NativeArray<bool>(geometry.Length, Allocator.Temp);

            for (var o = 0; o < oldShapes.Length; ++o)
                oldGeometry[o] = oldShapes[o].isValid ? oldShapes[o].chainSegmentGeometry : default;

            for (var n = 0; n < geometry.Length; ++n)
            {
                for (var o = 0; o < oldShapes.Length; ++o)
                {
                    if (oldMatched[o] || !oldShapes[o].isValid || !IsSame(oldGeometry[o], geometry[n]))
                        continue;

                    oldMatched[o] = true;
                    newMatched[n] = true;
                    kept.Add(oldShapes[o]);
                    keptCells.Add(cell);
                    break;
                }
            }

            var nextOld = 0;
            for (var n = 0; n < geometry.Length; ++n)
            {
                if (newMatched[n])
                    continue;

                while (nextOld < oldShapes.Length && (oldMatched[nextOld] || !oldShapes[nextOld].isValid))
                    ++nextOld;

                if (nextOld < oldShapes.Length)
                {
                    oldMatched[nextOld] = true;
                    reshaped.Add(oldShapes[nextOld]);
                    reshapedGeometry.Add(geometry[n]);
                    kept.Add(oldShapes[nextOld]);
                    keptCells.Add(cell);
                    continue;
                }

                createGeometry.Add(geometry[n]);
                createCells.Add(cell);
            }

            for (var o = 0; o < oldShapes.Length; ++o)
            {
                if (!oldMatched[o] && oldShapes[o].isValid)
                    destroyed.Add(oldShapes[o]);
            }

            newMatched.Dispose();
            oldMatched.Dispose();
            oldGeometry.Dispose();
        }

        // Whether two chain segments are the same in every point, the ghost vertices included.
        public static bool IsSame(ChainSegmentGeometry a, ChainSegmentGeometry b) =>
            a.segment.point1 == b.segment.point1 && a.segment.point2 == b.segment.point2 && a.ghost1 == b.ghost1 && a.ghost2 == b.ghost2;

        // Record which cell each shape came from, so anything reading a contact can tell which tile it touched.
        public static void StampCells(in TilemapBuildContext context, NativeArray<PhysicsShape> shapes, NativeArray<Vector3Int> cells)
        {
            var userDatas = new NativeArray<PhysicsUserData>(shapes.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < shapes.Length; ++i)
                userDatas[i] = new PhysicsUserData { vector3IntValue = cells[i] };

            context.shapeContext.SetOwnerUserData(shapes.AsReadOnlySpan(), userDatas.AsReadOnlySpan());
            userDatas.Dispose();
        }

        // Place a segment and its ghost vertices in the body's space. A chain segment collides on its right,
        // so a mirroring placement writes it the other way round, ghosts swapped, to keep it facing out.
        public static ChainSegmentGeometry Place(float4 segment, float4 ghosts, Matrix4x4 relative)
        {
            var mirrors = relative.m00 * relative.m11 - relative.m01 * relative.m10 < 0f;

            var chainSegment = mirrors
                ? new ChainSegmentGeometry(SegmentGeometry.Create(segment.zw, segment.xy), ghosts.zw, ghosts.xy)
                : new ChainSegmentGeometry(SegmentGeometry.Create(segment.xy, segment.zw), ghosts.xy, ghosts.zw);

            return chainSegment.Transform(relative);
        }

        // A cell, as the key of a native hash container, from the form a job works with.
        public static TilemapCellKey ToKey(int3 cell) => new(new Vector3Int(cell.x, cell.y, cell.z));

        public static int3 ToInt3(Vector3Int cell) => new(cell.x, cell.y, cell.z);

        public static Vector3Int ToVector3Int(int3 cell) => new(cell.x, cell.y, cell.z);

        // One surviving piece and the cell whose outline it came from, as a job writes it.
        internal struct CellSegment
        {
            public int3 cell;
            public float4 segment;
        }

        // Every surviving piece before ghost vertices, in the Tilemap's local space, the cell each came from, and where each cell's run of them is.
        internal struct RawSegments : IDisposable
        {
            public RawSegments(int capacity)
            {
                segments = new NativeList<float4>(capacity, Allocator.TempJob);
                cells = new NativeList<int3>(capacity, Allocator.TempJob);
                ranges = new NativeHashMap<TilemapCellKey, int2>(capacity, Allocator.TempJob);
            }

            // Read back what a job wrote, one stream index per chunk of cells.
            // Each chunk wrote its cells in order, so reading the chunks back in order keeps every cell's pieces together and the result the same however the work was spread.
            public static RawSegments Read(NativeStream stream, int chunkCount)
            {
                var reader = stream.AsReader();
                var raw = new RawSegments(math.max(stream.Count(), 1));

                for (var chunk = 0; chunk < chunkCount; ++chunk)
                {
                    var count = reader.BeginForEachIndex(chunk);
                    for (var i = 0; i < count; ++i)
                        raw.Add(reader.Read<CellSegment>());

                    reader.EndForEachIndex();
                }

                return raw;
            }

            // Add a piece. A cell's pieces arrive together, so a cell's run is extended until a piece of another cell arrives.
            public void Add(CellSegment piece)
            {
                var index = segments.Length;
                segments.Add(piece.segment);
                cells.Add(piece.cell);

                var key = ToKey(piece.cell);
                if (ranges.TryGetValue(key, out var range))
                    ranges[key] = new int2(range.x, index - range.x + 1);
                else
                    ranges.Add(key, new int2(index, 1));
            }

            public void Dispose()
            {
                if (segments.IsCreated)
                    segments.Dispose();

                if (cells.IsCreated)
                    cells.Dispose();

                if (ranges.IsCreated)
                    ranges.Dispose();
            }

            public NativeList<float4> segments;
            public NativeList<int3> cells;
            public NativeHashMap<TilemapCellKey, int2> ranges;
        }

        #region Internal

        // The smallest angular step that counts as turning, so a segment never picks itself or one running exactly back over it.
        private const float AngleEpsilon = 1e-5f;

        // The most cells an update's working table starts out able to hold.
        private const long MaxUpdateCapacity = 1 << 16;

        // A batch changing more than this fraction of the cells the last build read rebuilds, provided it changes at least the minimum.
        private const int RebuildFraction = 4;
        private const int MinRebuildCells = 1024;

        #endregion
    }
}
