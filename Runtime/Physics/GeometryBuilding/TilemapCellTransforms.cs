using System;

using Unity.Mathematics;
using Unity.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.U2D.Physics
{
    // Every transformation a tilemap area's geometry goes through, from a Grid's unit cell to the Tilemap's local space, in one
    // place so each step can be tested on its own. The builders only call these; none of them places geometry by hand.
    //
    // Grid cells:   unit cell points (Grid.GetCellPoints) -> cell size and swizzle -> at CellToLocal(cell) -> Tilemap local space.
    // Sprite cells: sprite outline about its pivot -> the tile's transform -> at GetCellCenterLocal(cell) -> Tilemap local space.
    //
    // From there to the body is PhysicsArea's, which hands GenerateShapes the matrix, the same as for every other area.
    internal static class TilemapCellTransforms
    {
        // The outline every Grid cell shares, relative to a cell's position: GetCellPoints scaled by the cell size and swizzled, the same cell TilemapCollider2D uses.
        // Wound anti-clockwise; a swizzle that exchanges axes mirrors the Grid's clockwise points, so the winding is checked.
        // Null when the map has no usable cell shape.
        // Written into 'reuse' when it is the right length, so a caller working the shape out on every build allocates nothing.
        public static Vector2[] GetGridCellShape(Tilemap map, Vector2[] reuse = null)
        {
            var grid = map == null ? null : map.layoutGrid;
            if (grid == null)
                return null;

            var cellSize = grid.cellSize;
            if (cellSize.x <= 0f || cellSize.y <= 0f)
                return null;

            var unitPoints = grid.GetCellPoints();
            if (unitPoints.Length < 3)
                return null;

            var swizzle = grid.cellSwizzle;

            var count = unitPoints.Length;
            var points = reuse != null && reuse.Length == count ? reuse : new Vector2[count];
            for (var i = 0; i < count; ++i)
                points[i] = Grid.Swizzle(swizzle, Vector3.Scale(unitPoints[i], cellSize));

            if (SignedArea(points) < 0f)
                Array.Reverse(points);

            return points;
        }

        // Whether GetGridCellShape would return a cell shape for the map, worked out without building the shape, so asking allocates nothing.
        public static bool HasGridCellShape(Tilemap map)
        {
            var grid = map == null ? null : map.layoutGrid;
            if (grid == null)
                return false;

            var cellSize = grid.cellSize;
            return cellSize.x > 0f && cellSize.y > 0f && grid.GetCellPoints().Length >= 3;
        }

        // Twice the enclosed area, signed: positive is anti-clockwise. Only the sign is used.
        private static float SignedArea(Vector2[] points)
        {
            var area = 0f;
            for (var i = 0; i < points.Length; ++i)
            {
                var next = points[(i + 1) % points.Length];
                area += points[i].x * next.y - next.x * points[i].y;
            }

            return area;
        }

        // Place a cell shape from GetGridCellShape at a cell, in the Tilemap's local space. 'placed' must be as long as 'shape'.
        public static void PlaceGridCell(Tilemap map, Vector3Int cell, ReadOnlySpan<Vector2> shape, Span<Vector2> placed) =>
            PlaceGridCell(map.CellToLocal(cell), shape, placed);

        // Place a cell shape from GetGridCellShape at a cell's position in the Tilemap's local space, such as one from CellPlacement.
        public static void PlaceGridCell(Vector3 cellLocal, ReadOnlySpan<Vector2> shape, Span<Vector2> placed)
        {
            if (placed.Length < shape.Length)
                throw new ArgumentException("The output is shorter than the shape.", nameof(placed));

            var origin = (Vector2)cellLocal;
            for (var i = 0; i < shape.Length; ++i)
                placed[i] = origin + shape[i];
        }

        // How far a cell's center, which carries the Tile Anchor, sits from the cell's position, in the Tilemap's local space.
        // The same for every cell of a layout, so a sprite outline placed relative to a cell's position can be cached once per tile rather than once per cell.
        public static Vector2 GetCellCenterOffset(Tilemap map) => map.GetCellCenterLocal(Vector3Int.zero) - map.CellToLocal(Vector3Int.zero);

        // Whether every cell of the map can have its outline clipped to its own cell, which needs a usable cell shape and cells that do not overlap.
        // Returns the reason when it cannot, worded for the Inspector.
        public static bool CanClipToCells(Tilemap map, out string reason)
        {
            var grid = map == null ? null : map.layoutGrid;
            if (grid == null || !HasGridCellShape(map))
            {
                reason = "Cell Clipping is not in use because the Grid has no usable cell shape, so the tiles' outlines are used as they are.";
                return false;
            }

            var cellGap = grid.cellGap;
            if (cellGap.x < 0f || cellGap.y < 0f)
            {
                reason = "Cell Clipping is not in use because the Grid's Cell Gap is negative, which makes neighboring cells overlap, so the tiles' outlines are used as they are.";
                return false;
            }

            reason = null;
            return true;
        }

        // The matrix placing a tile's sprite outline at a cell, in the Tilemap's local space: the tile's own transform (its flip,
        // rotation and scale), then the Tilemap's orientation, then the cell centre, which carries the Tile Anchor. The outline is
        // authored about the sprite's pivot, which is what the renderer puts at the cell centre too.
        // An orientation that stands the sprite out of the Tilemap's plane, such as XZ, flattens it to nothing unless the tile's own transform turns it back.
        public static Matrix4x4 GetSpriteCellMatrix(Tilemap map, Vector3Int cell) =>
            GetSpriteCellMatrix(map.GetCellCenterLocal(cell), map.orientationMatrix, map.GetTransformMatrix(cell));

        // The same matrix from a cell center, orientation and tile transform already in hand, so placing a cell makes no call into the Tilemap.
        public static Matrix4x4 GetSpriteCellMatrix(Vector3 cellCenterLocal, Matrix4x4 orientation, Matrix4x4 tileMatrix) =>
            Matrix4x4.Translate(cellCenterLocal) * orientation * tileMatrix;

        // Where every cell of a map sits, computed by GridUtility from Grid settings read once.
        // Sample it again at every build or update.
        public readonly struct CellPlacement
        {
            private CellPlacement(GridData gridData, Vector3 cellCenterOffset)
            {
                m_GridData = gridData;
                m_CellCenterOffset = cellCenterOffset;
            }

            // Read the map's Grid settings and its cell center offset, a handful of calls into the Tilemap however many cells it has.
            public static CellPlacement Sample(Tilemap map) => new(new GridData(map), GetCellCenterOffset(map));

            // The cell's position in the Tilemap's local space, the same as Tilemap.CellToLocal.
            public Vector3 CellToLocal(Vector3Int cell)
            {
                GridUtility.CellToLocal(m_GridData, new int3(cell.x, cell.y, cell.z), out var local);
                return local;
            }

            // The cell's center in the Tilemap's local space, the same as Tilemap.GetCellCenterLocal.
            // GridUtility's own cell center uses the layout's center rather than the Tilemap's Tile Anchor, so the Tilemap's offset is added instead.
            public Vector3 CellCenterToLocal(Vector3Int cell) => CellToLocal(cell) + m_CellCenterOffset;

            #region Internal

            // The Grid's cell size, gap, layout and swizzle, which is all GridUtility needs to place a cell.
            private readonly GridData m_GridData;

            // How far a cell's center, which carries the Tile Anchor, sits from its position.
            private readonly Vector3 m_CellCenterOffset;

            #endregion
        }
    }
}
