using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.U2D.Physics
{
    /// <summary>
    /// Everything a builder needs from the component that owns it.
    /// </summary>
    internal readonly struct TilemapBuildContext
    {
        public TilemapBuildContext(PhysicsAreaTilemap owner, Tilemap tilemap, in PhysicsAreaShapeContext shapeContext,
            TilemapTileCache tileCache, bool cellClipping, bool splitZLayers)
        {
            this.owner = owner;
            this.tilemap = tilemap;
            this.shapeContext = shapeContext;
            definition = shapeContext.definition;
            relative = shapeContext.relative;
            this.tileCache = tileCache;
            this.cellClipping = cellClipping;
            this.splitZLayers = splitZLayers;
        }

        /// <summary>The component the shapes are built for.</summary>
        public readonly PhysicsAreaTilemap owner;

        /// <summary>The Tilemap whose occupied cells become the shapes.</summary>
        public readonly Tilemap tilemap;

        /// <summary>Creates and destroys the shapes, already owned by the area that holds them.</summary>
        public readonly PhysicsAreaShapeContext shapeContext;

        /// <summary>The definition every shape is created with, already stripped of its per-shape mass update.</summary>
        public readonly PhysicsShapeDefinition definition;

        /// <summary>Maps geometry in the component's local space into the owning body's space.</summary>
        public readonly Matrix4x4 relative;

        /// <summary>
        /// Whether the shapes being built are the component's own live shapes.
        /// </summary>
        /// <remarks>
        /// False when the Editor builds shapes only to draw them. A builder only records which shape stands for which cell when
        /// this is true, since those are the shapes a later tile edit updates.
        /// </remarks>
        public bool isOwnBuild => shapeContext.owner == owner;

        // Every tile's geometry shaped once per tile, which both outputs read, clipped or not.
        public readonly TilemapTileCache tileCache;

        // Whether each tile is cut to its own cell.
        public readonly bool cellClipping;

        // Whether the Segments output keeps tiles on different Z layers apart rather than joining them.
        public readonly bool splitZLayers;

        // Room for a quarter more than the build needs, so the first edits after it don't rehash a large table.
        public static int CapacityWithHeadroom(int count) => count + count / 4;
    }
}
