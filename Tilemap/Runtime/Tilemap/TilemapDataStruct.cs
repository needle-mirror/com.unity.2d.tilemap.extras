using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.Tilemaps.Experimental
{
    /// <summary>
    /// Struct containing Tilemap information for use in jobs to get Tile data
    /// </summary>
    public struct TilemapDataStruct
    {
        [NativeDisableUnsafePtrRestriction]
        private IntPtr m_TilemapHandle;

        private float m_TilemapAnimationFrameRate;

        /// <summary>
        /// Createa a TilemapDataStruct for the input Tilemap
        /// </summary>
        /// <param name="tilemap">Tilemap to create TilemapDataStruct for</param>
        public TilemapDataStruct(Tilemap tilemap)
        {
            if (tilemap != null)
            {
                m_TilemapHandle = tilemap.GetTilemapHandle();
                m_TilemapAnimationFrameRate = tilemap.animationFrameRate;
            }
            else
            {
                m_TilemapHandle = IntPtr.Zero;
                m_TilemapAnimationFrameRate = 1.0f;
            }
        }

        /// <summary>
        /// Gets Tile Animation Frame Rate for Tilemap
        /// </summary>
        /// <returns>Tile Animation Frame Rate for Tilemap.</returns>
        public float GetTileAnimationFrameRate()
        {
            return m_TilemapAnimationFrameRate;
        }

        /// <summary>
        /// Gets the Entity Id of the Tile at the position on the Tilemap
        /// </summary>
        /// <param name="position">Position on Tilemap.</param>
        /// <returns>Entity Id of the Tile at the position on the Tilemap.</returns>
        public EntityId GetTileId(int3 position)
        {
            return Tilemap.GetTileEntityIdFromHandle(m_TilemapHandle, position.ToVector3Int());
        }

        /// <summary>
        /// Gets the Entity Ids of Tiles at the position on the Tilemap within the given bounds
        /// </summary>
        /// <param name="position">Position on Tilemap.</param>
        /// <param name="bounds">Bounds surrounding the position.</param>
        /// <param name="entityIds">Array storing the Entity Ids of Tiles.</param>
        public void GetTilesFromBlockOffset(int3 position, BoundsInt bounds, NativeArray<EntityId> entityIds)
        {
            Tilemap.GetTileEntityIdsFromBlockOffsetAndHandle(m_TilemapHandle, position.ToVector3Int(), bounds, entityIds);
        }
    }
}
