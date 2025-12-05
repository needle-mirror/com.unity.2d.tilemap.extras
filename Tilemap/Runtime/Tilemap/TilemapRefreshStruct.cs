using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Unity.Tilemaps.Experimental
{
    /// <summary>
    /// Struct containing Tilemap information for use in jobs to refresh Tiles
    /// </summary>
    [BurstCompile]
    public struct TilemapRefreshStruct
    {
        [NativeDisableUnsafePtrRestriction]
        private IntPtr m_TilemapHandle;

        private UnsafeParallelHashSet<int3>.ParallelWriter m_OutPositionsSet;

        internal TilemapRefreshStruct(IntPtr tilemapHandle, UnsafeParallelHashSet<int3>.ParallelWriter outPositionsSet)
        {
            m_TilemapHandle = tilemapHandle;
            m_OutPositionsSet = outPositionsSet;
        }

        /// <summary>
        /// Refreshes the given position on the Tilemap
        /// </summary>
        /// <param name="position">Position to refresh on the Tilemap</param>
        public void RefreshTile(int3 position)
        {
            m_OutPositionsSet.Add(position);
        }

        /// <summary>
        /// Refreshes the given positions on the Tilemap
        /// </summary>
        /// <param name="positions">Positions to refresh on the Tilemap</param>
        public void RefreshTiles(NativeArray<int3> positions)
        {
            foreach (var position in positions)
                m_OutPositionsSet.Add(position);
        }

        /// <summary>
        /// Refreshes the given positions on the Tilemap
        /// </summary>
        /// <param name="positions">Positions to refresh on the Tilemap</param>
        public void RefreshTiles(NativeSlice<int3> positions)
        {
            foreach (var position in positions)
                m_OutPositionsSet.Add(position);
        }
    }
}
