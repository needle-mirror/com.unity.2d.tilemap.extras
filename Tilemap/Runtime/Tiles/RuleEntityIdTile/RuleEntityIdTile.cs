using System;
using System.Collections.Generic;
using AOT;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.Tilemaps.Experimental
{
    /// <summary>
    ///     Generic visual tile for creating different tilesets like terrain, pipeline, random or animated tiles.
    /// </summary>
    [BurstCompile]
    [Serializable]
    [HelpURL(
        "https://docs.unity3d.com/Packages/com.unity.2d.tilemap.extras@latest/index.html?subfolder=/manual/RuleEntityIdTile.html")]
    public class RuleEntityIdTile : EntityIdTileBase
    {
        private static Dictionary<Tilemap, KeyValuePair<HashSet<TileBase>, HashSet<Vector3Int>>>
            m_CacheTilemapsNeighborPositions = new();

        private static TileBase[] m_AllocatedUsedTileArr = Array.Empty<TileBase>();

        [SerializeField]
        private Sprite m_DefaultSprite;
        [SerializeField]
        private GameObject m_DefaultGameObject;
        [SerializeField]
        private Tile.ColliderType m_DefaultColliderType = Tile.ColliderType.Sprite;

        private RuleEntityIdTileDataStruct m_Data;

        /// <summary>
        ///     The Default Sprite set when creating a new Rule.
        /// </summary>
        public Sprite defaultSprite
        {
            get => m_DefaultSprite;
            set
            {
                m_DefaultSprite = value;
                m_Data.defaultTileData.spriteEntityId = m_DefaultSprite != null ? m_DefaultSprite.GetEntityId() : EntityId.None;
            }
        }


        /// <summary>
        ///     The Default GameObject set when creating a new Rule.
        /// </summary>
        public GameObject defaultGameObject
        {
            get => m_DefaultGameObject;
            set
            {
                m_DefaultGameObject = value;
                m_Data.defaultTileData.gameObjectEntityId = m_DefaultGameObject != null ? m_DefaultGameObject.GetEntityId() : EntityId.None;
            }
        }

        /// <summary>
        ///     The Default Collider Type set when creating a new Rule.
        /// </summary>
        public Tile.ColliderType defaultColliderType
        {
            get => m_DefaultColliderType;
            set
            {
                m_DefaultColliderType = value;
                m_Data.defaultTileData.colliderType = value;
            }
        }

        /// <summary>
        ///     A list of Tiling Rules for the Rule Tile.
        /// </summary>
        [HideInInspector] public List<TilingRule> m_TilingRules = new();

        private HashSet<Vector3Int> m_NeighborPositions = new();

        /// <summary>
        ///     Returns the default Neighbor Rule Class type.
        /// </summary>
        public virtual Type m_NeighborType => typeof(TilingRuleOutput.Neighbor);

        /// <summary>
        ///     Angle in which the RuleEntityIdTile is rotated by for matching in Degrees.
        /// </summary>
        public virtual int m_RotationAngle => 90;

        /// <summary>
        ///     Number of rotations the RuleEntityIdTile can be rotated by for matching.
        /// </summary>
        public int m_RotationCount => 360 / m_RotationAngle;

        /// <summary>
        ///     Returns a set of neighboring positions for this RuleEntityIdTile
        /// </summary>
        public HashSet<Vector3Int> neighborPositions
        {
            get
            {
                if (m_NeighborPositions.Count == 0)
                {
                    UpdateNeighborPositions(ref m_Data.neighborPositions, ref m_Data.bounds);
                    foreach (var neighborPosition in m_Data.neighborPositions)
                        m_NeighborPositions.Add(neighborPosition.ToVector3Int());
                }
                return m_NeighborPositions;
            }
        }

        /// <summary>
        ///     Updates the neighboring positions of this RuleEntityIdTile
        /// </summary>
        /// <param name="refNeighborPositions">Neighbor positions</param>
        /// <param name="refBounds">Bounds of the neighboring positions</param>
        protected void UpdateNeighborPositions(ref NativeHashSet<int3> refNeighborPositions, ref BoundsInt refBounds)
        {
            m_CacheTilemapsNeighborPositions.Clear();

            var positions = refNeighborPositions;
            if (positions.IsCreated)
                positions.Dispose();
            positions = new NativeHashSet<int3>(m_TilingRules.Count, Allocator.Persistent);

            foreach (var rule in m_TilingRules)
            {
                foreach (var neighbor in rule.GetNeighbors())
                {
                    var position = neighbor.Key.ToInt3();
                    positions.Add(position);

                    // Check rule against rotations
                    if (rule.m_RuleTransform == RuleMatchSerializable.TransformMatch.Rotated)
                    {
                        for (var angle = m_RotationAngle; angle < 360; angle += m_RotationAngle)
                        {
                            positions.Add(GetRotatedPosition(position, angle));
                        }
                    }
                    // Check rule against x-axis, y-axis mirror
                    else if (rule.m_RuleTransform == RuleMatchSerializable.TransformMatch.MirrorXY)
                    {
                        positions.Add(GetMirroredPosition(position, true, true));
                        positions.Add(GetMirroredPosition(position, true, false));
                        positions.Add(GetMirroredPosition(position, false, true));
                    }
                    // Check rule against x-axis mirror
                    else if (rule.m_RuleTransform == RuleMatchSerializable.TransformMatch.MirrorX)
                    {
                        positions.Add(GetMirroredPosition(position, true, false));
                    }
                    // Check rule against y-axis mirror
                    else if (rule.m_RuleTransform == RuleMatchSerializable.TransformMatch.MirrorY)
                    {
                        positions.Add(GetMirroredPosition(position, false, true));
                    }
                    else if (rule.m_RuleTransform == RuleMatchSerializable.TransformMatch.RotatedMirror)
                    {
                        var mirPos = GetMirroredPosition(position, true, false);
                        for (var angle = m_RotationAngle; angle < 360; angle += m_RotationAngle)
                        {
                            positions.Add(GetRotatedPosition(position, angle));
                            positions.Add(GetRotatedPosition(mirPos, angle));
                        }
                    }
                }
            }

            refBounds = new BoundsInt();
            foreach (var position in positions)
            {
                if (position.x < refBounds.xMin)
                    refBounds.xMin = position.x;
                if (position.x > refBounds.xMax)
                    refBounds.xMax = position.x;
                if (position.y < refBounds.yMin)
                    refBounds.yMin = position.y;
                if (position.y > refBounds.yMax)
                    refBounds.yMax = position.y;
            }
            refNeighborPositions = positions;
        }

        /// <summary>
        ///     Gets a rotated position given its original position and the rotation in degrees.
        /// </summary>
        /// <param name="position">Original position of Tile.</param>
        /// <param name="rotation">Rotation in degrees.</param>
        /// <returns>Rotated position of Tile.</returns>
        public virtual int3 GetRotatedPosition(int3 position, int rotation)
        {
            var curPos = position;
            GetRotatedPosition(ref curPos, rotation);
            return curPos;
        }

        /// <summary>
        ///     Gets a mirrored position given its original position and the mirroring axii.
        /// </summary>
        /// <param name="position">Original position of Tile.</param>
        /// <param name="mirrorX">Mirror in the X Axis.</param>
        /// <param name="mirrorY">Mirror in the Y Axis.</param>
        /// <returns>Mirrored position of Tile.</returns>
        public virtual int3 GetMirroredPosition(int3 position, bool mirrorX, bool mirrorY)
        {
            var mirPos = position;
            GetMirroredPosition(ref mirPos, mirrorX, mirrorY);
            return mirPos;
        }

        /// <summary>
        ///     Get the offset for the given position with the given offset.
        /// </summary>
        /// <param name="position">Position to offset.</param>
        /// <param name="offset">Offset for the position.</param>
        /// <returns>The offset position.</returns>
        public virtual int3 GetOffsetPosition(int3 position, int3 offset)
        {
            var curPos = position;
            GetOffsetPosition(ref curPos, ref offset);
            return curPos;
        }

        /// <summary>
        ///     Get the reversed offset for the given position with the given offset.
        /// </summary>
        /// <param name="position">Position to offset.</param>
        /// <param name="offset">Offset for the position.</param>
        /// <returns>The reversed offset position.</returns>
        public virtual int3 GetOffsetPositionReverse(int3 position, int3 offset)
        {
            var curPos = position;
            GetOffsetPositionReverse(ref curPos, ref offset);
            return curPos;
        }

        /// <summary>
        /// Data struct for RuleEntityIdTile to be used in Unity Jobs
        /// </summary>
        public struct RuleEntityIdTileDataStruct : IDisposable
        {
            /// <summary>
            /// RuleEntityIdTile's EntityId
            /// </summary>
            public EntityId tileId;

            /// <summary>
            /// Set of neighbor positions
            /// </summary>
            public NativeHashSet<int3> neighborPositions;

            /// <summary>
            /// Bounds of neighbor positions
            /// </summary>
            public BoundsInt bounds;

            /// <summary>
            /// Rotation Angle for RuleEntityIdTile
            /// </summary>
            public int rotationAngle;

            /// <summary>
            /// Randomization struct
            /// </summary>
            public Unity.Mathematics.Random random;

            /// <summary>
            /// Array of RuleMatchInput for each TilingRule
            /// </summary>
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<RuleMatchInput> ruleMatchInputs;

            /// <summary>
            /// Array of RuleMatchOutput for each TilingRule
            /// </summary>
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<RuleMatchOutput> ruleMatchOutputs;

            /// <summary>
            /// Default Tile Data
            /// </summary>
            public TileData defaultTileData;

            /// <summary>
            /// Disposes memory allocations used by RuleEntityIdTileDataStruct
            /// </summary>
            public void Dispose()
            {
                if (neighborPositions.IsCreated)
                    neighborPositions.Dispose();
                if (ruleMatchInputs.IsCreated)
                {
                    foreach (var ruleMatchInput in ruleMatchInputs)
                        ruleMatchInput.Dispose();
                    ruleMatchInputs.Dispose();
                }
                if (ruleMatchOutputs.IsCreated)
                {
                    foreach (var ruleMatchOutput in ruleMatchOutputs)
                        ruleMatchOutput.Dispose();
                    ruleMatchOutputs.Dispose();
                }
            }
        }

        /// <summary>
        /// Returns the type of data struct used for the RuleEntityIdTile,
        /// which is the type of RuleEntityIdTileDataStruct.
        /// </summary>
        public override Type structType => typeof(RuleEntityIdTileDataStruct);

        /// <summary>
        /// Copies the Data struct used for this RuleEntityIdTile to the outPtr buffer
        /// for use in Unity Jobs.
        /// </summary>
        /// <param name="outPtr">Data buffer to copy data struct from RuleEntityIdTile to.</param>
        public override unsafe void CopyDataStruct(void* outPtr)
        {
            UnsafeUtility.CopyStructureToPtr(ref m_Data, outPtr);
        }

        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to refresh RuleEntityIdTiles
        /// </summary>
        protected override unsafe RefreshTileJobDelegate refreshTileJobDelegate => RefreshTileJob;

        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to get Tile Data from RuleEntityIdTiles
        /// </summary>
        protected override unsafe GetTileDataJobDelegate getTileDataJobDelegate => GetTileDataJob;

        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to get Tile Animation Data from RuleEntityIdTiles
        /// </summary>
        protected override unsafe GetTileAnimationDataJobDelegate getTileAnimationDataJobDelegate =>
            GetTileAnimationDataJob;

        /// <summary>
        /// Initializes the RuleEntityIdTile.
        /// </summary>
        public override void OnEnable()
        {
            base.OnEnable();
            OnValidate();
        }

        /// <summary>
        /// Cleans up the RuleEntityIdTile.
        /// </summary>
        public override void OnDisable()
        {
            Dispose();
            base.OnDisable();
        }

        private void Dispose()
        {
            m_Data.Dispose();
        }

        private void OnValidate()
        {
            Dispose();

            uint r = 0;
            var c = cachedEntityId;
            if (c == EntityId.None)
                return;

            r = UnsafeUtility.As<EntityId, uint>(ref c);
            m_Data = new RuleEntityIdTileDataStruct()
            {
                tileId = cachedEntityId,
                random = new Unity.Mathematics.Random(r),
                ruleMatchInputs = new NativeArray<RuleMatchInput>(m_TilingRules.Count, Allocator.Persistent),
                ruleMatchOutputs = new NativeArray<RuleMatchOutput>(m_TilingRules.Count, Allocator.Persistent),
                rotationAngle = 90,
                defaultTileData = new TileData()
                {
                    spriteEntityId = defaultSprite != null ? defaultSprite.GetEntityId() : EntityId.None,
                    color = Color.white,
                    transform = Matrix4x4.identity,
                    gameObjectEntityId = defaultGameObject != null ? defaultGameObject.GetEntityId() : EntityId.None,
                    flags = TileFlags.LockAll,
                    colliderType = m_DefaultColliderType
                }
            };

            for (var i = 0; i < m_TilingRules.Count; i++)
            {
                var tilingRule = m_TilingRules[i];
                unsafe
                {
                    tilingRule.ToRuleMatchInput(ref UnsafeUtility.ArrayElementAsRef<RuleMatchInput>(m_Data.ruleMatchInputs.GetUnsafePtr(), i));
                    tilingRule.ToRuleMatchOutput(ref UnsafeUtility.ArrayElementAsRef<RuleMatchOutput>(m_Data.ruleMatchOutputs.GetUnsafePtr(), i));
                }
            }
            UpdateNeighborPositions(ref m_Data.neighborPositions, ref m_Data.bounds);
        }

        /// <summary>
        ///     StartUp is called on the first frame of the running Scene.
        /// </summary>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="tilemap">The Tilemap the tile is present on.</param>
        /// <param name="instantiatedGameObject">The GameObject instantiated for the Tile.</param>
        /// <returns>Whether StartUp was successful</returns>
        public override bool StartUp(Vector3Int position, ITilemap tilemap, GameObject instantiatedGameObject)
        {
            if (instantiatedGameObject != null)
            {
                var tmpMap = tilemap.GetComponent<Tilemap>();
                var orientMatrix = tmpMap.orientationMatrix;
                TilemapDataStruct tilemapData = new TilemapDataStruct(tilemap.GetComponent<Tilemap>());

                var gameObjectTranslation = new Vector3();
                var gameObjectRotation = new Quaternion();
                var gameObjectScale = new Vector3();

                var ruleMatched = false;
                var j = 0;
                for (; j < m_Data.ruleMatchInputs.Length; j++)
                {
                    var ruleMatch = m_Data.ruleMatchInputs[j];
                    var pos = position.ToInt3();
                    var transform = AffineTransform.identity;
                    if (RuleMatches(ref ruleMatch, ref tilemapData, ref m_Data, ref pos, ref transform))
                    {
                        var t = (float4x4) transform;
                        var m4x4transform = UnsafeUtility.As<float4x4, Matrix4x4>(ref t);
                        m4x4transform = orientMatrix * m4x4transform;

                        // Converts the tile's translation, rotation, & scale matrix to values to be used by the instantiated GameObject
                        gameObjectTranslation = new Vector3(m4x4transform.m03, m4x4transform.m13, m4x4transform.m23);
                        gameObjectRotation = Quaternion.LookRotation(
                            new Vector3(m4x4transform.m02, m4x4transform.m12, m4x4transform.m22),
                            new Vector3(m4x4transform.m01, m4x4transform.m11, m4x4transform.m21));
                        gameObjectScale = m4x4transform.lossyScale;
                        ruleMatched = true;
                        break;
                    }
                }

                if (!ruleMatched)
                {
                    // Fallback to just using the orientMatrix for the translation, rotation, & scale values.
                    gameObjectTranslation = new Vector3(orientMatrix.m03, orientMatrix.m13, orientMatrix.m23);
                    gameObjectRotation = Quaternion.LookRotation(
                        new Vector3(orientMatrix.m02, orientMatrix.m12, orientMatrix.m22),
                        new Vector3(orientMatrix.m01, orientMatrix.m11, orientMatrix.m21));
                    gameObjectScale = orientMatrix.lossyScale;
                }

                instantiatedGameObject.transform.localPosition = gameObjectTranslation +
                                                                 tmpMap.CellToLocalInterpolated(position +
                                                                     tmpMap.tileAnchor);
                instantiatedGameObject.transform.localRotation = gameObjectRotation;
                instantiatedGameObject.transform.localScale = gameObjectScale;
            }

            return true;
        }

        /// <summary>
        ///     This method is called when the tile is refreshed.
        /// </summary>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="tilemap">The Tilemap the tile is present on.</param>
        public override void RefreshTile(Vector3Int position, ITilemap tilemap)
        {
            var refreshPositions = new NativeArray<Vector3Int>(m_Data.neighborPositions.Count + 1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var i = 0;
            foreach (var neighborPosition in m_Data.neighborPositions)
            {
                var curpos = position.ToInt3();
                var neighborOffset = neighborPosition;
                GetOffsetPosition(ref curpos, ref neighborOffset);
                refreshPositions[i++] = curpos.ToVector3Int();
            }
            refreshPositions[i] = position;
            tilemap.RefreshTiles(refreshPositions);
            refreshPositions.Dispose();
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(RefreshTileJobDelegate))]
        static unsafe void RefreshTileJob(int count, int3* position, void* data, ref TilemapRefreshStruct tilemapRefreshStruct)
        {
            var dataStruct = UnsafeUtility.AsRef<RuleEntityIdTileDataStruct>(data);
            var refreshPositions = new NativeArray<int3>(dataStruct.neighborPositions.Count + 1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < count; ++i)
            {
                var pos = *(position + i);
                var j = 0;
                foreach (var neighborPosition in dataStruct.neighborPositions)
                {
                    var offsetPosition = pos;
                    var neighborOffset = neighborPosition;
                    GetOffsetPosition(ref offsetPosition, ref neighborOffset);
                    refreshPositions[j++] = offsetPosition;
                }
                refreshPositions[j] = pos;
                tilemapRefreshStruct.RefreshTiles(refreshPositions);
            }
            refreshPositions.Dispose();
        }


        /// <summary>
        ///     Retrieves any tile rendering data from the scripted tile.
        /// </summary>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="tilemap">The Tilemap the tile is present on.</param>
        /// <param name="tileData">Data to render the tile.</param>
        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            var tilemapData = new TilemapDataStruct(tilemap.GetComponent<Tilemap>());
            unsafe
            {
                UnsafeUtility.CopyPtrToStructure(UnsafeUtility.AddressOf(ref m_Data.defaultTileData)
                    , out tileData);

                var pos = position.ToInt3();
                GetTileDataJob(1, &pos
                    , UnsafeUtility.AddressOf(ref m_Data)
                    , ref tilemapData
                    , (TileData*) UnsafeUtility.AddressOf(ref tileData));
            }
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(GetTileDataJobDelegate))]
        static unsafe void GetTileDataJob(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileData* outTileData)
        {
            var dataStruct = UnsafeUtility.AsRef<RuleEntityIdTileDataStruct>(data);
            for (var i = 0; i < count; ++i)
            {
                ref int3 inPos = ref *(position + i);
                ref TileData tileData = ref *(outTileData + i);
                UnsafeUtility.CopyPtrToStructure(UnsafeUtility.AddressOf(ref dataStruct.defaultTileData), out tileData);
                for (var j = 0; j < dataStruct.ruleMatchInputs.Length; j++)
                {
                    var pos = inPos;
                    var transform = AffineTransform.identity;
                    var ruleMatch = dataStruct.ruleMatchInputs[j];
                    if (RuleMatches(ref ruleMatch, ref tilemapDataStruct, ref dataStruct, ref pos, ref transform))
                    {
                        var ruleMatchOutput = dataStruct.ruleMatchOutputs[j];
                        switch (ruleMatchOutput.spriteOutput)
                        {
                            case RuleMatchSerializable.OutputSprite.Single:
                            case RuleMatchSerializable.OutputSprite.Animation:
                                tileData.spriteEntityId = ruleMatchOutput.sprites.Length > 0 ? ruleMatchOutput.sprites[0] : EntityId.None;
                                break;
                            case RuleMatchSerializable.OutputSprite.Random:
                                var l = ruleMatchOutput.sprites.Length;
                                var pv = GetPerlinValue(ref pos, ruleMatchOutput.perlinScale, 100000f) * l;
                                var idx = math.clamp((int) math.floor(pv), 0, l);
                                tileData.spriteEntityId = ruleMatchOutput.sprites[idx];
                                // TODO: ApplyRandomTransform
                                break;
                        }

                        var t = (float4x4)transform;
                        tileData.transform = UnsafeUtility.As<float4x4, Matrix4x4>(ref t);
                        tileData.gameObjectEntityId = ruleMatchOutput.gameObjects.Length > 0 ? ruleMatchOutput.gameObjects[0] : EntityId.None;
                        tileData.colliderType = ruleMatchOutput.colliderType;
                        break;
                    }
                }
            }
        }

        /// <summary>
        ///     Does a Rule Match given a Tiling Rule and neighboring Tiles.
        /// </summary>
        /// <param name="ruleMatch">The Tiling Rule to match with.</param>
        /// <param name="tilemapData">The tilemap to match with.</param>
        /// <param name="tileData">The Tile's data as a struct.</param>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="transform">A transform matrix which will match the Rule.</param>
        /// <returns>True if there is a match, False if not.</returns>
        [BurstCompile]
        public static bool RuleMatches(ref RuleMatchInput ruleMatch
            , ref TilemapDataStruct tilemapData
            , ref RuleEntityIdTileDataStruct tileData
            , ref int3 position
            , ref AffineTransform transform)
        {
            if (RuleMatches(ref ruleMatch, ref tilemapData, ref tileData, ref position, 0))
            {
                transform = math.AffineTransform(int3.zero, quaternion.identity, new float3(1f));
                return true;
            }

            // Check rule against rotations of 0, 90, 180, 270
            if (ruleMatch.transformMatch == RuleMatchSerializable.TransformMatch.Rotated)
            {
                for (var angle = tileData.rotationAngle; angle < 360; angle += tileData.rotationAngle)
                    if (RuleMatches(ref ruleMatch, ref tilemapData, ref tileData, ref position, angle))
                    {
                        transform = math.AffineTransform(int3.zero, quaternion.Euler(0f, 0f, math.radians(-angle)), new float3(1f));
                        return true;
                    }
            }
            // Check rule against x-axis, y-axis mirror
            else if (ruleMatch.transformMatch == RuleMatchSerializable.TransformMatch.MirrorXY)
            {
                if (RuleMatches(ref ruleMatch, ref tilemapData, ref tileData, ref position, true, true))
                {
                    transform = math.AffineTransform(int3.zero, quaternion.identity, new float3(-1f, -1f, 1f));
                    return true;
                }

                if (RuleMatches(ref ruleMatch, ref tilemapData, ref tileData, ref position, true, false))
                {
                    transform = math.AffineTransform(int3.zero, quaternion.identity, new float3(-1f, 1f, 1f));
                    return true;
                }

                if (RuleMatches(ref ruleMatch, ref tilemapData, ref tileData, ref position, false, true))
                {
                    transform = math.AffineTransform(int3.zero, quaternion.identity, new float3(-1f, -1f, 1f));
                    return true;
                }
            }
            // Check rule against x-axis mirror
            else if (ruleMatch.transformMatch == RuleMatchSerializable.TransformMatch.MirrorX)
            {
                if (RuleMatches(ref ruleMatch, ref tilemapData, ref tileData, ref position, true, false))
                {
                    transform = math.AffineTransform(int3.zero, quaternion.identity, new float3(-1f, 1f, 1f));
                    return true;
                }
            }
            // Check rule against y-axis mirror
            else if (ruleMatch.transformMatch == RuleMatchSerializable.TransformMatch.MirrorY)
            {
                if (RuleMatches(ref ruleMatch, ref tilemapData, ref tileData, ref position, false, true))
                {
                    transform = math.AffineTransform(int3.zero, quaternion.identity, new float3(1f, -1f, 1f));
                    return true;
                }
            }
            // Check rule against x-axis mirror with rotations of 0, 90, 180, 270
            else if (ruleMatch.transformMatch == RuleMatchSerializable.TransformMatch.RotatedMirror)
            {
                for (var angle = 0; angle < 360; angle += tileData.rotationAngle)
                {
                    if (angle != 0 && RuleMatches(ref ruleMatch, ref tilemapData, ref tileData, ref position, angle))
                    {
                        transform = math.AffineTransform(int3.zero, quaternion.Euler(0f, 0f, math.radians(-angle)), new float3(1f));
                        return true;
                    }

                    if (RuleMatches(ref ruleMatch, ref tilemapData, ref tileData, ref position, angle, true))
                    {
                        transform = math.AffineTransform(int3.zero, quaternion.Euler(0f, 0f, math.radians(-angle)), new float3(-1f, 1f, 1f));
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        ///     Returns a Perlin Noise value based on the given inputs.
        /// </summary>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="scale">The Perlin Scale factor of the Tile.</param>
        /// <param name="offset">Offset of the Tile on the Tilemap.</param>
        /// <returns>A Perlin Noise value based on the given inputs.</returns>
        [BurstCompile]
        public static float GetPerlinValue(ref int3 position, float scale, float offset)
        {
            var posf = new float2(position.x, position.y);
            return noise.cnoise((posf + offset) * scale);
        }

        private static bool IsTilemapUsedTilesChange(Tilemap tilemap,
            out KeyValuePair<HashSet<TileBase>, HashSet<Vector3Int>> hashSet)
        {
            if (!m_CacheTilemapsNeighborPositions.TryGetValue(tilemap, out hashSet))
                return true;

            var oldUsedTiles = hashSet.Key;
            var newUsedTilesCount = tilemap.GetUsedTilesCount();
            if (newUsedTilesCount != oldUsedTiles.Count)
                return true;

            if (m_AllocatedUsedTileArr.Length < newUsedTilesCount)
                Array.Resize(ref m_AllocatedUsedTileArr, newUsedTilesCount);

            tilemap.GetUsedTilesNonAlloc(m_AllocatedUsedTileArr);
            for (var i = 0; i < newUsedTilesCount; i++)
            {
                var newUsedTile = m_AllocatedUsedTileArr[i];
                if (!oldUsedTiles.Contains(newUsedTile))
                    return true;
            }

            return false;
        }

        private static KeyValuePair<HashSet<TileBase>, HashSet<Vector3Int>> CachingTilemapNeighborPositions(
            Tilemap tilemap)
        {
            var usedTileCount = tilemap.GetUsedTilesCount();
            var usedTiles = new HashSet<TileBase>();
            var cachedNeighborPositions = new HashSet<Vector3Int>();

            if (m_AllocatedUsedTileArr.Length < usedTileCount)
                Array.Resize(ref m_AllocatedUsedTileArr, usedTileCount);

            tilemap.GetUsedTilesNonAlloc(m_AllocatedUsedTileArr);

            for (var i = 0; i < usedTileCount; i++)
            {
                var tile = m_AllocatedUsedTileArr[i];
                usedTiles.Add(tile);
                RuleEntityIdTile ruleEntityIdTile = null;

                if (tile is RuleEntityIdTile rt)
                    ruleEntityIdTile = rt;
                if (ruleEntityIdTile)
                    foreach (Vector3Int neighborPosition in ruleEntityIdTile.neighborPositions)
                        cachedNeighborPositions.Add(neighborPosition);
            }

            var value = new KeyValuePair<HashSet<TileBase>, HashSet<Vector3Int>>(usedTiles, cachedNeighborPositions);
            m_CacheTilemapsNeighborPositions[tilemap] = value;
            return value;
        }

        private static bool NeedRelease()
        {
            foreach (var keypair in m_CacheTilemapsNeighborPositions)
                if (keypair.Key == null)
                    return true;

            return false;
        }

        private static void ReleaseDestroyedTilemapCacheData()
        {
            if (!NeedRelease())
                return;

            var hasCleared = false;
            var keys = new Tilemap[m_CacheTilemapsNeighborPositions.Count];
            var i = 0;
            foreach (var key in m_CacheTilemapsNeighborPositions.Keys)
            {
                keys[i++] = key;
            }
            foreach (var key in keys)
                if (key == null && m_CacheTilemapsNeighborPositions.Remove(key))
                    hasCleared = true;

            if (hasCleared)
                // TrimExcess
                m_CacheTilemapsNeighborPositions =
                    new Dictionary<Tilemap, KeyValuePair<HashSet<TileBase>, HashSet<Vector3Int>>>(
                        m_CacheTilemapsNeighborPositions);
        }

        /// <summary>
        ///     Retrieves any tile animation data from the scripted tile.
        /// </summary>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="tilemap">The Tilemap the tile is present on.</param>
        /// <param name="tileAnimationData">Data to run an animation on the tile.</param>
        /// <returns>Whether the call was successful.</returns>
        public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap,
            ref TileAnimationData tileAnimationData)
        {
            var tilemapData = new TilemapDataStruct(tilemap.GetComponent<Tilemap>());
            for (var j = 0; j < m_Data.ruleMatchOutputs.Length; j++)
            {
                var ruleMatchOutput = m_Data.ruleMatchOutputs[j];
                var transform = AffineTransform.identity;
                if (ruleMatchOutput.spriteOutput == RuleMatchSerializable.OutputSprite.Animation)
                {
                    var ruleMatch = m_Data.ruleMatchInputs[j];
                    var pos = position.ToInt3();
                    if (RuleMatches(ref ruleMatch, ref tilemapData, ref m_Data, ref pos, ref transform))
                    {
                        tileAnimationData.animatedSprites = m_TilingRules[j].m_Sprites;
                        tileAnimationData.animationSpeed =
                            m_Data.random.NextFloat(ruleMatchOutput.minAnimationSpeed, ruleMatchOutput.maxAnimationSpeed);
                        return true;
                    }
                }
            }
            return false;
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(GetTileAnimationDataJobDelegate))]
        private static unsafe void GetTileAnimationDataJob(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileAnimationEntityIdData* outTileAnimationEntityIdData)
        {
            var dataStruct = UnsafeUtility.AsRef<RuleEntityIdTileDataStruct>(data);
            for (var i = 0; i < count; ++i)
            {
                ref int3 inPos = ref *(position + i);
                ref TileAnimationEntityIdData tileAnimationData = ref *(outTileAnimationEntityIdData + i);
                for (var j = 0; j < dataStruct.ruleMatchInputs.Length; j++)
                {
                    var ruleMatchOutput = dataStruct.ruleMatchOutputs[j];
                    if (ruleMatchOutput.spriteOutput == RuleMatchSerializable.OutputSprite.Animation)
                    {
                        var ruleMatch = dataStruct.ruleMatchInputs[j];
                        var pos = inPos;
                        var transform = AffineTransform.identity;
                        if (RuleMatches(ref ruleMatch, ref tilemapDataStruct, ref dataStruct, ref pos, ref transform))
                        {
                            if (ruleMatchOutput.sprites.IsCreated)
                                tileAnimationData.animatedSpritesEntityIds = ruleMatchOutput.sprites;
                            tileAnimationData.animationSpeed =
                                dataStruct.random.NextFloat(ruleMatchOutput.minAnimationSpeed, ruleMatchOutput.maxAnimationSpeed);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Checks if there is a match given the neighbor matching rule and a Tile.
        /// </summary>
        /// <param name="neighborRule">Neighbor matching rule.</param>
        /// <param name="thisOne">Tile to match.</param>
        /// <param name="otherOne">Other Tile to match.</param>
        /// <returns>True if there is a match, False if not.</returns>
        [BurstCompile]
        public static bool RuleMatch(int neighborRule, EntityId thisOne, EntityId otherOne)
        {
            switch (neighborRule)
            {
                case TilingRuleOutput.Neighbor.This: return thisOne == otherOne;
                case TilingRuleOutput.Neighbor.NotThis: return thisOne != otherOne;
            }
            return true;
        }

        /// <summary>
        ///     Checks if there is a match given the neighbor matching rule and a Tile with a rotation angle.
        /// </summary>
        /// <param name="ruleMatch">Neighbor matching rule.</param>
        /// <param name="tilemapData">Tilemap to match.</param>
        /// <param name="tileData">Tile to match.</param>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="angle">Rotation angle for matching.</param>
        /// <param name="mirrorX">Mirror X Axis for matching.</param>
        /// <returns>True if there is a match, False if not.</returns>
        [BurstCompile]
        public static bool RuleMatches(ref RuleMatchInput ruleMatch
            , ref TilemapDataStruct tilemapData
            , ref RuleEntityIdTileDataStruct tileData, ref int3 position, int angle, bool mirrorX = false)
        {
            var minCount = math.min(ruleMatch.neighbors.Length, ruleMatch.matchingRules.Length);
            for (var i = 0; i < minCount; i++)
            {
                var neighborPosition = position;
                var neighbor = ruleMatch.matchingRules[i];
                var neighborOffset = ruleMatch.neighbors[i];
                if (mirrorX)
                    GetMirroredPosition(ref neighborOffset, true, false);
                GetRotatedPosition(ref neighborOffset, angle);
                GetOffsetPosition(ref neighborPosition, ref neighborOffset);
                var other = tilemapData.GetTileId(neighborPosition);
                if (!RuleMatch(neighbor, tileData.tileId, other)) return false;
            }
            return true;
        }

        /// <summary>
        ///     Checks if there is a match given the neighbor matching rule and a Tile with mirrored axii.
        /// </summary>
        /// <param name="ruleMatch">Neighbor matching rule.</param>
        /// <param name="tilemapData">Tilemap to match.</param>
        /// <param name="tileData">Tile to match.</param>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="mirrorX">Mirror X Axis for matching.</param>
        /// <param name="mirrorY">Mirror Y Axis for matching.</param>
        /// <returns>True if there is a match, False if not.</returns>
        [BurstCompile]
        public static bool RuleMatches(ref RuleMatchInput ruleMatch
            , ref TilemapDataStruct tilemapData
            , ref RuleEntityIdTileDataStruct tileData, ref int3 position, bool mirrorX, bool mirrorY)
        {
            var minCount = math.min(ruleMatch.neighbors.Length, ruleMatch.matchingRules.Length);
            for (var i = 0; i < minCount; i++)
            {
                var neighborPosition = position;
                var neighbor = ruleMatch.matchingRules[i];
                var neighborOffset = ruleMatch.neighbors[i];
                GetMirroredPosition(ref neighborOffset, mirrorX, mirrorY);
                GetOffsetPosition(ref neighborPosition, ref neighborOffset);
                var other = tilemapData.GetTileId(neighborPosition);
                if (!RuleMatch(neighbor, tileData.tileId, other)) return false;
            }
            return true;
        }

        /// <summary>
        ///     Gets a rotated position given its original position and the rotation in degrees.
        /// </summary>
        /// <param name="position">Original position of Tile.</param>
        /// <param name="rotation">Rotation in degrees.</param>
        /// <returns>Rotated position of Tile.</returns>
        [BurstCompile]
        private static void GetRotatedPosition(ref int3 position, int rotation)
        {
            switch (rotation)
            {
                case 90:
                    position = new int3(position.y, -position.x, 0);
                    break;
                case 180:
                    position =new int3(-position.x, -position.y, 0);
                    break;
                case 270:
                    position = new int3(-position.y, position.x, 0);
                    break;
            }
        }

        /// <summary>
        ///     Gets a mirrored position given its original position and the mirroring axii.
        /// </summary>
        /// <param name="position">Original position of Tile.</param>
        /// <param name="mirrorX">Mirror in the X Axis.</param>
        /// <param name="mirrorY">Mirror in the Y Axis.</param>
        /// <returns>Mirrored position of Tile.</returns>
        [BurstCompile]
        private static void GetMirroredPosition(ref int3 position, bool mirrorX, bool mirrorY)
        {
            if (mirrorX)
                position.x *= -1;
            if (mirrorY)
                position.y *= -1;
        }

        /// <summary>
        ///     Get the offset for the given position with the given offset.
        /// </summary>
        /// <param name="position">Position to offset.</param>
        /// <param name="offset">Offset for the position.</param>
        /// <returns>The offset position.</returns>
        [BurstCompile]
        private static void GetOffsetPosition(ref int3 position, ref int3 offset)
        {
            position += offset;
        }

        /// <summary>
        ///     Get the reversed offset for the given position with the given offset.
        /// </summary>
        /// <param name="position">Position to offset.</param>
        /// <param name="offset">Offset for the position.</param>
        /// <returns>The reversed offset position.</returns>
        [BurstCompile]
        private static void GetOffsetPositionReverse(ref int3 position, ref int3 offset)
        {
            position -= offset;
        }

        /// <summary>
        ///     The data structure holding the Rule information for matching Rule Tiles with
        ///     its neighbors.
        /// </summary>
        [Serializable]
        public class TilingRuleOutput
        {
            /// <summary>
            ///     Id for this Rule.
            /// </summary>
            public int m_Id;

            /// <summary>
            ///     The output Sprites for this Rule.
            /// </summary>
            public Sprite[] m_Sprites = new Sprite[1];

            /// <summary>
            ///     The output GameObject for this Rule.
            /// </summary>
            public GameObject m_GameObject;

            /// <summary>
            ///     The output minimum Animation Speed for this Rule.
            /// </summary>
            public float m_MinAnimationSpeed = 1f;

            /// <summary>
            ///     The output maximum Animation Speed for this Rule.
            /// </summary>
            public float m_MaxAnimationSpeed = 1f;

            /// <summary>
            ///     The perlin scale factor for this Rule.
            /// </summary>
            public float m_PerlinScale = 0.5f;

            /// <summary>
            ///     The output type for this Rule.
            /// </summary>
            public RuleMatchSerializable.OutputSprite m_Output = RuleMatchSerializable.OutputSprite.Single;

            /// <summary>
            ///     The output Collider Type for this Rule.
            /// </summary>
            public Tile.ColliderType m_ColliderType = Tile.ColliderType.Sprite;

            /// <summary>
            ///     The randomized transform output for this Rule.
            /// </summary>
            public RuleMatchSerializable.TransformMatch m_RandomTransform;

            /// <summary>
            ///     The enumeration for matching Neighbors when matching Rule Tiles
            /// </summary>
            public class Neighbor
            {
                /// <summary>
                ///     The Rule Tile will check if the contents of the cell in that direction is an instance of this Rule Tile.
                ///     If not, the rule will fail.
                /// </summary>
                public const int This = 1;

                /// <summary>
                ///     The Rule Tile will check if the contents of the cell in that direction is not an instance of this Rule Tile.
                ///     If it is, the rule will fail.
                /// </summary>
                public const int NotThis = 2;
            }

            /// <summary>
            /// Copies the TilingRule to a RuleMatchInput struct for use with Unity Jobs
            /// </summary>
            /// <param name="input">RuleMatchInput to copy to</param>
            public virtual void ToRuleMatchInput(ref RuleMatchInput input)
            {

            }

            /// <summary>
            /// Copies the TilingRule to a RuleMatchOutput struct for use with Unity Jobs
            /// </summary>
            /// <param name="output">RuleMatchOutput to copy to</param>
            public virtual void ToRuleMatchOutput(ref RuleMatchOutput output)
            {

            }
        }

        /// <summary>
        ///     The data structure holding the Rule information for matching Rule Tiles with
        ///     its neighbors.
        /// </summary>
        [Serializable]
        public class TilingRule : TilingRuleOutput
        {
            /// <summary>
            ///     The matching Rule conditions for each of its neighboring Tiles.
            /// </summary>
            public List<int> m_Neighbors = new();

            /// <summary>
            ///     * Preset this list to RuleEntityIdTile backward compatible, but not support for HexagonalRuleEntityIdTile backward compatible.
            /// </summary>
            public List<Vector3Int> m_NeighborPositions = new()
            {
                new(-1, 1, 0),
                new(0, 1, 0),
                new(1, 1, 0),
                new(-1, 0, 0),
                new(1, 0, 0),
                new(-1, -1, 0),
                new(0, -1, 0),
                new(1, -1, 0)
            };

            /// <summary>
            ///     The transform matching Rule for this Rule.
            /// </summary>
            public RuleMatchSerializable.TransformMatch m_RuleTransform;

            /// <summary>
            ///     This clones a copy of the TilingRule.
            /// </summary>
            /// <returns>A copy of the TilingRule.</returns>
            public TilingRule Clone()
            {
                var rule = new TilingRule
                {
                    m_Neighbors = new List<int>(m_Neighbors),
                    m_NeighborPositions = new List<Vector3Int>(m_NeighborPositions),
                    m_RuleTransform = m_RuleTransform,
                    m_Sprites = new Sprite[m_Sprites.Length],
                    m_GameObject = m_GameObject,
                    m_MinAnimationSpeed = m_MinAnimationSpeed,
                    m_MaxAnimationSpeed = m_MaxAnimationSpeed,
                    m_PerlinScale = m_PerlinScale,
                    m_Output = m_Output,
                    m_ColliderType = m_ColliderType,
                    m_RandomTransform = m_RandomTransform
                };
                Array.Copy(m_Sprites, rule.m_Sprites, m_Sprites.Length);
                return rule;
            }

            /// <summary>
            ///     Returns all neighbors of this Tile as a dictionary
            /// </summary>
            /// <returns>A dictionary of neighbors for this Tile</returns>
            public Dictionary<Vector3Int, int> GetNeighbors()
            {
                var dict = new Dictionary<Vector3Int, int>();

                for (var i = 0; i < m_Neighbors.Count && i < m_NeighborPositions.Count; i++)
                    dict.Add(m_NeighborPositions[i], m_Neighbors[i]);

                return dict;
            }

            /// <summary>
            ///     Applies the values from the given dictionary as this Tile's neighbors
            /// </summary>
            /// <param name="dict">Dictionary to apply values from</param>
            public void ApplyNeighbors(Dictionary<Vector3Int, int> dict)
            {
                m_NeighborPositions = new List<Vector3Int>(dict.Keys);
                m_Neighbors = new List<int>(dict.Values);
            }

            /// <summary>
            ///     Gets the cell bounds of the TilingRule.
            /// </summary>
            /// <returns>Returns the cell bounds of the TilingRule.</returns>
            public BoundsInt GetBounds()
            {
                var bounds = new BoundsInt(Vector3Int.zero, Vector3Int.one);
                foreach (var neighbor in GetNeighbors())
                {
                    bounds.xMin = Mathf.Min(bounds.xMin, neighbor.Key.x);
                    bounds.yMin = Mathf.Min(bounds.yMin, neighbor.Key.y);
                    bounds.xMax = Mathf.Max(bounds.xMax, neighbor.Key.x + 1);
                    bounds.yMax = Mathf.Max(bounds.yMax, neighbor.Key.y + 1);
                }

                return bounds;
            }

            /// <summary>
            /// Copies the TilingRule to a RuleMatchInput struct for use with Unity Jobs
            /// </summary>
            /// <param name="input">RuleMatchInput to copy to</param>
            public override void ToRuleMatchInput(ref RuleMatchInput input)
            {
                var neighborCount = 0;
                var matchingRulesCount = 0;
                if (m_NeighborPositions != null)
                    neighborCount = m_NeighborPositions.Count;
                if (m_Neighbors != null)
                    matchingRulesCount = m_Neighbors.Count;

                input.tileId = EntityId.None;
                input.groupId = 0;
                input.enabled = 1;
                input.neighbors = new NativeArray<int3>(neighborCount, Allocator.Persistent);
                input.matchingRules = new NativeArray<int>(matchingRulesCount, Allocator.Persistent);
                input.bounds = GetBounds();
                input.transformMatch = m_RuleTransform;

                for (var i = 0; i < neighborCount; ++i)
                {
                    input.neighbors[i] = m_NeighborPositions[i].ToInt3();
                }
                for (var i = 0; i < matchingRulesCount; ++i)
                {
                    input.matchingRules[i] = m_Neighbors[i];
                }
            }

            /// <summary>
            /// Copies the TilingRule to a RuleMatchOutput struct for use with Unity Jobs
            /// </summary>
            /// <param name="output">RuleMatchOutput to copy to</param>
            public override void ToRuleMatchOutput(ref RuleMatchOutput output)
            {
                var spritesCount = m_Sprites.Length;
                var gameObjectsCount = 1;
                output.sprites = new NativeArray<EntityId>(spritesCount, Allocator.Persistent);
                output.gameObjects = new NativeArray<EntityId>(gameObjectsCount, Allocator.Persistent);
                output.minAnimationSpeed = m_MinAnimationSpeed;
                output.maxAnimationSpeed = m_MaxAnimationSpeed;
                output.perlinScale = m_PerlinScale;
                output.spriteOutput = m_Output;
                output.colliderType = m_ColliderType;

                for (var i = 0; i < spritesCount; ++i)
                {
                    var sprite = m_Sprites[i];
                    output.sprites[i++] = sprite != null ? sprite.GetEntityId() : EntityId.None;
                }
                output.gameObjects[0] = m_GameObject != null ? m_GameObject.GetEntityId() : EntityId.None;
            }
        }

        /// <summary>
        ///     Attribute which marks a property which cannot be overridden by a RuleOverrideTile
        /// </summary>
        public class DontOverride : Attribute
        {
        }
    }
}
