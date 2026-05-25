using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.Tilemaps.Experimental
{
    /// <summary>
    /// Static utility class providing Grid coordinate conversions.
    /// All methods are Burst-compatible and can be used in Unity Jobs.
    /// </summary>
    [BurstCompile]
    public static class GridUtility
    {
        /// <summary>
        /// Epsilon value for near-zero comparisons in grid calculations.
        /// </summary>
        public const float kGridEpsilon = 0.00001f;

        const float kHexVerticalDistance = 0.75f;

        // -------------------------------------------------------------------
        // Swizzle
        // -------------------------------------------------------------------

        /// <summary>
        /// Applies a swizzle transformation to reorder the components of a position.
        /// </summary>
        /// <param name="swizzle">The swizzle mode to apply.</param>
        /// <param name="position">The position to swizzle.</param>
        /// <param name="result">The swizzled position.</param>
        [BurstCompile]
        public static void CellSwizzle(GridData.Swizzle swizzle, in float3 position, out float3 result)
        {
            switch (swizzle)
            {
                case GridData.Swizzle.XYZ: result = position; break;
                case GridData.Swizzle.XZY: result = new float3(position.x, position.z, position.y); break;
                case GridData.Swizzle.YXZ: result = new float3(position.y, position.x, position.z); break;
                case GridData.Swizzle.YZX: result = new float3(position.y, position.z, position.x); break;
                case GridData.Swizzle.ZXY: result = new float3(position.z, position.x, position.y); break;
                case GridData.Swizzle.ZYX: result = new float3(position.z, position.y, position.x); break;
                default: result = position; break;
            }
        }

        /// <summary>
        /// Applies the inverse swizzle transformation.
        /// Note: YZX and ZXY are inverses of each other; all others are self-inverse.
        /// </summary>
        /// <param name="swizzle">The swizzle mode to invert.</param>
        /// <param name="position">The position to inverse-swizzle.</param>
        /// <param name="result">The inverse-swizzled position.</param>
        [BurstCompile]
        public static void InverseCellSwizzle(GridData.Swizzle swizzle, in float3 position, out float3 result)
        {
            switch (swizzle)
            {
                case GridData.Swizzle.XYZ: result = position; break;
                case GridData.Swizzle.XZY: result = new float3(position.x, position.z, position.y); break;
                case GridData.Swizzle.YXZ: result = new float3(position.y, position.x, position.z); break;
                case GridData.Swizzle.YZX: result = new float3(position.z, position.x, position.y); break;
                case GridData.Swizzle.ZXY: result = new float3(position.y, position.z, position.x); break;
                case GridData.Swizzle.ZYX: result = new float3(position.z, position.y, position.x); break;
                default: result = position; break;
            }
        }

        // -------------------------------------------------------------------
        // Cell Stride
        // -------------------------------------------------------------------

        /// <summary>
        /// Gets the stride between cells (cellSize + cellGap).
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="result">The cell stride.</param>
        [BurstCompile]
        public static void GetCellStride(in GridData gridData, out float3 result)
        {
            result = gridData.cellSize + gridData.cellGap;
        }

        /// <summary>
        /// Gets the inverse of the cell stride with epsilon-guarded reciprocal.
        /// Returns zero for components where the stride is near zero.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="result">The inverse cell stride.</param>
        [BurstCompile]
        public static void GetInvCellStride(in GridData gridData, out float3 result)
        {
            GetCellStride(in gridData, out var stride);
            result = math.select(math.rcp(stride), float3.zero, math.abs(stride) < kGridEpsilon);
        }

        // -------------------------------------------------------------------
        // Cell to Local
        // -------------------------------------------------------------------

        /// <summary>
        /// Converts an integer cell position to local space.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="cellPosition">The integer cell position.</param>
        /// <param name="result">The local space position.</param>
        [BurstCompile]
        public static void CellToLocal(in GridData gridData, in int3 cellPosition, out float3 result)
        {
            float3 floatPos = (float3)cellPosition;
            CellToLocalInterpolated(in gridData, in floatPos, out result);
        }

        /// <summary>
        /// Converts a floating-point cell position to local space.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="cellPosition">The floating-point cell position.</param>
        /// <param name="result">The local space position.</param>
        [BurstCompile]
        public static void CellToLocalInterpolated(in GridData gridData, in float3 cellPosition, out float3 result)
        {
            CellToLocalForLayout(in gridData, in cellPosition, out var layoutLocal);
            CellSwizzle(gridData.cellSwizzle, in layoutLocal, out result);
        }

        // -------------------------------------------------------------------
        // Local to Cell
        // -------------------------------------------------------------------

        /// <summary>
        /// Converts a local space position to an integer cell position.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="localPosition">The local space position.</param>
        /// <param name="result">The integer cell position.</param>
        [BurstCompile]
        public static void LocalToCell(in GridData gridData, in float3 localPosition, out int3 result)
        {
            LocalToCellInterpolated(in gridData, in localPosition, out var cellPosition);
            CellRoundForLayout(in gridData, in cellPosition, out result);
        }

        /// <summary>
        /// Converts a local space position to a floating-point cell position.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="localPosition">The local space position.</param>
        /// <param name="result">The floating-point cell position.</param>
        [BurstCompile]
        public static void LocalToCellInterpolated(in GridData gridData, in float3 localPosition, out float3 result)
        {
            InverseCellSwizzle(gridData.cellSwizzle, in localPosition, out var position);
            LocalToCellForLayout(in gridData, in position, out result);
        }

        // -------------------------------------------------------------------
        // Local to World / World to Local
        // -------------------------------------------------------------------

        /// <summary>
        /// Transforms a position from local space to world space.
        /// </summary>
        /// <param name="transformHandle">The TransformHandle for the Grid.</param>
        /// <param name="localPosition">The local space position.</param>
        /// <param name="result">The world space position.</param>
        public static void LocalToWorld(ref TransformHandle transformHandle, in float3 localPosition, out float3 result)
        {
            result = transformHandle.TransformPoint(localPosition);
        }

        /// <summary>
        /// Transforms a position from world space to local space.
        /// </summary>
        /// <param name="transformHandle">The TransformHandle for the Grid.</param>
        /// <param name="worldPosition">The world space position.</param>
        /// <param name="result">The local space position.</param>
        public static void WorldToLocal(ref TransformHandle transformHandle, in float3 worldPosition, out float3 result)
        {
            result = transformHandle.InverseTransformPoint(worldPosition);
        }

        // -------------------------------------------------------------------
        // Cell to World / World to Cell
        // -------------------------------------------------------------------

        /// <summary>
        /// Converts an integer cell position to world space.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="transformHandle">The TransformHandle for the Grid.</param>
        /// <param name="cellPosition">The integer cell position.</param>
        /// <param name="result">The world space position.</param>
        public static void CellToWorld(in GridData gridData, ref TransformHandle transformHandle, in int3 cellPosition, out float3 result)
        {
            CellToLocal(in gridData, in cellPosition, out var localPosition);
            LocalToWorld(ref transformHandle, in localPosition, out result);
        }

        /// <summary>
        /// Converts a world space position to an integer cell position.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="transformHandle">The TransformHandle for the Grid.</param>
        /// <param name="worldPosition">The world space position.</param>
        /// <param name="result">The integer cell position.</param>
        public static void WorldToCell(in GridData gridData, ref TransformHandle transformHandle, in float3 worldPosition, out int3 result)
        {
            WorldToLocal(ref transformHandle, in worldPosition, out var localPosition);
            LocalToCell(in gridData, in localPosition, out result);
        }

        // -------------------------------------------------------------------
        // Bounds
        // -------------------------------------------------------------------

        /// <summary>
        /// Gets the local-space bounds for a cell position.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="cellPosition">The cell position.</param>
        /// <param name="center">Output bounds center.</param>
        /// <param name="extents">Output bounds extents (half-size).</param>
        [BurstCompile]
        public static void GetBoundsLocal(in GridData gridData, in int3 cellPosition, out float3 center, out float3 extents)
        {
            CellToLocal(in gridData, in cellPosition, out center);
            float3 halfSize = gridData.cellSize * 0.5f;
            CellSwizzle(gridData.cellSwizzle, in halfSize, out extents);
        }

        /// <summary>
        /// Gets the local-space bounds for a region defined by origin and size.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="origin">The origin of the region in cell space.</param>
        /// <param name="size">The size of the region in cells.</param>
        /// <param name="center">Output bounds center.</param>
        /// <param name="extents">Output bounds extents (half-size).</param>
        [BurstCompile]
        public static void GetBoundsLocal(in GridData gridData, in float3 origin, in float3 size, out float3 center, out float3 extents)
        {
            CellLocalBoundsForLayout(in gridData, in origin, in size, out var boundsSize);
            float3 halfBounds = boundsSize * 0.5f;
            CellSwizzle(gridData.cellSwizzle, in halfBounds, out extents);
            CellLocalBoundsOriginForLayout(in gridData, in origin, in size, out var boundsOrigin);
            CellSwizzle(gridData.cellSwizzle, in boundsOrigin, out center);
            center += extents;
        }

        // -------------------------------------------------------------------
        // Cell Center
        // -------------------------------------------------------------------

        /// <summary>
        /// Gets the cell center offset in cell space for the current layout.
        /// Rectangle/Isometric/IsometricZAsY: (0.5, 0.5, 0.5). Hexagon: (0, 0, 0).
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="result">The cell center in cell space.</param>
        [BurstCompile]
        public static void GetLayoutCellCenter(in GridData gridData, out float3 result)
        {
            if (gridData.cellLayout == GridData.Layout.Hexagon)
                result = float3.zero;
            else
                result = new float3(0.5f, 0.5f, 0.5f);
        }

        /// <summary>
        /// Gets the number of vertices that define a cell outline.
        /// Hexagon: 6. All others: 4.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <returns>The number of cell outline points.</returns>
        [BurstCompile]
        public static int GetPointCount(in GridData gridData)
        {
            if (gridData.cellLayout == GridData.Layout.Hexagon)
                return 6;
            return 4;
        }

        /// <summary>
        /// Gets the cell center position in local space for an integer cell position.
        /// </summary>
        /// <param name="gridData">Grid configuration data.</param>
        /// <param name="position">The integer cell position.</param>
        /// <param name="result">The cell center in local space.</param>
        [BurstCompile]
        public static void GetCellCenterLocal(in GridData gridData, in int3 position, out float3 result)
        {
            float3 cs = gridData.cellSize;
            GetInvCellStride(in gridData, out var ics);
            GetLayoutCellCenter(in gridData, out var layoutCenter);
            float3 relativeCellCenter = new float3(
                layoutCenter.x * cs.x * ics.x,
                layoutCenter.y * cs.y * ics.y,
                layoutCenter.z * cs.z * ics.z
            );
            float3 floatPos = (float3)position;
            CellToLocalInterpolated(in gridData, in floatPos, out var basePos);
            CellToLocalInterpolated(in gridData, in relativeCellCenter, out var centerOffset);
            result = basePos + centerOffset;
        }

        // -------------------------------------------------------------------
        // Private: Per-Layout CellToLocal
        // -------------------------------------------------------------------

        [BurstCompile]
        static void CellToLocalForLayout(in GridData gridData, in float3 position, out float3 result)
        {
            switch (gridData.cellLayout)
            {
                case GridData.Layout.Rectangle:
                    CellToLocalRectangle(in gridData, in position, out result); break;
                case GridData.Layout.Hexagon:
                    CellToLocalHexagonal(in gridData, in position, out result); break;
                case GridData.Layout.Isometric:
                    CellToLocalIsometric(in gridData, in position, out result); break;
                case GridData.Layout.IsometricZAsY:
                    CellToLocalIsometricZAsY(in gridData, in position, out result); break;
                default:
                    CellToLocalRectangle(in gridData, in position, out result); break;
            }
        }

        [BurstCompile]
        static void CellToLocalRectangle(in GridData gridData, in float3 position, out float3 result)
        {
            GetCellStride(in gridData, out var stride);
            result = position * stride;
        }

        [BurstCompile]
        static void CellToLocalHexagonal(in GridData gridData, in float3 position, out float3 result)
        {
            float3 cellSize = gridData.cellSize;
            float3 cellGap = gridData.cellGap;
            float3 cellStride = new float3(
                cellSize.x + cellGap.x,
                cellSize.y * kHexVerticalDistance + cellGap.y,
                cellSize.z + cellGap.z
            );
            float oddRowOffset = (float)((int)position.y & 1) * cellStride.x * 0.5f;
            result = new float3(
                position.x * cellStride.x + oddRowOffset,
                position.y * cellStride.y,
                position.z * cellStride.z
            );
        }

        [BurstCompile]
        static void CellToLocalIsometric(in GridData gridData, in float3 position, out float3 result)
        {
            float3 cellSize = gridData.cellSize;
            float3 cellGap = gridData.cellGap;
            float3 isoCellStride = (cellSize + cellGap) * 0.5f;
            float3 cellStride = new float3(isoCellStride.x, isoCellStride.y, cellSize.z + cellGap.z);
            result = new float3(
                (position.x - position.y) * cellStride.x,
                (position.x + position.y) * cellStride.y,
                position.z * cellStride.z
            );
        }

        [BurstCompile]
        static void CellToLocalIsometricZAsY(in GridData gridData, in float3 position, out float3 result)
        {
            float3 cellSize = gridData.cellSize;
            float3 cellGap = gridData.cellGap;
            float3 isoCellStride = (cellSize + cellGap) * 0.5f;
            float3 cellStride = new float3(isoCellStride.x, isoCellStride.y, cellSize.z + cellGap.z);
            result = new float3(
                (position.x - position.y) * cellStride.x,
                (position.x + position.y + position.z * cellSize.z) * cellStride.y,
                position.z * cellStride.z
            );
        }

        // -------------------------------------------------------------------
        // Private: Per-Layout LocalToCell
        // -------------------------------------------------------------------

        [BurstCompile]
        static void LocalToCellForLayout(in GridData gridData, in float3 position, out float3 result)
        {
            switch (gridData.cellLayout)
            {
                case GridData.Layout.Rectangle:
                    LocalToCellRectangle(in gridData, in position, out result); break;
                case GridData.Layout.Hexagon:
                    LocalToCellHexagonal(in gridData, in position, out result); break;
                case GridData.Layout.Isometric:
                    LocalToCellIsometric(in gridData, in position, out result); break;
                case GridData.Layout.IsometricZAsY:
                    LocalToCellIsometricZAsY(in gridData, in position, out result); break;
                default:
                    LocalToCellRectangle(in gridData, in position, out result); break;
            }
        }

        [BurstCompile]
        static void LocalToCellRectangle(in GridData gridData, in float3 position, out float3 result)
        {
            GetCellStride(in gridData, out var cellStride);
            float3 invCellStride = math.select(math.rcp(cellStride), float3.zero, math.abs(cellStride) < kGridEpsilon);
            result = position * invCellStride;
        }

        [BurstCompile]
        static void LocalToCellHexagonal(in GridData gridData, in float3 position, out float3 result)
        {
            float3 cellSize = gridData.cellSize;
            float3 cellGap = gridData.cellGap;
            float3 cellStride = new float3(
                cellSize.x + cellGap.x,
                cellSize.y * kHexVerticalDistance + cellGap.y,
                cellSize.z + cellGap.z
            );
            float3 invCellStride = math.select(math.rcp(cellStride), float3.zero, math.abs(cellStride) < kGridEpsilon);

            result.z = position.z * invCellStride.z;
            result.y = position.y * invCellStride.y;
            float oddRowOffset = (float)((int)result.y & 1) * cellStride.x * 0.5f;
            result.x = (position.x - oddRowOffset) * invCellStride.x;
        }

        [BurstCompile]
        static void LocalToCellIsometric(in GridData gridData, in float3 position, out float3 result)
        {
            float3 cellSize = gridData.cellSize;
            float3 cellGap = gridData.cellGap;
            float3 isoCellStride = (cellSize + cellGap) * 0.5f;
            float3 cellStride = new float3(isoCellStride.x, isoCellStride.y, cellSize.z + cellGap.z);
            float3 invCellStride = math.select(math.rcp(cellStride), float3.zero, math.abs(cellStride) < kGridEpsilon);

            result = position * invCellStride;
            result.y = (result.y - result.x) * 0.5f;
            result.x += result.y;
        }

        [BurstCompile]
        static void LocalToCellIsometricZAsY(in GridData gridData, in float3 position, out float3 result)
        {
            float3 cellSize = gridData.cellSize;
            float3 cellGap = gridData.cellGap;
            float3 isoCellStride = (cellSize + cellGap) * 0.5f;
            float3 cellStride = new float3(isoCellStride.x, isoCellStride.y, cellSize.z + cellGap.z);
            float3 invCellStride = math.select(math.rcp(cellStride), float3.zero, math.abs(cellStride) < kGridEpsilon);

            result = position * invCellStride;
            result.y = ((result.y - result.z * cellSize.z) - result.x) * 0.5f;
            result.x += result.y;
        }

        // -------------------------------------------------------------------
        // Private: Per-Layout CellRound
        // -------------------------------------------------------------------

        [BurstCompile]
        static void CellRoundForLayout(in GridData gridData, in float3 cellPosition, out int3 result)
        {
            if (gridData.cellLayout == GridData.Layout.Hexagon)
                CellRoundHexagonal(in cellPosition, out result);
            else
                CellRoundDefault(in cellPosition, out result);
        }

        [BurstCompile]
        static void CellRoundDefault(in float3 position, out int3 result)
        {
            result = (int3)math.floor(position + kGridEpsilon);
        }

        [BurstCompile]
        static void CellRoundHexagonal(in float3 cellPosition, out int3 result)
        {
            // Convert offset coordinates to cube coordinates
            float3 cube;
            cube.x = cellPosition.x - (cellPosition.y - (float)((int)cellPosition.y & 1)) * 0.5f;
            cube.z = cellPosition.y;
            cube.y = -cube.x - cube.z;

            // Round in cube space
            float3 cubeRound = math.round(cube);
            float3 cubeDiff = math.abs(cubeRound - cube);

            // Fix the constraint x + y + z = 0 by adjusting the component with the largest rounding error
            if (cubeDiff.x > cubeDiff.y && cubeDiff.x > cubeDiff.z)
            {
                cubeRound.x = -cubeRound.y - cubeRound.z;
            }
            else if (cubeDiff.y > cubeDiff.z)
            {
                cubeRound.y = -cubeRound.x - cubeRound.z;
            }
            else
            {
                cubeRound.z = -cubeRound.x - cubeRound.y;
            }

            // Convert back to offset coordinates
            float3 offset;
            offset.x = cubeRound.x + (cubeRound.z - (float)((int)cubeRound.z & 1)) * 0.5f;
            offset.y = cubeRound.z;
            offset.z = math.floor(cellPosition.z + 0.5f);
            result = (int3)offset;
        }

        // -------------------------------------------------------------------
        // Private: Per-Layout Bounds
        // -------------------------------------------------------------------

        [BurstCompile]
        static void CellLocalBoundsForLayout(in GridData gridData, in float3 origin, in float3 size, out float3 result)
        {
            switch (gridData.cellLayout)
            {
                case GridData.Layout.Hexagon:
                    CellLocalBoundsHexagonal(in gridData, in size, out result); break;
                case GridData.Layout.Isometric:
                    CellLocalBoundsIsometric(in gridData, in size, out result); break;
                case GridData.Layout.IsometricZAsY:
                    CellLocalBoundsIsometricZAsY(in gridData, in size, out result); break;
                default:
                    CellLocalBoundsRectangle(in gridData, in size, out result); break;
            }
        }

        [BurstCompile]
        static void CellLocalBoundsOriginForLayout(in GridData gridData, in float3 origin, in float3 size, out float3 result)
        {
            switch (gridData.cellLayout)
            {
                case GridData.Layout.Hexagon:
                    CellLocalBoundsOriginHexagonal(in gridData, in origin, in size, out result); break;
                case GridData.Layout.Isometric:
                case GridData.Layout.IsometricZAsY:
                    CellLocalBoundsOriginIsometric(in gridData, in origin, in size, out result); break;
                default:
                    CellToLocalForLayout(in gridData, in origin, out result); break;
            }
        }

        [BurstCompile]
        static void CellLocalBoundsRectangle(in GridData gridData, in float3 size, out float3 result)
        {
            GetCellStride(in gridData, out var stride);
            result = stride * size;
        }

        [BurstCompile]
        static void CellLocalBoundsHexagonal(in GridData gridData, in float3 size, out float3 result)
        {
            float3 hexSize = size;
            if (hexSize.y > 1f)
            {
                hexSize.x += 0.5f;
                hexSize.y = (hexSize.y - 1f) * kHexVerticalDistance + 1f;
            }
            GetCellStride(in gridData, out var stride);
            result = stride * hexSize;
        }

        [BurstCompile]
        static void CellLocalBoundsOriginHexagonal(in GridData gridData, in float3 origin, in float3 size, out float3 result)
        {
            bool needOffset = size.y > 1f && ((int)origin.y & 1) != 0;
            GetCellStride(in gridData, out var stride);
            float3 xDelta = new float3(needOffset ? 1f : 0.5f, 0.5f, 0f) * stride;
            CellToLocalHexagonal(in gridData, in origin, out var o);
            result = o - xDelta;
        }

        [BurstCompile]
        static void CellLocalBoundsIsometric(in GridData gridData, in float3 size, out float3 result)
        {
            float widthAndHeight = 0.5f * (size.x + size.y);
            float3 boundsSize = new float3(widthAndHeight, widthAndHeight, size.z);
            GetCellStride(in gridData, out var stride);
            result = stride * boundsSize;
        }

        [BurstCompile]
        static void CellLocalBoundsIsometricZAsY(in GridData gridData, in float3 size, out float3 result)
        {
            float widthAndHeight = 0.5f * (size.x + size.y);
            float3 boundsSize = new float3(widthAndHeight, widthAndHeight, size.z);
            boundsSize.y += (size.z - 1f) * gridData.cellSize.z;
            GetCellStride(in gridData, out var stride);
            result = stride * boundsSize;
        }

        [BurstCompile]
        static void CellLocalBoundsOriginIsometric(in GridData gridData, in float3 origin, in float3 size, out float3 result)
        {
            GetCellStride(in gridData, out var stride);
            float3 originOffset = new float3(0.5f * size.y, 0f, 0f) * stride;
            CellToLocalForLayout(in gridData, in origin, out var o);
            result = o - originOffset;
        }
    }
}
