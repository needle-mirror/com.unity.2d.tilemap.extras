using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.Tilemaps.Experimental
{
    [BurstCompile]
    internal struct GetTileAnimationDataJob : IJobParallelForBatch
    {
        public TilemapDataStruct tilemapData;

        [ReadOnly] public NativeArray<int3> inPositions;
        [ReadOnly] public NativeArray<EntityId> inTileIds;
        [ReadOnly] public NativeHashMap<EntityId, int> inIdToIndexMap;
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> inStructDataStart;
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<byte> inTileStructDatas;
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<FunctionPointer<EntityIdTile.GetTileAnimationDataJobDelegate>> inFunctionPointers;

        [WriteOnly] public NativeArray<TileAnimationEntityIdData> outTileAnimationDatas;

        public unsafe void Execute(int startIndex, int count)
        {
            UnsafeUtility.MemClear(((TileAnimationEntityIdData*) outTileAnimationDatas.GetUnsafePtr() + startIndex), sizeof(TileAnimationEntityIdData) * count);

            var currentStart = startIndex;
            var currentTileId = inTileIds[startIndex];
            for (int i = 1; i < count; i++)
            {
                var currentIndex = startIndex + i;
                var tileId = inTileIds[currentIndex];
                if (currentTileId != EntityId.None
                    && currentTileId != tileId)
                {
                    var position = (int3*) inPositions.GetUnsafeReadOnlyPtr() + currentStart;
                    var tileIdx = inIdToIndexMap[currentTileId];
                    var tileStructData = (byte*) inTileStructDatas.GetUnsafeReadOnlyPtr() + inStructDataStart[tileIdx];
                    var tileAnimationData = (TileAnimationEntityIdData*) outTileAnimationDatas.GetUnsafePtr() + currentStart;
                    var function = inFunctionPointers[tileIdx];

                    function.Invoke(currentIndex - currentStart, position, tileStructData, ref tilemapData, tileAnimationData);

                    currentStart = currentIndex;
                    currentTileId = tileId;
                }
            }
            if (currentTileId != EntityId.None)
            {
                var position = (int3*) inPositions.GetUnsafeReadOnlyPtr() + currentStart;
                var tileIdx = inIdToIndexMap[currentTileId];
                var tileStructData = (byte*) inTileStructDatas.GetUnsafeReadOnlyPtr() + inStructDataStart[tileIdx];
                var tileAnimationData = (TileAnimationEntityIdData*) outTileAnimationDatas.GetUnsafePtr() + currentStart;
                var function = inFunctionPointers[tileIdx];
                function.Invoke(startIndex + count - currentStart, position, tileStructData, ref tilemapData, tileAnimationData);
            }
        }
    }
}
