using System;

using Unity.Collections;
using Unity.Mathematics;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.U2D.Physics
{
    // Reads every occupied cell of a Tilemap for a full build from one snapshot of its tile grid, rather than calling into the engine for each cell.
    // A cell's tile comes down to a source: its collider type, sprite and transform, or the Grid's cell shape. Many cells share a source.
    // Each distinct source is looked up in the tile cache once per build, and every cell with that source reuses the answer.
    // A cell reads the same sprite, transform and collider type as Tilemap.GetSprite, GetTransformMatrix and GetColliderType return for it.
    // Dispose it when the build is done. It is a struct holding its last answer, so call it through a local that is not readonly.
    internal struct TilemapGridReader : IDisposable
    {
        // Snapshot the Tilemap's tile grid and count its occupied cells.
        // If anything here throws, what was already taken is released before the exception leaves.
        public static TilemapGridReader Acquire(Tilemap map)
        {
            var reader = new TilemapGridReader { m_LastSource = Source.none };

            try
            {
                reader.m_Grid = map.AcquireReadOnlyTileGridData(s_MemoryLabel);
                reader.m_Results = new NativeHashMap<Source, int2>(16, Allocator.Temp);
                reader.m_SpriteCount = (uint)reader.m_Grid.spriteCount;
                reader.m_AnimatedSpriteCount = (uint)reader.m_Grid.animatedSpriteCount;

                var occupiedCount = 0;

                for (var c = 0; c < reader.m_Grid.Length; ++c)
                {
                    var chunk = reader.m_Grid[c];

                    for (var i = 0; i < TileChunkData.kCells; ++i)
                    {
                        if (chunk[i].isOccupied)
                            ++occupiedCount;
                    }
                }

                reader.occupiedCount = occupiedCount;
            }
            catch
            {
                reader.Dispose();
                throw;
            }

            return reader;
        }

        public TileChunkData GetChunk(int index) => m_Grid[index];

        // The source of a cell's tile. False when the cell contributes nothing: it is empty, its collider type is None, or it is a Sprite cell with no sprite.
        // A position-animated cell uses its animated sprite when it has one, the same as Tilemap.GetSprite.
        public bool TryGetSource(TileCellData cell, out Source source)
        {
            source = Source.none;

            if (!cell.isOccupied)
                return false;

            var colliderType = cell.colliderType;
            switch (colliderType)
            {
                case Tile.ColliderType.None:
                    return false;

                // Every Grid cell is the cell shape itself, whatever its sprite and transform.
                case Tile.ColliderType.Grid:
                    source = Source.grid;
                    return true;
            }

            // Sprite, and anything a future version adds. Animated sprites follow the tile sprites in one numbering.
            uint spriteSlot;
            if (cell.isPositionAnimated && cell.animatedSpriteIndex < m_AnimatedSpriteCount)
                spriteSlot = m_SpriteCount + cell.animatedSpriteIndex;
            else if (cell.spriteIndex < m_SpriteCount)
                spriteSlot = cell.spriteIndex;
            else
                return false;

            source = new Source(colliderType, spriteSlot, cell.matrixIndex);
            return true;
        }

        // The tile cache's polygon range for a source, looked up the first time the source is seen in this build.
        public int2 GetPolygonRange(Source source, TilemapTileCache cache, bool clipped)
        {
            if (TryGetResult(source, out var range))
                return range;

            range = TryResolve(source, out var content) ? cache.GetPolygonRange(content, clipped) : int2.zero;
            SetResult(source, range);
            return range;
        }

        // The tile cache's clipped segment entry for a source, or -1 when it contributes nothing, looked up the first time the source is seen in this build.
        public int GetSegmentEntry(Source source, TilemapTileCache cache)
        {
            if (TryGetResult(source, out var result))
                return result.x;

            var entry = TryResolve(source, out var content) ? cache.GetSegmentEntry(content) : -1;
            SetResult(source, new int2(entry, 0));
            return entry;
        }

        // The tile cache's outline entry for a source, or -1 when it contributes nothing, looked up the first time the source is seen in this build.
        public int GetOutlineEntry(Source source, TilemapTileCache cache)
        {
            if (TryGetResult(source, out var result))
                return result.x;

            var entry = TryResolve(source, out var content) ? cache.GetOutlineEntry(content) : -1;
            SetResult(source, new int2(entry, 0));
            return entry;
        }

        public void Dispose()
        {
            m_Grid.Dispose();

            if (m_Results.IsCreated)
                m_Results.Dispose();
        }

        // Number of chunks in the snapshot. Each holds TileChunkData.kCells cells, empty ones included.
        public int chunkCount => m_Grid.Length;

        // Number of cells holding a tile, whatever its collider type.
        public int occupiedCount { get; private set; }

        // What a cell's tile comes down to: its collider type, sprite slot and transform matrix, or the Grid's cell shape.
        // The three are packed into one long, so the bit layout is written and read here alone, and a source compares and hashes as one value.
        public readonly struct Source : IEquatable<Source>
        {
            public Source(Tile.ColliderType colliderType, uint spriteSlot, ushort matrixIndex) =>
                m_Value = ((long)colliderType << 48) | ((long)spriteSlot << 16) | matrixIndex;

            private Source(long value) => m_Value = value;

            public bool Equals(Source other) => m_Value == other.m_Value;

            public override bool Equals(object obj) => obj is Source other && Equals(other);

            // A long's own hash leaves the matrix index alone in its low bits, and a native container picks a bucket from the low bits.
            // Every source with the same matrix would then share one bucket, so the hash is mixed.
            public override int GetHashCode() => TilemapCellKey.Mix((uint)(m_Value & 0xFFFFFFFF) ^ (uint)((m_Value >> 32) & 0xFFFFFFFF));

            public Tile.ColliderType colliderType => (Tile.ColliderType)(m_Value >> 48);

            public uint spriteSlot => (uint)((m_Value >> 16) & 0xFFFFFFFF);

            public ushort matrixIndex => (ushort)(m_Value & 0xFFFF);

            public bool isGrid => m_Value == k_GridValue;

            // The one source every Grid cell shares, whatever its sprite and transform.
            public static Source grid => new(k_GridValue);

            // No source at all, held before the reader has found its first answer.
            public static Source none => new(k_NoneValue);

            // Values a sprite cell can never have: every Grid cell shares one, and the other marks no source.
            private const long k_GridValue = -1;
            private const long k_NoneValue = long.MinValue;

            private readonly long m_Value;
        }

        #region Internal

        // The answer already found for a source in this build.
        // Neighboring cells usually share a tile, so the last answer is checked before the table.
        private bool TryGetResult(Source source, out int2 result)
        {
            if (source.Equals(m_LastSource))
            {
                result = m_LastResult;
                return true;
            }

            if (!m_Results.TryGetValue(source, out result))
                return false;

            m_LastSource = source;
            m_LastResult = result;
            return true;
        }

        private void SetResult(Source source, int2 result)
        {
            m_Results[source] = result;
            m_LastSource = source;
            m_LastResult = result;
        }

        // What a source holds, for the tile cache. False when its sprite no longer exists, the same as a cell whose Tilemap.GetSprite returns null.
        private bool TryResolve(Source source, out TilemapCellReader.Content content)
        {
            if (source.isGrid)
            {
                content = new TilemapCellReader.Content(Tile.ColliderType.Grid, null, Matrix4x4.identity);
                return true;
            }

            var colliderType = source.colliderType;
            var spriteSlot = source.spriteSlot;
            var matrixIndex = source.matrixIndex;

            var spriteId = spriteSlot < m_SpriteCount ? m_Grid.GetSpriteEntityId(spriteSlot) : m_Grid.GetAnimatedSpriteEntityId(spriteSlot - m_SpriteCount);
            var sprite = Resources.EntityIdToObject(spriteId) as Sprite;
            if (sprite == null)
            {
                content = default;
                return false;
            }

            // An index outside the matrices reads as identity, the same as Tilemap.GetTransformMatrix.
            var tileMatrix = matrixIndex < m_Grid.matrixCount ? m_Grid.GetMatrix(matrixIndex) : Matrix4x4.identity;
            content = new TilemapCellReader.Content(colliderType, sprite, tileMatrix);
            return true;
        }

        // Labels the snapshot's native memory.
        [NoAutoStaticsCleanup] private static readonly MemoryLabel s_MemoryLabel = new("PhysicsAreaTilemap", "TileGridData");

        private TileGridDataArray m_Grid;
        private uint m_SpriteCount;
        private uint m_AnimatedSpriteCount;

        // The answer for each source seen in this build, and the last one found.
        private NativeHashMap<Source, int2> m_Results;
        private Source m_LastSource;
        private int2 m_LastResult;

        #endregion
    }
}
