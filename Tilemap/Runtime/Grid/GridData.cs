using Unity.Mathematics;
using UnityEngine;

namespace Unity.Tilemaps.Experimental
{
    /// <summary>
    /// Blittable representation of a Grid component's dimensional data, suitable for
    /// use inside Burst-compiled Jobs where managed <see cref="GridLayout"/> references
    /// cannot be accessed. A GridData stores the layout of the grid and provides the
    /// data required to convert between cell space and local space.
    /// </summary>
    public struct GridData
    {
        /// <summary>
        /// The layout of cells in the Grid. Determines how positions are converted
        /// between cell space and local space. Values match <see cref="GridLayout.CellLayout"/>.
        /// </summary>
        public enum Layout
        {
            /// <summary>
            /// Rectangular layout for cells in the Grid.
            /// </summary>
            Rectangle = 0,

            /// <summary>
            /// Hexagonal layout for cells in the Grid.
            /// </summary>
            Hexagon = 1,

            /// <summary>
            /// Isometric layout for cells in the Grid.
            /// </summary>
            Isometric = 2,

            /// <summary>
            /// Isometric layout for cells in the Grid where any Z cell value set will be added as a Y value.
            /// </summary>
            IsometricZAsY = 3
        }

        /// <summary>
        /// The swizzle order applied to Grid cell positions. Remaps how cell coordinates
        /// are arranged across the X, Y, and Z axes. Values match <see cref="GridLayout.CellSwizzle"/>.
        /// </summary>
        public enum Swizzle
        {
            /// <summary>
            /// Keeps the cell positions at XYZ. This is the default, with no reordering of axes.
            /// </summary>
            XYZ = 0,

            /// <summary>
            /// Swizzles the cell positions from XYZ to XZY. The Y and Z axes are swapped.
            /// </summary>
            XZY = 1,

            /// <summary>
            /// Swizzles the cell positions from XYZ to YXZ. The X and Y axes are swapped.
            /// </summary>
            YXZ = 2,

            /// <summary>
            /// Swizzles the cell positions from XYZ to YZX.
            /// </summary>
            YZX = 3,

            /// <summary>
            /// Swizzles the cell positions from XYZ to ZXY.
            /// </summary>
            ZXY = 4,

            /// <summary>
            /// Swizzles the cell positions from XYZ to ZYX. The X and Z axes are swapped.
            /// </summary>
            ZYX = 5
        }

        /// <summary>
        /// The dimensions of individual cells in the Grid.
        /// </summary>
        public float3 cellSize;

        /// <summary>
        /// The spacing between adjacent cells in the Grid.
        /// </summary>
        public float3 cellGap;

        /// <summary>
        /// Specifies how cells are arranged in the Grid.
        /// </summary>
        public Layout cellLayout;

        /// <summary>
        /// Determines the swizzle order applied to Grid cells.
        /// </summary>
        public Swizzle cellSwizzle;

        /// <summary>
        /// Initializes a new GridData with the given layout and dimensions.
        /// </summary>
        /// <param name="cellLayout">Specifies how cells are arranged in the Grid.</param>
        /// <param name="cellSwizzle">Determines the swizzle order applied to Grid cells.</param>
        /// <param name="cellSize">The dimensions of individual cells in the Grid.</param>
        /// <param name="cellGap">The spacing between adjacent cells in the Grid.</param>
        public GridData(Layout cellLayout, Swizzle cellSwizzle, in float3 cellSize, in float3 cellGap)
        {
            this.cellLayout = cellLayout;
            this.cellSwizzle = cellSwizzle;
            this.cellSize = cellSize;
            this.cellGap = cellGap;
        }

        /// <summary>
        /// Initializes a new GridData from an existing <see cref="GridLayout"/> component,
        /// copying its layout, swizzle, cell size, and cell gap so the data can be used
        /// from Burst-compiled Jobs. Must be called on the main thread.
        /// </summary>
        /// <param name="grid">The GridLayout component to read data from.</param>
        public GridData(GridLayout grid)
        {
            cellSize = grid.cellSize;
            cellGap = grid.cellGap;
            cellLayout = (Layout)(int)grid.cellLayout;
            cellSwizzle = (Swizzle)(int)grid.cellSwizzle;
        }
    }
}
