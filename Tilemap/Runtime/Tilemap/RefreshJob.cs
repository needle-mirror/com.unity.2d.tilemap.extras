using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.Tilemaps.Experimental
{
    [BurstCompile]
    internal struct RefreshJob : IJobParallelForBatch
    {
        [NativeSetThreadIndex]
        private int m_ThreadIndex;

        [NativeDisableUnsafePtrRestriction]
        public IntPtr tilemapHandle;

        [ReadOnly] public NativeArray<int3> inPositions;
        [ReadOnly] public NativeArray<EntityId> inOldTileIds;
        [ReadOnly] public NativeArray<EntityId> inNewTileIds;
        [ReadOnly] public NativeHashMap<EntityId, int> inIdToIndexMap;
        [DeallocateOnJobCompletion]
        [ReadOnly] public NativeArray<int> inStructDataStart;
        [DeallocateOnJobCompletion]
        [ReadOnly] public NativeArray<byte> inTileStructDatas;
        [DeallocateOnJobCompletion]
        [ReadOnly] public NativeArray<FunctionPointer<EntityIdTileBase.RefreshTileJobDelegate>> inFunctionPointers;

        public UnsafeParallelHashSet<int3>.ParallelWriter outPositionsSet;

        [BurstCompile]
        private unsafe void ExecuteList(int startIndex, int count, NativeArray<EntityId> tileIds, ref TilemapRefreshStruct tilemapRefreshStruct)
        {
            var currentStart = startIndex;
            var currentTileId = tileIds[startIndex];

            for (int i = 1; i < count; i++)
            {
                var tileId = tileIds[startIndex + i];
                if (currentTileId != EntityId.None
                    && currentTileId != tileId)
                {
                    var position = (int3*) inPositions.GetUnsafeReadOnlyPtr() + currentStart;
                    var tileIdx = inIdToIndexMap[currentTileId];
                    var tileStructData = (byte*) inTileStructDatas.GetUnsafeReadOnlyPtr() + inStructDataStart[tileIdx];
                    var function = inFunctionPointers[tileIdx];

                    function.Invoke(startIndex + i - currentStart, position, tileStructData, ref tilemapRefreshStruct);

                    currentStart = i;
                    currentTileId = tileId;
                }
            }
            if (currentTileId != EntityId.None)
            {
                var position = (int3*) inPositions.GetUnsafeReadOnlyPtr() + currentStart;
                var tileIdx = inIdToIndexMap[currentTileId];
                var tileStructData = (byte*) inTileStructDatas.GetUnsafeReadOnlyPtr() + inStructDataStart[tileIdx];
                var function = inFunctionPointers[tileIdx];
                function.Invoke(startIndex + count - currentStart, position, tileStructData, ref tilemapRefreshStruct);
            }
        }

        [BurstCompile]
        public void Execute(int startIndex, int count)
        {
            var tilemapRefreshStruct = new TilemapRefreshStruct(tilemapHandle, outPositionsSet);
            ExecuteList(startIndex, count, inOldTileIds, ref tilemapRefreshStruct);
            ExecuteList(startIndex, count, inNewTileIds, ref tilemapRefreshStruct);
        }
    }
}
