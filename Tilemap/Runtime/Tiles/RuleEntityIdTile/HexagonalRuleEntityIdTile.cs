using System;
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
    ///     Use this for Hexagonal Grids.
    /// </summary>
    [Serializable]
    [HelpURL(
        "https://docs.unity3d.com/Packages/com.unity.2d.tilemap.extras@latest/index.html?subfolder=/manual/RuleTile.html")]
    public class HexagonalRuleEntityIdTile : RuleEntityIdTile
    {
        private static readonly FixedList32Bytes<float> m_CosAngleArr1 = new FixedList32Bytes<float>()
        {
            math.cos(0 * math.TORADIANS), math.cos(-60 * math.TORADIANS), math.cos(-120 * math.TORADIANS),
            math.cos(-180 * math.TORADIANS), math.cos(-240 * math.TORADIANS), math.cos(-300 * math.TORADIANS)
        };

        private static readonly FixedList32Bytes<float> m_SinAngleArr1 = new FixedList32Bytes<float>()
        {
            math.sin(0 * math.TORADIANS), math.sin(-60 * math.TORADIANS), math.sin(-120 * math.TORADIANS),
            math.sin(-180 * math.TORADIANS), math.sin(-240 * math.TORADIANS), math.sin(-300 * math.TORADIANS)
        };

        private static readonly FixedList32Bytes<float> m_CosAngleArr2 = new FixedList32Bytes<float>()
        {
            math.cos(0 * math.TORADIANS), math.cos(60 * math.TORADIANS), math.cos(120 * math.TORADIANS),
            math.cos(180 * math.TORADIANS), math.cos(240 * math.TORADIANS), math.cos(300 * math.TORADIANS)
        };

        private static readonly FixedList32Bytes<float> m_SinAngleArr2 = new FixedList32Bytes<float>()
        {
            math.sin(0 * math.TORADIANS), math.sin(60 * math.TORADIANS), math.sin(120 * math.TORADIANS),
            math.sin(180 * math.TORADIANS), math.sin(240 * math.TORADIANS), math.sin(300 * math.TORADIANS)
        };

        private static readonly float m_TilemapToWorldYScale = math.pow(1 - math.pow(0.5f, 2f), 0.5f);

        /// <summary>
        ///     Whether this is a flat top Hexagonal Tile
        /// </summary>
        [DontOverride] public bool m_FlatTop;

        private HexagonalRuleEntityIdTileDataStruct m_Data;

        /// <summary>
        ///     Angle in which the HexagonalRuleTile is rotated by for matching in Degrees.
        /// </summary>
        public override int m_RotationAngle => 60;

        /// <summary>
        /// Data struct for HexagonalRuleEntityIdTile to be used in Unity Jobs
        /// </summary>
        public struct HexagonalRuleEntityIdTileDataStruct : IDisposable
        {
            /// <summary>
            /// HexagonalRuleEntityIdTile's EntityId
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
            /// Rotation Angle for HexagonalRuleEntityIdTile
            /// </summary>
            public int rotationAngle;

            /// <summary>
            /// Whether HexagonalRuleEntityIdTile is flat top
            /// </summary>
            public bool flatTop;

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
        /// Returns the type of data struct used for the HexagonalRuleEntityIdTile,
        /// which is the type of HexagonalRuleEntityIdTileDataStruct.
        /// </summary>
        public override Type structType => typeof(HexagonalRuleEntityIdTileDataStruct);

        /// <summary>
        /// Copies the Data struct used for this HexagonalRuleEntityIdTile to the outPtr buffer
        /// for use in Unity Jobs.
        /// </summary>
        /// <param name="outPtr">Data buffer to copy data struct from HexagonalRuleEntityIdTile to.</param>
        public override unsafe void CopyDataStruct(void* outPtr)
        {
            UnsafeUtility.CopyStructureToPtr(ref m_Data, outPtr);
        }

        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to refresh HexagonalRuleEntityIdTiles
        /// </summary>
        protected override unsafe RefreshTileJobDelegate refreshTileJobDelegate => RefreshTileJob;

        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to get Tile Data from HexagonalRuleEntityIdTiles
        /// </summary>
        protected override unsafe GetTileDataJobDelegate getTileDataJobDelegate => GetTileDataJob;

        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to get Tile Animation Data from HexagonalRuleEntityIdTiles
        /// </summary>
        protected override unsafe GetTileAnimationDataJobDelegate getTileAnimationDataJobDelegate =>
            GetTileAnimationDataJob;

        /// <summary>
        /// Initializes the HexagonalRuleEntityIdTile.
        /// </summary>
        public override void OnEnable()
        {
            base.OnEnable();
            OnValidate();
        }

        /// <summary>
        /// Cleans up the HexagonalRuleEntityIdTile.
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
            m_Data = new HexagonalRuleEntityIdTileDataStruct()
            {
                tileId = cachedEntityId,
                random = new Unity.Mathematics.Random(r),
                ruleMatchInputs = new NativeArray<RuleMatchInput>(m_TilingRules.Count, Allocator.Persistent),
                ruleMatchOutputs = new NativeArray<RuleMatchOutput>(m_TilingRules.Count, Allocator.Persistent),
                rotationAngle = 60,
                flatTop = m_FlatTop,
                defaultTileData = new TileData()
                {
                    spriteEntityId = defaultSprite != null ? defaultSprite.GetEntityId() : EntityId.None,
                    color = Color.white,
                    transform = Matrix4x4.identity,
                    gameObjectEntityId = defaultGameObject != null ? defaultGameObject.GetEntityId() : EntityId.None,
                    flags = TileFlags.LockAll,
                    colliderType = defaultColliderType
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
            var dataStruct = UnsafeUtility.AsRef<HexagonalRuleEntityIdTileDataStruct>(data);
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
                                tileData.spriteEntityId = ruleMatchOutput.sprites[0];
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
                        tileData.gameObjectEntityId = ruleMatchOutput.gameObjects[0];
                        tileData.colliderType = ruleMatchOutput.colliderType;
                        break;
                    }
                }
            }
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
            , ref HexagonalRuleEntityIdTileDataStruct tileData
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
            , ref HexagonalRuleEntityIdTileDataStruct tileData, ref int3 position, int angle, bool mirrorX = false)
        {
            var minCount = math.min(ruleMatch.neighbors.Length, ruleMatch.matchingRules.Length);
            for (var i = 0; i < minCount; i++)
            {
                var neighborPosition = position;
                var neighbor = ruleMatch.matchingRules[i];
                var neighborOffset = ruleMatch.neighbors[i];
                if (mirrorX)
                    GetMirroredPosition(ref neighborOffset, true, false, tileData.flatTop);
                GetRotatedPosition(ref neighborOffset, angle, tileData.flatTop);
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
            , ref HexagonalRuleEntityIdTileDataStruct tileData, ref int3 position, bool mirrorX, bool mirrorY)
        {
            var minCount = math.min(ruleMatch.neighbors.Length, ruleMatch.matchingRules.Length);
            for (var i = 0; i < minCount; i++)
            {
                var neighborPosition = position;
                var neighbor = ruleMatch.matchingRules[i];
                var neighborOffset = ruleMatch.neighbors[i];
                GetMirroredPosition(ref neighborOffset, mirrorX, mirrorY, tileData.flatTop);
                GetOffsetPosition(ref neighborPosition, ref neighborOffset);
                var other = tilemapData.GetTileId(neighborPosition);
                if (!RuleMatch(neighbor, tileData.tileId, other)) return false;
            }
            return true;
        }

        /// <summary>
        ///     Converts a Tilemap Position to World Position.
        /// </summary>
        /// <param name="tilemapPosition">Tilemap Position to convert.</param>
        /// <param name="worldPosition">Converted World Position.</param>
        [BurstCompile]
        private static void TilemapPositionToWorldPosition(in int3 tilemapPosition, out float3 worldPosition)
        {
            worldPosition = new float3(tilemapPosition.x, tilemapPosition.y, tilemapPosition.z);
            if (tilemapPosition.y % 2 != 0)
                worldPosition.x += 0.5f;
            worldPosition.y *= m_TilemapToWorldYScale;
        }

        /// <summary>
        ///     Converts a World Position to Tilemap Position.
        /// </summary>
        /// <param name="worldPosition">World Position to convert.</param>
        /// <param name="tilemapPosition">Converted Tile Position.</param>
        [BurstCompile]
        private static void WorldPositionToTilemapPosition(in float3 worldPosition, out int3 tilemapPosition)
        {
            var worldPos = worldPosition;
            worldPos.y /= m_TilemapToWorldYScale;
            tilemapPosition = new int3();
            tilemapPosition.y = Mathf.RoundToInt(worldPos.y);
            if (tilemapPosition.y % 2 != 0)
                tilemapPosition.x = Mathf.RoundToInt(worldPos.x - 0.5f);
            else
                tilemapPosition.x = Mathf.RoundToInt(worldPos.x);
        }

        /// <summary>
        ///     Gets a rotated position given its original position and the rotation in degrees.
        /// </summary>
        /// <param name="position">Original position of Tile.</param>
        /// <param name="rotation">Rotation in degrees.</param>
        /// <returns>Rotated position of Tile.</returns>
        [BurstCompile]
        public override int3 GetRotatedPosition(int3 position, int rotation)
        {
            var curPos = position;
            GetRotatedPosition(ref curPos, rotation, m_FlatTop);
            return curPos;
        }

        /// <summary>
        ///     Gets a mirrored position given its original position and the mirroring axii.
        /// </summary>
        /// <param name="position">Original position of Tile.</param>
        /// <param name="mirrorX">Mirror in the X Axis.</param>
        /// <param name="mirrorY">Mirror in the Y Axis.</param>
        /// <returns>Mirrored position of Tile.</returns>
        public override int3 GetMirroredPosition(int3 position, bool mirrorX, bool mirrorY)
        {
            var mirPos = position;
            GetMirroredPosition(ref mirPos, mirrorX, mirrorY, m_FlatTop);
            return mirPos;
        }

        /// <summary>
        ///     Get the offset for the given position with the given offset.
        /// </summary>
        /// <param name="position">Position to offset.</param>
        /// <param name="offset">Offset for the position.</param>
        /// <returns>The offset position.</returns>
        public override int3 GetOffsetPosition(int3 position, int3 offset)
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
        public override int3 GetOffsetPositionReverse(int3 position, int3 offset)
        {
            var curPos = position;
            GetOffsetPositionReverse(ref curPos, ref offset, m_FlatTop);
            return curPos;
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
            var offsetPosition = position + offset;
            if (offset.y % 2 != 0 && position.y % 2 != 0)
                offsetPosition.x += 1;
            position = offsetPosition;
        }

        /// <summary>
        ///     Get the reversed offset for the given position with the given offset.
        /// </summary>
        /// <param name="position">Position to offset.</param>
        /// <param name="offset">Offset for the position.</param>
        /// <param name="flatTop">Whether Tile is Flat Top.</param>
        /// <returns>The reversed offset position.</returns>
        [BurstCompile]
        private static void GetOffsetPositionReverse(ref int3 position, ref int3 offset, bool flatTop)
        {
            var rotatedOffset = offset;
            GetRotatedPosition(ref rotatedOffset, 180, flatTop);
            GetOffsetPosition(ref position, ref rotatedOffset);
        }

        /// <summary>
        ///     Gets a rotated position given its original position and the rotation in degrees.
        /// </summary>
        /// <param name="position">Original position of Tile.</param>
        /// <param name="rotation">Rotation in degrees.</param>
        /// <param name="flatTop">Whether Tile is Flat Top.</param>
        /// <returns>Rotated position of Tile.</returns>
        [BurstCompile]
        private static void GetRotatedPosition(ref int3 position, int rotation, bool flatTop)
        {
            if (rotation != 0)
            {
                TilemapPositionToWorldPosition(position, out var worldPosition);

                var index = rotation / 60;
                if (flatTop)
                    worldPosition = new Vector3(
                        worldPosition.x * m_CosAngleArr2[index] - worldPosition.y * m_SinAngleArr2[index],
                        worldPosition.x * m_SinAngleArr2[index] + worldPosition.y * m_CosAngleArr2[index]
                    );
                else
                    worldPosition = new Vector3(
                        worldPosition.x * m_CosAngleArr1[index] - worldPosition.y * m_SinAngleArr1[index],
                        worldPosition.x * m_SinAngleArr1[index] + worldPosition.y * m_CosAngleArr1[index]
                    );

                WorldPositionToTilemapPosition(worldPosition, out position);
            }
        }

        /// <summary>
        ///     Gets a mirrored position given its original position and the mirroring axii.
        /// </summary>
        /// <param name="position">Original position of Tile.</param>
        /// <param name="mirrorX">Mirror in the X Axis.</param>
        /// <param name="mirrorY">Mirror in the Y Axis.</param>
        /// <param name="flatTop">Whether Tile is Flat Top.</param>
        /// <returns>Mirrored position of Tile.</returns>
        [BurstCompile]
        private static void GetMirroredPosition(ref int3 position, bool mirrorX, bool mirrorY, bool flatTop)
        {
            if (mirrorX || mirrorY)
            {
                TilemapPositionToWorldPosition(position, out var worldPosition);

                if (flatTop)
                {
                    if (mirrorX)
                        worldPosition.y *= -1;
                    if (mirrorY)
                        worldPosition.x *= -1;
                }
                else
                {
                    if (mirrorX)
                        worldPosition.x *= -1;
                    if (mirrorY)
                        worldPosition.y *= -1;
                }

                WorldPositionToTilemapPosition(worldPosition, out position);
            }
        }
    }
}
