using System;

using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.U2D.Physics
{
    /// <summary>
    /// Creates physics shapes from the tiles of the Tilemap on this GameObject.
    /// </summary>
    [AddComponentMenu("Physics 2D (Core)/Physics Area Tilemap")]
    [Icon("Packages/com.unity.2d.tilemap.extras/Editor/Physics/Icons/PhysicsAreaTilemap.asset")]
    [RequireComponent(typeof(Tilemap))]
    [PhysicsCustomProperties]
    public sealed class PhysicsAreaTilemap : PhysicsArea
    {
        /// <summary>
        /// The kind of shapes to create.
        /// </summary>
        public enum OutputType
        {
            /// <summary>
            /// A solid shape for each tile.
            /// </summary>
            Polygons,

            /// <summary>
            /// Hollow edges around the outside of the painted tiles only, with no edges between neighbouring tiles, so objects
            /// don't catch on the seams.
            /// </summary>
            Segments
        }

        // The settings that decide how the shapes are built, kept together so the Inspector draws them as one group that shows only the settings in use.
        // Only this area reads and writes them, through its public properties.
        [Serializable]
        internal struct BuildSettings
        {
            // The settings a new or reset area starts with: segments trimmed to their cells, with Z layers joined and the default tolerance.
            internal static BuildSettings defaultSettings => new()
            {
                m_OutputType = OutputType.Segments,
                m_CellClipping = true,
                m_SplitZLayers = false,
                m_VertexTolerance = 0f
            };

            // A hash of every setting here, so an edit can tell whether any of them changed.
            internal Hash128 hash
            {
                get
                {
                    var hash = new Hash128();
                    hash.Append((int)m_OutputType);
                    hash.Append(m_VertexTolerance);
                    hash.Append(m_CellClipping ? 1 : 0);
                    hash.Append(m_SplitZLayers ? 1 : 0);

                    return hash;
                }
            }

            [Tooltip("The kind of shapes to create.")]
            [SerializeField]
            internal OutputType m_OutputType;

            [Tooltip("Trim each tile's shapes to its own cell. Turn it off for tiles meant to overhang their cells.")]
            [SerializeField]
            internal bool m_CellClipping;

            [Tooltip("Whether tiles on different Z layers are kept apart, so where they touch, each keeps its own edges. When off, tiles are joined as if all were on one layer, matching Tilemap Collider 2D.")]
            [SerializeField]
            internal bool m_SplitZLayers;

            [Tooltip("How close vertices must be to count as touching. Tile edges closer than this join, and while cells are clipped, outlines this close to a cell edge snap onto it. 0 uses the default.")]
            [SerializeField]
            [Min(0f)]
            internal float m_VertexTolerance;
        }

        /// <summary>
        /// The kind of shapes to create.
        /// </summary>
        public OutputType outputType
        {
            get => m_BuildSettings.m_OutputType;
            set
            {
                if (m_BuildSettings.m_OutputType == value)
                    return;

                m_BuildSettings.m_OutputType = value;
                ApplyGeometry();
            }
        }

        /// <summary>
        /// The distance within which two vertices count as touching, in the Tilemap's local units.
        /// </summary>
        /// <remarks>
        /// <see cref="OutputType.Segments"/> uses it to join the edges of neighboring tiles. While <see cref="cellClipping"/> is in use, both outputs also use it to pull a tile's outline onto the edge of its cell when the outline stops within this distance of it, and to drop clipped pieces too small to matter.
        /// When the value is 0, the area uses a tolerance that follows the project's scale. A negative value is clamped to 0, and a value below <see cref="PhysicsWorld.linearSlop"/> is raised to it when the shapes are built. See <see cref="activeVertexTolerance"/>.
        /// Raise it if neighboring tiles leave small gaps or bumps along their seams. Too high a value joins edges that should stay apart.
        /// Changing the value rebuilds the shapes.
        /// </remarks>
        public float vertexTolerance
        {
            get => m_BuildSettings.m_VertexTolerance;
            set
            {
                var clamped = Mathf.Max(value, 0f);
                if (Mathf.Approximately(m_BuildSettings.m_VertexTolerance, clamped))
                    return;

                m_BuildSettings.m_VertexTolerance = clamped;
                ApplyGeometry();
            }
        }

        /// <summary>
        /// The vertex tolerance the area builds its shapes with, in the Tilemap's local units.
        /// </summary>
        /// <remarks>
        /// When <see cref="vertexTolerance"/> is above 0, this is that value, raised to at least <see cref="PhysicsWorld.linearSlop"/>.
        /// When <see cref="vertexTolerance"/> is 0, this is twice <see cref="PhysicsWorld.linearSlop"/>, so it follows the project's scale.
        /// </remarks>
        public float activeVertexTolerance => m_BuildSettings.m_VertexTolerance > 0f
            ? Mathf.Max(m_BuildSettings.m_VertexTolerance, PhysicsWorld.linearSlop)
            : defaultVertexTolerance;

        // The tolerance used while vertexTolerance is 0: twice the project's linear slop, so it follows the project's scale.
        internal static float defaultVertexTolerance => PhysicsWorld.linearSlop * 2f;

        /// <summary>
        /// Whether each tile's shapes are trimmed to the tile's own cell, so no tile's shapes reach into a neighboring cell.
        /// </summary>
        /// <remarks>
        /// Tiles are nearly always meant to fit their cells, and a sprite outline that strays past the cell edge, even by a fraction, leaves a sliver or a bump where it meets the next tile.
        /// Clipping removes those.
        /// Turn it off when tiles are meant to overhang their cells, such as tall props that reach into the row above.
        /// It only takes effect when the Grid allows it: see <see cref="isCellClippingActive"/>. Changing it rebuilds the shapes.
        /// </remarks>
        public bool cellClipping
        {
            get => m_BuildSettings.m_CellClipping;
            set
            {
                if (m_BuildSettings.m_CellClipping == value)
                    return;

                m_BuildSettings.m_CellClipping = value;
                ApplyGeometry();
            }
        }

        /// <summary>
        /// Whether the shapes are currently being trimmed to their cells.
        /// </summary>
        /// <remarks>
        /// True when <see cref="cellClipping"/> is on and the Grid allows it. The Grid does not allow it when it has no usable cell shape, or when a negative Cell Gap makes neighboring cells overlap, and the tiles' outlines are then used as they are.
        /// </remarks>
        public bool isCellClippingActive => m_BuildSettings.m_CellClipping && TilemapCellTransforms.CanClipToCells(tilemap, out _);

        /// <summary>
        /// Whether tiles on different Z layers are kept apart, so where they touch, each keeps its own edges.
        /// </summary>
        /// <remarks>
        /// When the value is false, tiles are joined as if they were all on one layer, which matches Tilemap Collider 2D. Tiles stacked at the same cell position on different layers then count as one tile.
        /// When the value is true, each Z layer is built as if it were a Tilemap of its own.
        /// This property only affects the <see cref="OutputType.Segments"/> output type and has no effect on polygons, which are always one shape for each tile.
        /// While <see cref="cellClipping"/> is in use, tiles are only joined across layers when a cell's Z layer does not move it within the Tilemap's plane, as it does on an Isometric Z as Y Grid.
        /// Changing the value rebuilds the shapes.
        /// </remarks>
        public bool splitZLayers
        {
            get => m_BuildSettings.m_SplitZLayers;
            set
            {
                if (m_BuildSettings.m_SplitZLayers == value)
                    return;

                m_BuildSettings.m_SplitZLayers = value;
                ApplyGeometry();
            }
        }

        // Why Cell Clipping is on but not in use, worded for the Inspector, or null when it is in use or turned off.
        internal string cellClippingInactiveReason => m_BuildSettings.m_CellClipping && !TilemapCellTransforms.CanClipToCells(tilemap, out var reason) ? reason : null;

        /// <summary>
        /// Gets the cell a shape of this area was built from.
        /// </summary>
        /// <remarks>
        /// Use this in a contact or trigger callback to find which tile was touched. The area's callbacks report one begin and
        /// one end per pair of areas, so to hear about every cell a body touches, set the callback source to
        /// <see cref="PhysicsArea.CallbackSourceType.Shapes"/>.
        /// </remarks>
        /// <param name="shape">A shape from a contact or trigger event.</param>
        /// <param name="cell">The cell, when the shape is one of this area's.</param>
        /// <returns>False when the shape is not one of this area's or has been destroyed.</returns>
        public bool TryGetCell(PhysicsShape shape, out Vector3Int cell)
        {
            // The owner is stamped as the shapes are created, so this is constant time. A destroyed shape answers false, since
            // an event can outlive the shape it names.
            if (!shape.isValid || !ReferenceEquals(shape.owner, this))
            {
                cell = default;
                return false;
            }

            cell = shape.ownerUserData.vector3IntValue;
            return true;
        }

        /// <summary>
        /// Whether this area always produces exactly one shape.
        /// </summary>
        /// <remarks>
        /// Returns false, because the Tilemap produces a shape for each tile or many edges between them.
        /// The inspector reports the shapes as a set instead of showing a single shape.
        /// </remarks>
        protected override bool producesSingleShape => false;

        /// <summary>
        /// Whether this area's geometry has a radius that the Transform scale can affect.
        /// </summary>
        /// <remarks>
        /// Returns false, because neither the polygons for the tiles nor the chain segments around them have a radius.
        /// The inspector does not offer the Scale Radius option.
        /// </remarks>
        protected override bool usesScaleRadius => false;

        /// <summary>
        /// Whether this area can be used as a Pose layer of a composite.
        /// </summary>
        /// <remarks>
        /// Returns false for both output types, so a composite cannot use this area as a layer.
        /// See <see cref="PhysicsAreaComposite"/>.
        /// </remarks>
        protected override bool canContributeToComposer => false;

        /// <summary>
        /// Releases everything the area prepared for building shapes from the Tilemap when the component is disabled.
        /// </summary>
        protected override void OnProviderDisable()
        {
            base.OnProviderDisable();

            ReleaseUnclippedSegmentBuilder();
            ReleaseClippedSegmentBuilder();
            ReleasePolygonBuilder();

            m_TileCache?.Dispose();
            m_TileCache = null;
            m_CellClippingInUse = false;
        }

        /// <summary>
        /// Starts following tile edits, Tilemap changes and changes to the physics outline of a sprite.
        /// </summary>
        /// <remarks>
        /// Paired with <see cref="OnAreaDisable"/>.
        /// </remarks>
        protected override void OnAreaEnable()
        {
            Tilemap.tilemapTilesChanged += m_OnTilemapTilesChangedHandler ??= OnTilemapTilesChanged;
            Tilemap.tilemapChanged += m_OnTilemapChangedHandler ??= OnTilemapChanged;
            PhysicsSpriteExtensions.physicsGeometryChanged += m_OnSpriteGeometryChangedHandler ??= OnSpriteGeometryChanged;
        }

        /// <summary>
        /// Stops following tile edits, Tilemap changes and changes to the physics outline of a sprite.
        /// </summary>
        /// <remarks>
        /// Paired with <see cref="OnAreaEnable"/>.
        /// </remarks>
        protected override void OnAreaDisable()
        {
            Tilemap.tilemapTilesChanged -= m_OnTilemapTilesChangedHandler;
            Tilemap.tilemapChanged -= m_OnTilemapChangedHandler;
            PhysicsSpriteExtensions.physicsGeometryChanged -= m_OnSpriteGeometryChangedHandler;
        }

        /// <summary>
        /// Builds this area's shapes from the tiles of the Tilemap.
        /// </summary>
        /// <remarks>
        /// The Polygons output creates a solid shape for each tile, and the Segments output creates edges around the outside of the painted tiles.
        /// Builds nothing when the GameObject has no Tilemap.
        /// See <see cref="outputType"/> and <see cref="cellClipping"/>.
        /// </remarks>
        /// <param name="shapeContext">The body, placement and definition to build with, and the calls that create the shapes.</param>
        protected override void GenerateShapes(in PhysicsAreaShapeContext shapeContext)
        {
            var map = tilemap;
            if (map == null)
                return;

            var isOwnBuild = ReferenceEquals(shapeContext.owner, this);

            using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ShapesGenerate))
            {
                // An own build re-reads the Grid's layout; a draw-only build reuses the cache, which updates still read.
                var tileCache = PrepareTileCache(map, isOwnBuild);

                // Only the builder in use is kept, since each holds a table for the whole map.
                var context = new TilemapBuildContext(this, map, shapeContext, tileCache, m_CellClippingInUse, m_BuildSettings.m_SplitZLayers);

                if (m_BuildSettings.m_OutputType == OutputType.Segments && m_CellClippingInUse)
                {
                    ReleasePolygonBuilder();
                    ReleaseUnclippedSegmentBuilder();
                    m_ClippedSegmentBuilder ??= new TilemapClippedSegmentBuilder();
                    m_ClippedSegmentBuilder.Build(context);
                }
                else if (m_BuildSettings.m_OutputType == OutputType.Segments)
                {
                    ReleasePolygonBuilder();
                    ReleaseClippedSegmentBuilder();
                    m_UnclippedSegmentBuilder ??= new TilemapUnclippedSegmentBuilder();
                    m_UnclippedSegmentBuilder.Build(context);
                }
                else
                {
                    ReleaseClippedSegmentBuilder();
                    ReleaseUnclippedSegmentBuilder();
                    m_PolygonBuilder ??= new TilemapPolygonBuilder();
                    m_PolygonBuilder.Build(context);
                }
            }

            FlushBuildTimings("build");
        }

        private void OnTilemapTilesChanged(Tilemap changed, NativeArray<Tilemap.SyncTile> entries, TileChangeReason reason)
        {
            if ((reason & TileChangeReason.Physics) == 0 || changed != tilemap)
                return;

            // Edits update only the shapes they disturb, unless the batch is big enough that rebuilding is cheaper.
            // The entries are only valid during this call, so the builder copies them now.
            if (m_BuildSettings.m_OutputType == OutputType.Segments && m_ClippedSegmentBuilder != null && m_ClippedSegmentBuilder.isTracking)
            {
                m_ClippedSegmentBuilder.ApplyTileChanges(entries);
                RequestUpdateOrRebuild(m_ClippedSegmentBuilder.isRebuildCheaper);
                return;
            }

            if (m_BuildSettings.m_OutputType == OutputType.Segments && m_UnclippedSegmentBuilder != null && m_UnclippedSegmentBuilder.isTracking)
            {
                m_UnclippedSegmentBuilder.ApplyTileChanges(entries);
                RequestUpdateOrRebuild(m_UnclippedSegmentBuilder.isRebuildCheaper);
                return;
            }

            if (m_BuildSettings.m_OutputType == OutputType.Polygons && m_PolygonBuilder != null && m_PolygonBuilder.isTracking)
            {
                m_PolygonBuilder.ApplyTileChanges(entries);
                RequestUpdate();
                return;
            }

            RequestRebuild();
        }

        private void RequestUpdateOrRebuild(bool rebuild)
        {
            if (rebuild)
                RequestRebuild();
            else
                RequestUpdate();
        }

        /// <summary>
        /// Brings the shapes in step with tile edits by changing only the shapes of the cells that were edited, and with edits to the area by keeping the shapes wherever it can.
        /// </summary>
        /// <remarks>
        /// Every shape is rebuilt instead when the area has no Tilemap, or when a tile edit is large enough that a rebuild is quicker.
        /// An edit to the area's definition, user data or callbacks is written to the existing shapes.
        /// An edit to the output type, vertex tolerance, cell clipping, Z layer splitting or transformation rebuilds every shape.
        /// See <see cref="GenerateShapes(in PhysicsAreaShapeContext)"/>.
        /// </remarks>
        /// <param name="shapeContext">The body, placement and definition the area's shapes were built with, the calls that create and destroy them, and why they are being changed.</param>
        protected override void UpdateShapes(in PhysicsAreaShapeContext shapeContext)
        {
            var reasons = shapeContext.reasons;

            if ((reasons & UpdateReasons.Requested) == 0)
            {
                // An edit to the area: a new placement or a change to how the tiles become shapes needs every shape built again.
                // Anything else this area's shapes all share, so it is written to the shapes where they are.
                if ((reasons & (UpdateReasons.Transformation | UpdateReasons.CustomSettings)) != 0)
                {
                    Rebuild();
                    return;
                }

                ApplyDefinitionChanges(reasons);
                return;
            }

            var map = tilemap;
            if (map == null)
            {
                Rebuild();
                return;
            }

            using (TilemapBuildTimings.Measure(TilemapBuildTimings.Stage.ShapesUpdate))
            {
                // Updates reuse the last build's tile cache: layout changes rebuild instead.
                var context = new TilemapBuildContext(this, map, shapeContext, m_TileCache, m_CellClippingInUse, m_BuildSettings.m_SplitZLayers);

                if (m_BuildSettings.m_OutputType == OutputType.Segments && m_CellClippingInUse)
                    m_ClippedSegmentBuilder?.UpdateShapes(context);
                else if (m_BuildSettings.m_OutputType == OutputType.Segments)
                    m_UnclippedSegmentBuilder?.UpdateShapes(context);
                else
                    m_PolygonBuilder?.UpdateShapes(context);
            }

            FlushBuildTimings("update");
        }

        /// <summary>
        /// The options describing how this area uses its shapes, read each time its shapes are built.
        /// </summary>
        /// <remarks>
        /// Adds fast shape searches to the options of the base class, so an edit that destroys a few shapes out of a very large number stays cheap.
        /// See <see cref="AreaOptions"/>.
        /// </remarks>
        protected override AreaOptions areaOptions => base.areaOptions | AreaOptions.FastShapeSearches;

        /// <summary>
        /// A hash of the settings that decide how the tiles become shapes: the output type, vertex tolerance, cell clipping and Z layer splitting.
        /// </summary>
        /// <remarks>
        /// An edit that changes any of these together with a shared setting rebuilds every shape, rather than writing only the shared setting to them.
        /// A tile edit updates the shapes on its own and is not part of the hash.
        /// See <see cref="PhysicsArea.customSettingsHash"/>.
        /// </remarks>
        protected override Hash128 customSettingsHash => m_BuildSettings.hash;

        // Log the diagnostic stage timings and the builder's memory for the build or update that just finished, when they are turned on.
        private void FlushBuildTimings(string phase)
        {
            if (!TilemapBuildTimings.logStageTimings)
                return;

            var builderReport = m_BuildSettings.m_OutputType == OutputType.Polygons ? m_PolygonBuilder?.memoryReport
                : m_CellClippingInUse ? m_ClippedSegmentBuilder?.memoryReport
                : m_UnclippedSegmentBuilder?.memoryReport;

            var memoryReport = new System.Text.StringBuilder(builderReport);
            m_TileCache?.AppendMemoryReport(memoryReport);

            TilemapBuildTimings.Flush($"[{m_BuildSettings.m_OutputType}{(m_CellClippingInUse ? ", clipped" : "")}] {phase}", memoryReport.ToString());
        }

        // Ready the tile cache for a build, and note whether the cells are clipped. Both outputs read every tile's geometry from it.
        private TilemapTileCache PrepareTileCache(Tilemap map, bool isOwnBuild)
        {
            // A draw-only build follows the last own build, and only creates a cache when there is none.
            if (!isOwnBuild)
            {
                if (m_TileCache == null)
                {
                    m_TileCache = new TilemapTileCache();
                    m_TileCache.Reset(map, activeVertexTolerance);
                }

                return m_TileCache;
            }

            m_CellClippingInUse = m_BuildSettings.m_CellClipping && TilemapCellTransforms.CanClipToCells(map, out _);

            m_TileCache ??= new TilemapTileCache();
            m_TileCache.Reset(map, activeVertexTolerance);

            return m_TileCache;
        }

        private void OnTilemapChanged(Tilemap changed, TilemapChangeReason reason)
        {
            const TilemapChangeReason movesCells = TilemapChangeReason.Property | TilemapChangeReason.Geometry;
            if ((reason & movesCells) == 0 || (reason & TilemapChangeReason.EditorPreview) != 0 || changed != tilemap)
                return;

            RequestRebuild();
        }

        // A reimported sprite makes every cell built from it stale, so the shapes are built again when the map uses it.
        private void OnSpriteGeometryChanged(Sprite sprite)
        {
            if (!UsesSprite(tilemap, sprite))
                return;

            RequestRebuild();
        }

        // Whether any cell of the map draws its geometry from the given sprite.
        // Checks the map's distinct sprites, not every cell.
        private static bool UsesSprite(Tilemap map, Sprite sprite)
        {
            if (map == null)
                return false;

            using var usedSprites = map.GetUsedSprites();
            foreach (var used in usedSprites)
            {
                if (used == sprite)
                    return true;
            }

            return false;
        }

        // The Tilemap on this GameObject, looked up once and again only after it has been destroyed, since every tile edit in the scene reads it.
        Tilemap tilemap
        {
            get
            {
                if (m_Tilemap == null)
                    TryGetComponent(out m_Tilemap);

                return m_Tilemap;
            }
        }

        // Whether the polygon builder, or either segment builder, is currently allocated, so tests can check a builder is released when it is no longer used.
        internal bool hasPolygonBuilder => m_PolygonBuilder != null;
        internal bool hasSegmentBuilder => m_ClippedSegmentBuilder != null || m_UnclippedSegmentBuilder != null;

        void ReleaseUnclippedSegmentBuilder()
        {
            m_UnclippedSegmentBuilder?.Dispose();
            m_UnclippedSegmentBuilder = null;
        }

        void ReleaseClippedSegmentBuilder()
        {
            m_ClippedSegmentBuilder?.Dispose();
            m_ClippedSegmentBuilder = null;
        }

        void ReleasePolygonBuilder()
        {
            m_PolygonBuilder?.Dispose();
            m_PolygonBuilder = null;
        }

        Tilemap m_Tilemap;
        TilemapPolygonBuilder m_PolygonBuilder;
        TilemapUnclippedSegmentBuilder m_UnclippedSegmentBuilder;
        TilemapClippedSegmentBuilder m_ClippedSegmentBuilder;

        // Every tile's geometry shaped once per tile, and whether the last build clipped the cells, which decides whether the updates after it do too.
        TilemapTileCache m_TileCache;
        bool m_CellClippingInUse;

        System.Action<Tilemap, NativeArray<Tilemap.SyncTile>, TileChangeReason> m_OnTilemapTilesChangedHandler;
        System.Action<Tilemap, TilemapChangeReason> m_OnTilemapChangedHandler;
        System.Action<Sprite> m_OnSpriteGeometryChangedHandler;

        // The build settings, drawn in the Inspector by their own drawer.
        [SerializeField]
        BuildSettings m_BuildSettings = BuildSettings.defaultSettings;
    }
}
