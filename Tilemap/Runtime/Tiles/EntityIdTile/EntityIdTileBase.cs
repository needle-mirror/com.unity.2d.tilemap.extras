using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.Tilemaps.Experimental
{
    /// <summary>
    /// Base abstract class for all Entity Id Tiles used by a Tilemap.
    /// In order for a custom Tile to use Unity Jobs and Burst by a Tilemap,
    /// the custom Tile must be derived from EntityIdTileBase or other Tiles
    /// which derive from EntityIdTileBase.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public abstract class EntityIdTileBase : TileBase
    {
        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to refresh Tiles of this type
        /// </summary>
        protected abstract unsafe RefreshTileJobDelegate refreshTileJobDelegate { get; }

        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to get Tile Data from Tiles of this type
        /// </summary>
        protected abstract unsafe GetTileDataJobDelegate getTileDataJobDelegate { get; }

        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to get Tile Animation Data from Tiles of this type
        /// </summary>
        protected abstract unsafe GetTileAnimationDataJobDelegate getTileAnimationDataJobDelegate { get; }

        private static Dictionary<Type, FunctionPointer<RefreshTileJobDelegate>> s_RefreshTileJobDelegateDictionary = new  Dictionary<Type, FunctionPointer<RefreshTileJobDelegate>>();

        private static Dictionary<Type, FunctionPointer<GetTileDataJobDelegate>> s_GetTileDataJobDelegateDictionary =  new Dictionary<Type, FunctionPointer<GetTileDataJobDelegate>>();

        private static Dictionary<Type, FunctionPointer<GetTileAnimationDataJobDelegate>> s_GetTileAnimationDataJobDelegateDictionary =  new Dictionary<Type, FunctionPointer<GetTileAnimationDataJobDelegate>>();

        /// <summary>
        /// Initialises the EntityIdTileBase.
        /// If a Tile is derived from EntityIdTileBase and overrides OnEnable,
        /// it must call base.OnEnable in the overriden OnEnable
        /// </summary>
        public override void OnEnable()
        {
            base.OnEnable();
            var type = GetType();
            if (!s_RefreshTileJobDelegateDictionary.ContainsKey(type))
            {
                if (refreshTileJobDelegate == null)
                {
                    FunctionPointer<RefreshTileJobDelegate> fp = default;
                    s_RefreshTileJobDelegateDictionary.Add(type, fp);
                }
                else
                {
                    var refreshTileJobBurstedDelegate =
                        BurstCompiler.CompileFunctionPointer<EntityIdTileBase.RefreshTileJobDelegate>(
                            refreshTileJobDelegate);
                    s_RefreshTileJobDelegateDictionary.Add(type, refreshTileJobBurstedDelegate);
                }
            }

            if (!s_GetTileDataJobDelegateDictionary.ContainsKey(type))
            {
                if (getTileDataJobDelegate == null)
                {
                    FunctionPointer<GetTileDataJobDelegate> fp = default;
                    s_GetTileDataJobDelegateDictionary.Add(type, fp);
                }
                else
                {
                    var getTileDataJobBurstedDelegate =
                        BurstCompiler.CompileFunctionPointer<EntityIdTileBase.GetTileDataJobDelegate>(
                            getTileDataJobDelegate);
                    s_GetTileDataJobDelegateDictionary.Add(type, getTileDataJobBurstedDelegate);
                }
            }

            if (!s_GetTileAnimationDataJobDelegateDictionary.ContainsKey(type))
            {
                if (getTileAnimationDataJobDelegate == null)
                {
                    FunctionPointer<GetTileAnimationDataJobDelegate> fp = default;
                    s_GetTileAnimationDataJobDelegateDictionary.Add(type, fp);
                }
                else
                {
                    var getTileDataJobBurstedDelegate =
                        BurstCompiler.CompileFunctionPointer<EntityIdTileBase.GetTileAnimationDataJobDelegate>(
                            getTileAnimationDataJobDelegate);
                    s_GetTileAnimationDataJobDelegateDictionary.Add(type, getTileDataJobBurstedDelegate);
                }
            }
        }

        /// <summary>
        /// Returns the type of data struct used for this EntityIdTileBase
        /// </summary>
        public abstract Type structType { get; }

        /// <summary>
        /// Copies the data struct used for this EntityIdTileBase to the outPtr buffer
        /// for use in Unity Jobs.
        /// </summary>
        /// <param name="outPtr">Data buffer to copy data struct from EntityIdTileBase to.</param>
        public abstract unsafe void CopyDataStruct(void* outPtr);

        /// <summary>
        /// Returns the Burst Compiler compiled function pointer for the Tile's `refreshTileJobDelegate`
        /// </summary>
        public FunctionPointer<RefreshTileJobDelegate> refreshTileJobFunction => s_RefreshTileJobDelegateDictionary[GetType()];

        /// <summary>
        /// Returns the Burst Compiler compiled function pointer for the Tile's `getTileDataJobDelegate`
        /// </summary>
        public FunctionPointer<GetTileDataJobDelegate> getTileDataJobFunction => s_GetTileDataJobDelegateDictionary[GetType()];

        /// <summary>
        /// Returns the Burst Compiler compiled function pointer for the Tile's `getTileAnimationDataJobDelegate`
        /// </summary>
        public FunctionPointer<GetTileAnimationDataJobDelegate> getTileAnimationDataJobFunction => s_GetTileAnimationDataJobDelegateDictionary[GetType()];

        /// <summary>
        /// The delegate called by Tilemap using Unity Jobs to refresh Tiles
        /// </summary>
        /// <param name="count">The number of positions on the Tilemap where this Tile is set</param>
        /// <param name="position">A pointer containing an array of positions on the Tilemap where this Tile is set with the size count</param>
        /// <param name="data">A data pointer containing a copy of this Tile's data (from CopyDataStruct)</param>
        /// <param name="tilemapRefreshStruct">Contains data for the Tilemap where the Tile is placed and functions to refresh positions on the Tilemap</param>
        public unsafe delegate void RefreshTileJobDelegate(int count, int3* position, void* data, ref TilemapRefreshStruct tilemapRefreshStruct);

        /// <summary>
        /// The delegate called by Tilemap using Unity Jobs to get Tile Data from Tiles
        /// </summary>
        /// <param name="count">The number of positions on the Tilemap where this Tile is set</param>
        /// <param name="position">A pointer containing an array of positions on the Tilemap where this Tile is set with the size count</param>
        /// <param name="data">A data pointer containing a copy of this Tile's data (from CopyDataStruct)</param>
        /// <param name="tilemapDataStruct">Contains data for the Tilemap where the Tile is placed and functions to retrieve Tile data from the Tilemap</param>
        /// <param name="outTileData">A pointer containing an array of TileData on the Tilemap which should be filled by this delegate function with the size count</param>
        public unsafe delegate void GetTileDataJobDelegate(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileData* outTileData);

        /// <summary>
        /// The delegate called by Tilemap using Unity Jobs to get Tile Animation Data from Tiles
        /// </summary>
        /// <param name="count">The number of positions on the Tilemap where this Tile is set</param>
        /// <param name="position">A pointer containing an array of positions on the Tilemap where this Tile is set with the size count</param>
        /// <param name="data">A data pointer containing a copy of this Tile's data (from CopyDataStruct)</param>
        /// <param name="tilemapDataStruct">Contains data for the Tilemap where the Tile is placed and functions to retrieve Tile data from the Tilemap</param>
        /// <param name="outTileAnimationEntityIdData">A pointer containing an array of TileAnimationEntityIdData on the Tilemap which should be filled by this delegate function with the size count</param>
        public unsafe delegate void GetTileAnimationDataJobDelegate(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileAnimationEntityIdData* outTileAnimationEntityIdData);
    }
}
