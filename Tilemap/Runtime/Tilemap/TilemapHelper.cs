using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

[assembly: AlwaysLinkAssembly]

namespace Unity.Tilemaps.Experimental{
    [Preserve]
    [BurstCompile]
    internal class TilemapHelper : ITilemap
    {
        static ProfilerMarker s_RefreshTilePerfMarker = new ProfilerMarker("Tilemap.RefreshTile");
        static ProfilerMarker s_GetAllTileDataPerfMarker = new ProfilerMarker("Tilemap.GetAllTileData");
        static ProfilerMarker s_GetAllTileAnimationPerfMarker = new ProfilerMarker("Tilemap.GetAllTileAnimation");

        const int k_IndicesPerJob = 128;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void InitializeTilemapHelper()
        {
            ITilemap.RegisterCreateITilemapFunc(CreateInstance, 2);
        }

        private static ITilemap CreateInstance(Tilemap tilemap)
        {
            return new TilemapHelper(tilemap);
        }

        private struct RefreshThreadData
        {
            public NativeList<Vector3Int> m_RefreshPos;
        }

        private int m_RefreshThreadInitialCount;
        private NativeParallelHashMap<int, RefreshThreadData> m_RefreshThreadMap;

        public TilemapHelper(Tilemap tilemap) : base(tilemap)
        {
        }

        private static bool UseUnityJob(List<TileBase> tiles)
        {
            Profiler.BeginSample("TilemapHelper.Job.UseUnityJob");
            foreach (var tile in tiles)
            {
                if (tile != null && tile is not EntityIdTileBase)
                {
                    Profiler.EndSample();
                    return false;
                }
            }
            Profiler.EndSample();
            return true;
        }

        private static bool UseUnityJob(NativeArray<EntityId> tileIds)
        {
            Profiler.BeginSample("TilemapHelper.Job.UseUnityJob");
            foreach (var tileId in tileIds)
            {
                if (tileId == EntityId.None)
                    continue;

                var tile = Resources.EntityIdToObject(tileId);
                if (tile != null && tile is not EntityIdTileBase)
                {
                    Profiler.EndSample();
                    return false;
                }
            }
            Profiler.EndSample();
            return true;
        }

        internal override void HandleRefreshPositions(int count
            , NativeArray<EntityId> usedTileIds
            , NativeArray<EntityId> oldTilesIds
            , NativeArray<EntityId> newTilesIds
            , NativeArray<Vector3Int> positions)
        {
            using (s_RefreshTilePerfMarker.Auto())
            {
                var job = UseUnityJob(usedTileIds);
                if (job)
                {
                    Profiler.BeginSample("TilemapHelper.Job.PrepareRefreshPositions");
                    var positionsCount = positions.Length;
                    var structDataStart = new NativeArray<int>(usedTileIds.Length, Allocator.TempJob);
                    var refreshTileFunctionPointers =
                        new NativeArray<FunctionPointer<EntityIdTileBase.RefreshTileJobDelegate>>(usedTileIds.Length,
                            Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    var entityIdToIndexMap = new NativeHashMap<EntityId, int>(usedTileIds.Length, Allocator.TempJob);
                    var startIndex = 0;
                    for (var i = 0; i < usedTileIds.Length; i++)
                    {
                        var tile = Resources.EntityIdToObject(usedTileIds[i]) as EntityIdTileBase;
                        var tileDataSize = UnsafeUtility.SizeOf(tile.structType);
                        structDataStart[i] = startIndex;
                        startIndex += tileDataSize;
                        refreshTileFunctionPointers[i] = tile.refreshTileJobFunction;
                        entityIdToIndexMap.Add(tile.cachedEntityId, i);
                    }

                    var tileStructDatas = new NativeArray<byte>(startIndex, Allocator.TempJob);
                    for (var i = 0; i < usedTileIds.Length; i++)
                    {
                        var tile = Resources.EntityIdToObject(usedTileIds[i]) as EntityIdTileBase;
                        unsafe
                        {
                            tile.CopyDataStruct((byte*)tileStructDatas.GetUnsafePtr() + structDataStart[i]);
                        }
                    }

                    var tilemapHandle = m_Tilemap.GetTilemapHandle();
                    var initialSize = math.ceilpow2(positions.Length) << 4;
                    var outPositionsSet = new UnsafeParallelHashSet<int3>(initialSize, Allocator.TempJob);
                    Profiler.EndSample();

                    var refreshJob = new RefreshJob()
                    {
                        tilemapHandle = tilemapHandle,
                        inPositions = positions.Reinterpret<int3>(),
                        inOldTileIds = oldTilesIds,
                        inNewTileIds = newTilesIds,
                        inIdToIndexMap = entityIdToIndexMap,
                        inStructDataStart = structDataStart,
                        inTileStructDatas = tileStructDatas,
                        inFunctionPointers = refreshTileFunctionPointers,
                        outPositionsSet = outPositionsSet.AsParallelWriter()
                    };
                    var refreshJobHandle = refreshJob.ScheduleBatch(positionsCount, k_IndicesPerJob);
                    var disposeHandle = entityIdToIndexMap.Dispose(refreshJobHandle);
                    var combineHandle = JobHandle.CombineDependencies(refreshJobHandle, disposeHandle);
                    combineHandle.Complete();

                    Profiler.BeginSample("TilemapHelper.Job.SetToArray");
                    ToNativeArray(ref outPositionsSet, out m_RefreshPos);
                    m_RefreshCount = m_RefreshPos.Length;
                    Profiler.EndSample();

                    Profiler.BeginSample("TilemapHelper.Job.RefreshStructDispose");
                    outPositionsSet.Dispose();
                    Profiler.EndSample();
                }
                else
                {
                    Profiler.BeginSample("TilemapHelper.NonJob.Refresh");
                    base.HandleRefreshPositions(count, usedTileIds, oldTilesIds, newTilesIds, positions);
                    Profiler.EndSample();
                }
            }
        }

        [BurstCompile]
        static void ToNativeArray(ref UnsafeParallelHashSet<int3> positionsSet, out NativeArray<Vector3Int> outPositions)
        {
            outPositions = positionsSet.ToNativeArray(Allocator.TempJob).Reinterpret<Vector3Int>();
        }

        internal override unsafe JobHandle HandleGetAllTileData(int usedTileCount
            , NativeArray<EntityId> usedTilesIds
            , int count
            , NativeArray<EntityId> tileIds
            , NativeArray<Vector3Int> positions
            , NativeArray<TileData> tileDataArray)
        {
            using (s_GetAllTileDataPerfMarker.Auto())
            {
                var jobHandle = default(JobHandle);
                var job = UseUnityJob(usedTilesIds);
                if (job)
                {
                    Profiler.BeginSample("TilemapHelper.Job.PrepareGetAllTileData");
                    var structDataStart = new NativeArray<int>(usedTilesIds.Length, Allocator.TempJob);
                    var getTileDataFunctionPointers =
                        new NativeArray<FunctionPointer<EntityIdTileBase.GetTileDataJobDelegate>>(usedTilesIds.Length,
                            Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    var entityIdToIndexMap = new NativeHashMap<EntityId, int>(usedTilesIds.Length, Allocator.TempJob);
                    var startIndex = 0;
                    for (var i = 0; i < usedTilesIds.Length; i++)
                    {
                        var tile = Resources.EntityIdToObject(usedTilesIds[i]) as EntityIdTileBase;
                        var tileDataSize = UnsafeUtility.SizeOf(tile.structType);
                        structDataStart[i] = startIndex;
                        startIndex += tileDataSize;
                        getTileDataFunctionPointers[i] = tile.getTileDataJobFunction;
                        entityIdToIndexMap.Add(tile.cachedEntityId, i);
                    }
                    var tileStructDatas = new NativeArray<byte>(startIndex, Allocator.TempJob);
                    for (var i = 0; i < usedTilesIds.Length; i++)
                    {
                        var tile = Resources.EntityIdToObject(usedTilesIds[i]) as EntityIdTileBase;
                        tile.CopyDataStruct((byte*) tileStructDatas.GetUnsafePtr() + structDataStart[i]);
                    }
                    Profiler.EndSample();
                    var updateJob = new GetTileDataJob()
                    {
                        tilemapData = new TilemapDataStruct(m_Tilemap),
                        inTileIds = tileIds.Reinterpret<EntityId>(),
                        inPositions = positions.Reinterpret<int3>(),
                        inIdToIndexMap = entityIdToIndexMap,
                        inStructDataStart = structDataStart,
                        inTileStructDatas = tileStructDatas,
                        inFunctionPointers = getTileDataFunctionPointers,
                        outTileDatas = tileDataArray,
                    };
                    var updateCount = tileIds.Length;
                    var updateJobHandle = updateJob.ScheduleBatch(updateCount, k_IndicesPerJob);
                    var disposeHandle = entityIdToIndexMap.Dispose(updateJobHandle);
                    jobHandle = disposeHandle;
                }
                else
                {
                    Profiler.BeginSample("TilemapHelper.NonJob.GetAllTileData");
                    jobHandle = base.HandleGetAllTileData(usedTileCount, usedTilesIds, count, tileIds, positions, tileDataArray);
                    Profiler.EndSample();
                }
                return jobHandle;
            }
        }

        internal override unsafe JobHandle HandleGetAllTileAnimation(int usedTileCount
            , NativeArray<EntityId> usedTilesIds
            , NativeArray<bool> usedTileHasAnimation
            , int count
            , NativeArray<EntityId> tileIds
            , NativeArray<Vector3Int> positions
            , NativeArray<TileAnimationEntityIdData> tileAnimationDataArray)
        {
            using (s_GetAllTileAnimationPerfMarker.Auto())
            {
                var jobHandle = default(JobHandle);
                var job = UseUnityJob(usedTilesIds);
                if (job)
                {
                    Profiler.BeginSample("TilemapHelper.PrepareGetAllTileAnimation");
                    var structDataStart = new NativeArray<int>(usedTilesIds.Length, Allocator.TempJob);
                    var getTileAnimationDataFunctionPointers =
                        new NativeArray<FunctionPointer<EntityIdTileBase.GetTileAnimationDataJobDelegate>>(usedTilesIds.Length,
                            Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    var entityIdToIndexMap = new NativeHashMap<EntityId, int>(usedTilesIds.Length, Allocator.TempJob);
                    var startIndex = 0;
                    for (var i = 0; i < usedTilesIds.Length; i++)
                    {
                        var tile = Resources.EntityIdToObject(usedTilesIds[i]) as EntityIdTileBase;
                        var size = UnsafeUtility.SizeOf(tile.structType);
                        structDataStart[i] = startIndex;
                        startIndex += size;
                        getTileAnimationDataFunctionPointers[i] = tile.getTileAnimationDataJobFunction;
                        entityIdToIndexMap.Add(tile.cachedEntityId, i);
                    }
                    var tileStructDatas = new NativeArray<byte>(startIndex, Allocator.TempJob);
                    for (var i = 0; i < usedTilesIds.Length; i++)
                    {
                        var tile = Resources.EntityIdToObject(usedTilesIds[i]) as EntityIdTileBase;
                        tile.CopyDataStruct((byte*) tileStructDatas.GetUnsafePtr() + structDataStart[i]);
                    }
                    Profiler.EndSample();

                    Profiler.BeginSample("TilemapHelper.RunGetAllTileAnimation");
                    var updateJob = new GetTileAnimationDataJob()
                    {
                        tilemapData = new TilemapDataStruct(m_Tilemap),
                        inPositions = positions.Reinterpret<int3>(),
                        inTileIds = tileIds.Reinterpret<EntityId>(),
                        inIdToIndexMap = entityIdToIndexMap,
                        inStructDataStart = structDataStart,
                        inTileStructDatas = tileStructDatas,
                        inFunctionPointers = getTileAnimationDataFunctionPointers,
                        outTileAnimationDatas = tileAnimationDataArray,
                    };
                    var updateCount = tileIds.Length;
                    var updateJobHandle = updateJob.ScheduleBatch(updateCount, k_IndicesPerJob);
                    var disposeHandle = entityIdToIndexMap.Dispose(updateJobHandle);
                    jobHandle = disposeHandle;
                    Profiler.EndSample();
                }
                else
                {
                    Profiler.BeginSample("TilemapHelper.NonJob.GetAllTileAnimation");
                    jobHandle = base.HandleGetAllTileAnimation(usedTileCount, usedTilesIds, usedTileHasAnimation, count, tileIds, positions, tileAnimationDataArray);
                    Profiler.EndSample();
                }
                return jobHandle;
            }
        }
    }
}
