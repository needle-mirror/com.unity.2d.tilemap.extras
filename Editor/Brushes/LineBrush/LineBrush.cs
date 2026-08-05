using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace UnityEditor.Tilemaps
{
    /// <summary>
    ///     This Brush helps draw lines of Tiles onto a Tilemap.
    ///     Use this as an example to modify brush painting behaviour to making painting quicker with less actions.
    /// </summary>
    [HelpURL(
        "https://docs.unity3d.com/Packages/com.unity.2d.tilemap.extras@latest/index.html?subfolder=/manual/LineBrush.html")]
    [CustomGridBrush(true, false, false, "Line Brush")]
    public class LineBrush : GridBrush
    {
        /// <summary>
        ///     Ensures that there are orthogonal connections of Tiles from the start of the line to the end.
        /// </summary>
        public bool fillGaps;

        /// <summary>
        ///     The current starting point of the line.
        /// </summary>
        public Vector3Int lineStart = Vector3Int.zero;

        /// <summary>
        ///     Whether the Line Brush has started drawing a line.
        /// </summary>
        [NonSerialized] public bool lineStartActive;

        /// <summary>
        ///     Indicates whether the brush is currently
        ///     moving something using the "Move selection with active brush" tool.
        /// </summary>
        public bool IsMoving { get; private set; }

        private void OnEnable()
        {
            lineStartActive = false;
        }

        /// <summary>
        ///     Paints tiles and GameObjects into a given position within the selected layers.
        ///     The LineBrush overrides this to provide line painting functionality.
        ///     The first paint action sets the starting point of the line.
        ///     The next paint action sets the ending point of the line and paints Tile from start to end.
        /// </summary>
        /// <param name="grid">Grid used for layout.</param>
        /// <param name="brushTarget">Target of the paint operation. By default the currently selected GameObject.</param>
        /// <param name="position">The coordinates of the cell to paint data to.</param>
        public override void Paint(GridLayout grid, GameObject brushTarget, Vector3Int position)
        {
            if (lineStartActive)
            {
                if (Event.current != null &&
                    Event.current.type == EventType.MouseDrag)
                    return;

                var startPos = new Vector2Int(lineStart.x, lineStart.y);
                var endPos = new Vector2Int(position.x, position.y);
                if (startPos == endPos)
                    base.Paint(grid, brushTarget, position);
                else
                {
                    foreach (var point in GetPointsOnLine(startPos, endPos, fillGaps))
                    {
                        var paintPos = new Vector3Int(point.x, point.y, position.z);
                        base.Paint(grid, brushTarget, paintPos);
                    }
                }
                lineStartActive = false;
            }
            else if (IsMoving)
            {
                base.Paint(grid, brushTarget, position);
            }
            else
            {
                lineStart = position;
                lineStartActive = true;
            }
        }

        /// <summary>
        ///     Starts the movement of tiles and GameObjects from a given position within the selected layers.
        /// </summary>
        /// <param name="grid">Grid used for layout.</param>
        /// <param name="brushTarget">Target of the Move operation. By default the currently selected GameObject.</param>
        /// <param name="position">The coordinates of the cell to move data from.</param>
        public override void MoveStart(GridLayout grid, GameObject brushTarget, BoundsInt position)
        {
            base.MoveStart(grid, brushTarget, position);
            IsMoving = true;
        }

        /// <summary>
        ///     Ends the movement of tiles and GameObjects to a given position within the selected layers.
        /// </summary>
        /// <param name="grid">Grid used for layout.</param>
        /// <param name="brushTarget">Target of the Move operation. By default the currently selected GameObject.</param>
        /// <param name="position">The coordinates of the cell to move data to.</param>
        public override void MoveEnd(GridLayout grid, GameObject brushTarget, BoundsInt position)
        {
            base.MoveEnd(grid, brushTarget, position);
            IsMoving = false;
        }

        /// <summary>
        ///     Enumerates all the points between the start and end position which are
        ///     linked diagonally or orthogonally.
        /// </summary>
        /// <param name="startPos">Start position of the line.</param>
        /// <param name="endPos">End position of the line.</param>
        /// <param name="fillGaps">
        ///     Fills any gaps between the start and end position so that
        ///     all points are linked only orthogonally.
        /// </param>
        /// <returns>
        ///     Returns an IEnumerable which enumerates all the points between the start and end position which are
        ///     linked diagonally or orthogonally.
        /// </returns>
        public static IEnumerable<Vector2Int> GetPointsOnLine(Vector2Int startPos, Vector2Int endPos, bool fillGaps)
        {
            var points = GetPointsOnLine(startPos, endPos);
            if (fillGaps)
            {
                var rise = endPos.y - startPos.y;
                var run = endPos.x - startPos.x;

                if (rise != 0 || run != 0)
                {
                    var extraStart = startPos;
                    var extraEnd = endPos;


                    if (Mathf.Abs(rise) >= Mathf.Abs(run))
                    {
                        // up
                        if (rise > 0)
                        {
                            extraStart.y += 1;
                            extraEnd.y += 1;
                        }
                        // down
                        else // rise < 0
                        {
                            extraStart.y -= 1;
                            extraEnd.y -= 1;
                        }
                    }
                    else // Mathf.Abs(rise) < Mathf.Abs(run)
                    {
                        // right
                        if (run > 0)
                        {
                            extraStart.x += 1;
                            extraEnd.x += 1;
                        }
                        // left
                        else // run < 0
                        {
                            extraStart.x -= 1;
                            extraEnd.x -= 1;
                        }
                    }

                    var extraPoints = GetPointsOnLine(extraStart, extraEnd);
                    extraPoints = extraPoints.Except(new[] { extraEnd });
                    points = points.Union(extraPoints);
                }
            }

            return points;
        }

        /// <summary>
        ///     Gets an enumerable for all the cells directly between two points
        ///     http://ericw.ca/notes/bresenhams-line-algorithm-in-csharp.html
        /// </summary>
        /// <param name="p1">A starting point of a line</param>
        /// <param name="p2">An ending point of a line</param>
        /// <returns>Gets an enumerable for all the cells directly between two points</returns>
        public static IEnumerable<Vector2Int> GetPointsOnLine(Vector2Int p1, Vector2Int p2)
        {
            var x0 = p1.x;
            var y0 = p1.y;
            var x1 = p2.x;
            var y1 = p2.y;

            var steep = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
            if (steep)
            {
                int t;
                t = x0; // swap x0 and y0
                x0 = y0;
                y0 = t;
                t = x1; // swap x1 and y1
                x1 = y1;
                y1 = t;
            }

            if (x0 > x1)
            {
                int t;
                t = x0; // swap x0 and x1
                x0 = x1;
                x1 = t;
                t = y0; // swap y0 and y1
                y0 = y1;
                y1 = t;
            }

            var dx = x1 - x0;
            var dy = Math.Abs(y1 - y0);
            var error = dx / 2;
            var ystep = y0 < y1 ? 1 : -1;
            var y = y0;
            for (var x = x0; x <= x1; x++)
            {
                yield return new Vector2Int(steep ? y : x, steep ? x : y);
                error = error - dy;
                if (error < 0)
                {
                    y += ystep;
                    error += dx;
                }
            }
        }
    }

    /// <summary>
    ///     The Brush Editor for a Line Brush.
    /// </summary>
    [CustomEditor(typeof(LineBrush))]
    public class LineBrushEditor : GridBrushEditor
    {
        private static readonly string iconPath =
            "Packages/com.unity.2d.tilemap.extras/Editor/Brushes/LineBrush/LineBrush.png";

        private Tilemap lastTilemap;

        private Texture2D m_BrushIcon;
        private LineBrush lineBrush => target as LineBrush;

        private Vector3[] startCells = new Vector3[4];

        /// <summary> Returns an icon identifying the Line Brush. </summary>
        public override Texture2D icon
        {
            get
            {
                if (m_BrushIcon == null)
                {
                    var gui = EditorGUIUtility.TrIconContent(iconPath);
                    m_BrushIcon = gui.image as Texture2D;
                }

                return m_BrushIcon;
            }
        }

        /// <summary>
        ///     Callback for handling the GUI events for the GridBrush in the Scene View.
        ///     The LineBrush Editor overrides this cancel drawing lines with the right mouse button.
        /// </summary>
        /// <param name="gridLayout">Grid that the brush is being used on.</param>
        /// <param name="brushTarget">Target of the GridBrushBase::ref::Tool operation.
        ///     By default the currently selected GameObject.</param>
        public override void OnSceneGUI(GridLayout gridLayout, GameObject brushTarget)
        {
            base.OnSceneGUI(gridLayout, brushTarget);

            if (!lineBrush.lineStartActive)
                return;

            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 1)
            {
                lineBrush.lineStartActive = false;
                Event.current.Use();
                return;
            }
        }

        /// <summary>
        ///     Callback for painting the GUI for the GridBrush in the Scene View.
        ///     The LineBrush Editor overrides this to draw the preview of the brush when drawing lines.
        /// </summary>
        /// <param name="grid">Grid that the brush is being used on.</param>
        /// <param name="brushTarget">
        ///     Target of the GridBrushBase::ref::Tool operation. By default the currently selected
        ///     GameObject.
        /// </param>
        /// <param name="position">Current selected location of the brush.</param>
        /// <param name="tool">Current GridBrushBase::ref::Tool selected.</param>
        /// <param name="executing">Whether brush is being used.</param>
        public override void OnPaintSceneGUI(GridLayout grid, GameObject brushTarget, BoundsInt position,
            GridBrushBase.Tool tool, bool executing)
        {
            base.OnPaintSceneGUI(grid, brushTarget, position, tool, executing);
            if (brushTarget != null)
            {
                var tilemap = brushTarget.GetComponent<Tilemap>();
                if (tilemap != null)
                {
                    tilemap.ClearAllEditorPreviewTiles();
                    lastTilemap = tilemap;
                }

                if (!lineBrush.lineStartActive)
                    return;

                // Draw preview tiles for tilemap
                var startPos = new Vector2Int(lineBrush.lineStart.x, lineBrush.lineStart.y);
                var endPos = new Vector2Int(position.x, position.y);
                if (startPos == endPos)
                    PaintPreview(grid, brushTarget, position.min);
                else
                {
                    foreach (var point in LineBrush.GetPointsOnLine(startPos, endPos, lineBrush.fillGaps))
                    {
                        var paintPos = new Vector3Int(point.x, point.y, position.z);
                        PaintPreview(grid, brushTarget, paintPos);
                    }
                }

                if (Event.current.type == EventType.Repaint)
                {
                    GL.PushMatrix();
                    GL.MultMatrix(tilemap.transform.localToWorldMatrix);
                    GL.Begin(GL.LINES);
                    GL.Color(Color.blue);

                    var startArea = new BoundsInt(lineBrush.lineStart, position.size);
                    if (grid.cellLayout == GridLayout.CellLayout.Hexagon)
                    {
                        var cellLocals = GetHexOutlineVertices(grid, startArea);
                        if (cellLocals.IsCreated)
                        {
                            for (int i = 0, j = cellLocals.Length - 1; i < cellLocals.Length; j = i++)
                            {
                                GL.Vertex3(cellLocals[j].x, cellLocals[j].y, cellLocals[j].z);
                                GL.Vertex3(cellLocals[i].x, cellLocals[i].y, cellLocals[i].z);
                            }
                            cellLocals.Dispose();
                        }
                    }
                    else
                    {
                        var min = lineBrush.lineStart;
                        var max = lineBrush.lineStart + position.size;

                        var cellStride = tilemap.cellSize + tilemap.cellGap;
                        var cellGap = Vector3.one;
                        if (!Mathf.Approximately(cellStride.x, 0f))
                        {
                            cellGap.x = tilemap.cellSize.x / cellStride.x;
                        }
                        if (!Mathf.Approximately(cellStride.y, 0f))
                        {
                            cellGap.y = tilemap.cellSize.y / cellStride.y;
                        }

                        startCells[0] = tilemap.CellToLocalInterpolated(new Vector3(min.x, min.y, min.z));
                        startCells[1] = tilemap.CellToLocalInterpolated(new Vector3(max.x - 1 + cellGap.x, min.y, min.z));
                        startCells[2] = tilemap.CellToLocalInterpolated(new Vector3(max.x - 1 + cellGap.x, max.y - 1 + cellGap.y, min.z));
                        startCells[3] = tilemap.CellToLocalInterpolated(new Vector3(min.x, max.y - 1 + cellGap.y, min.z));

                        // Draws a box on the picked starting position
                        int i = 0;
                        for (int j = startCells.Length - 1; i < startCells.Length; j = i++)
                        {
                            GL.Vertex(startCells[j]);
                            GL.Vertex(startCells[i]);
                        }
                    }
                    GL.End();
                    GL.PopMatrix();
                }
            }
        }

        internal static NativeArray<Vector3> GetHexOutlineVertices(GridLayout grid, BoundsInt area)
        {
            int requiredVertices = 4 * (area.size.x + area.size.y) - 2;
            if (requiredVertices <= 0)
                return default;

            var cellLocals = new NativeArray<Vector3>(requiredVertices, Allocator.Temp);
            int horizontalCount = area.size.x * 2;
            int verticalCount = area.size.y * 2 - 1;
            int bottom = 0;
            int top = horizontalCount + verticalCount + horizontalCount - 1;
            int left = requiredVertices - 1;
            int right = horizontalCount;

            var cellOffset = new NativeArray<Vector3>(6, Allocator.Temp);
            cellOffset[0] = Grid.Swizzle(grid.cellSwizzle, new Vector3(0, grid.cellSize.y / 2, 0));
            cellOffset[1] = Grid.Swizzle(grid.cellSwizzle, new Vector3(grid.cellSize.x / 2, grid.cellSize.y / 4, 0));
            cellOffset[2] = Grid.Swizzle(grid.cellSwizzle, new Vector3(grid.cellSize.x / 2, -grid.cellSize.y / 4, 0));
            cellOffset[3] = Grid.Swizzle(grid.cellSwizzle, new Vector3(0, -grid.cellSize.y / 2, 0));
            cellOffset[4] = Grid.Swizzle(grid.cellSwizzle, new Vector3(-grid.cellSize.x / 2, -grid.cellSize.y / 4, 0));
            cellOffset[5] = Grid.Swizzle(grid.cellSwizzle, new Vector3(-grid.cellSize.x / 2, grid.cellSize.y / 4, 0));

            for (int x = area.min.x; x < area.max.x; x++)
            {
                cellLocals[bottom++] = grid.CellToLocal(new Vector3Int(x, area.min.y, area.zMin)) + cellOffset[4];
                cellLocals[bottom++] = grid.CellToLocal(new Vector3Int(x, area.min.y, area.zMin)) + cellOffset[3];
                cellLocals[top--] = grid.CellToLocal(new Vector3Int(x, area.max.y - 1, area.zMin)) + cellOffset[0];
                cellLocals[top--] = grid.CellToLocal(new Vector3Int(x, area.max.y - 1, area.zMin)) + cellOffset[1];
            }
            cellLocals[left--] = grid.CellToLocal(new Vector3Int(area.min.x, area.min.y, area.zMin)) + cellOffset[5];
            cellLocals[top--] = grid.CellToLocal(new Vector3Int(area.max.x - 1, area.max.y - 1, area.zMin)) + cellOffset[2];
            for (int y = area.min.y + 1; y < area.max.y; y++)
            {
                cellLocals[left--] = grid.CellToLocal(new Vector3Int(area.min.x, y, area.zMin)) + cellOffset[4];
                cellLocals[left--] = grid.CellToLocal(new Vector3Int(area.min.x, y, area.zMin)) + cellOffset[5];
            }
            for (int y = area.min.y; y < area.max.y - 1; y++)
            {
                cellLocals[right++] = grid.CellToLocal(new Vector3Int(area.max.x - 1, y, area.zMin)) + cellOffset[2];
                cellLocals[right++] = grid.CellToLocal(new Vector3Int(area.max.x - 1, y, area.zMin)) + cellOffset[1];
            }
            cellOffset.Dispose();
            return cellLocals;
        }

        /// <summary>
        ///     Clears all line previews.
        /// </summary>
        public override void ClearPreview()
        {
            base.ClearPreview();
            if (lastTilemap != null)
            {
                lastTilemap.ClearAllEditorPreviewTiles();
                lastTilemap = null;
            }
        }
    }
}
