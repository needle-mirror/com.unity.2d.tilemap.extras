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

        private GridData m_GridData;

        [NativeDisableUnsafePtrRestriction]
        private TransformHandle m_TransformHandle;

        /// <summary>
        /// Create a a TilemapDataStruct for the input Tilemap
        /// </summary>
        /// <param name="tilemap">Tilemap to create TilemapDataStruct for</param>
        public TilemapDataStruct(Tilemap tilemap)
        {
            if (tilemap != null)
            {
                m_TilemapHandle = tilemap.GetTilemapHandle();
                m_TilemapAnimationFrameRate = tilemap.animationFrameRate;
                m_GridData = new GridData(tilemap);
                m_TransformHandle = tilemap.transformHandle;
            }
            else
            {
                m_TilemapHandle = IntPtr.Zero;
                m_TilemapAnimationFrameRate = 1.0f;
                m_GridData = default;
                m_TransformHandle = default;
            }
        }

        /// <summary>
        /// Gets Grid data for Tilemap
        /// </summary>
        public GridData gridData => m_GridData;

        /// <summary>
        /// Gets TransformHandle for Tilemap
        /// </summary>
        public TransformHandle transformHandle => m_TransformHandle;

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
#if UNITY_EDITOR
            return Tilemap.GetAnyTileEntityIdFromHandle(m_TilemapHandle, position.ToVector3Int());
#else
            return Tilemap.GetTileEntityIdFromHandle(m_TilemapHandle, position.ToVector3Int());
#endif
        }

        /// <summary>
        /// Gets the Entity Ids of Tiles at the position on the Tilemap within the given bounds
        /// </summary>
        /// <param name="position">Position on Tilemap.</param>
        /// <param name="bounds">Bounds surrounding the position.</param>
        /// <param name="entityIds">Array storing the Entity Ids of Tiles.</param>
        public void GetTilesFromBlockOffset(int3 position, BoundsInt bounds, NativeArray<EntityId> entityIds)
        {
#if UNITY_EDITOR
            Tilemap.GetAnyTileEntityIdsFromBlockOffsetAndHandle(m_TilemapHandle, position.ToVector3Int(), bounds, entityIds);
#else
            Tilemap.GetTileEntityIdsFromBlockOffsetAndHandle(m_TilemapHandle, position.ToVector3Int(), bounds, entityIds);
#endif

        }
    }
}
