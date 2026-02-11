using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.Tilemaps.Experimental
{
    /// <summary>
    /// A Serializable class for storing Rule Match information for RuleEntityIdTile
    /// </summary>
    [BurstCompile]
    [Serializable]
    public class RuleMatchSerializable
    {
        /// <summary>
        ///     The Output for the Tile which fits this Rule.
        /// </summary>
        public enum OutputSprite
        {
            /// <summary>
            ///     A Single Sprite will be output.
            /// </summary>
            Single,

            /// <summary>
            ///     A Random Sprite will be output.
            /// </summary>
            Random,

            /// <summary>
            ///     A Sprite Animation will be output.
            /// </summary>
            Animation
        }

        /// <summary>
        ///     The enumeration for the transform rule used when matching Rule Tiles.
        /// </summary>
        public enum TransformMatch
        {
            /// <summary>
            ///     The Rule Tile will match Tiles exactly as laid out in its neighbors.
            /// </summary>
            Fixed,

            /// <summary>
            ///     The Rule Tile will rotate and match its neighbors.
            /// </summary>
            Rotated,

            /// <summary>
            ///     The Rule Tile will mirror in the X axis and match its neighbors.
            /// </summary>
            MirrorX,

            /// <summary>
            ///     The Rule Tile will mirror in the Y axis and match its neighbors.
            /// </summary>
            MirrorY,

            /// <summary>
            ///     The Rule Tile will mirror in the X or Y axis and match its neighbors.
            /// </summary>
            MirrorXY,

            /// <summary>
            ///     The Rule Tile will rotate and mirror in the X and match its neighbors.
            /// </summary>
            RotatedMirror
        }

#region Input
        /// <summary>
        /// Tile to match with
        /// </summary>
        public TileBase tile;
        /// <summary>
        /// Group to match with
        /// </summary>
        public int groupId;

        /// <summary>
        /// Whether this Rule is enabled and used for matching
        /// </summary>
        public bool enabled = true;

        /// <summary>
        /// List of neighbor positions to match with
        /// </summary>
        public List<Vector3Int> neighbors;
        /// <summary>
        /// List of Rules to match neighbors with
        /// </summary>
        public List<int> matchingRules;

        /// <summary>
        /// Transform rules to match neighbors with
        /// </summary>
        public TransformMatch transformMatch;
#endregion

#region Output
        /// <summary>
        /// List of output Sprites if Rule matches
        /// </summary>
        public List<Sprite> sprites;
        /// <summary>
        /// List of output GameObjects if Rule matches
        /// </summary>
        public List<GameObject> gameObjects;
        /// <summary>
        /// Minimum range for Animation Speed
        /// </summary>
        public float minAnimationSpeed;
        /// <summary>
        /// Maximum range for Animation Speed
        /// </summary>
        public float maxAnimationSpeed;
        /// <summary>
        /// Perlin Scale for randomizing output
        /// </summary>
        public float perlinScale;
        /// <summary>
        /// Type of Sprite output by Tile
        /// </summary>
        public OutputSprite spriteOutput;
        /// <summary>
        /// Collider Shape output for Tile
        /// </summary>
        public Tile.ColliderType colliderType;
#endregion

        /// <summary>
        ///     Gets the cell bounds of the TilingRule.
        /// </summary>
        /// <returns>Returns the cell bounds of the TilingRule.</returns>
        public BoundsInt GetBounds()
        {
            var bounds = new BoundsInt(Vector3Int.zero, Vector3Int.one);
            foreach (var neighbor in neighbors)
            {
                bounds.xMin = Mathf.Min(bounds.xMin, neighbor.x);
                bounds.yMin = Mathf.Min(bounds.yMin, neighbor.y);
                bounds.xMax = Mathf.Max(bounds.xMax, neighbor.x + 1);
                bounds.yMax = Mathf.Max(bounds.yMax, neighbor.y + 1);
            }
            return bounds;
        }

        /// <summary>
        /// Converts the RuleMatch to a RuleMatchInput struct for use with Unity Jobs
        /// </summary>
        /// <returns>RuleMatchInput struct</returns>
        public RuleMatchInput ToInput()
        {
            var neighborCount = 0;
            var matchingRulesCount = 0;
            if (neighbors != null)
                neighborCount = neighbors.Count;
            if (matchingRules != null)
                matchingRulesCount = matchingRules.Count;
            var input = new RuleMatchInput()
            {
                tileId = tile != null ? tile.GetEntityId() : EntityId.None,
                groupId = groupId,
                enabled = enabled ? 1 : 0,
                neighbors = new NativeArray<int3>(neighborCount, Allocator.Persistent),
                matchingRules = new NativeArray<int>(matchingRulesCount, Allocator.Persistent),
                bounds = GetBounds(),
                transformMatch = transformMatch,
            };
            for (var i = 0; i < neighborCount; ++i)
            {
                input.neighbors[i] = neighbors[i].ToInt3();
            }
            for (var i = 0; i < matchingRulesCount; ++i)
            {
                input.matchingRules[i] = matchingRules[i];
            }
            return input;
        }

        /// <summary>
        /// Copies the RuleMatch to a RuleMatchInput struct for use with Unity Jobs
        /// </summary>
        /// <param name="input">RuleMatchInput to copy to</param>
        public void ToInput(ref RuleMatchInput input)
        {
            var neighborCount = 0;
            var matchingRulesCount = 0;
            if (neighbors != null)
                neighborCount = neighbors.Count;
            if (matchingRules != null)
                matchingRulesCount = matchingRules.Count;

            input.tileId = tile ? tile.GetEntityId() : EntityId.None;
            input.groupId = groupId;
            input.enabled = enabled ? 1 : 0;
            input.neighbors = new NativeArray<int3>(neighborCount, Allocator.Persistent);
            input.matchingRules = new NativeArray<int>(matchingRulesCount, Allocator.Persistent);
            input.bounds = GetBounds();
            input.transformMatch = transformMatch;

            for (var i = 0; i < neighborCount; ++i)
            {
                input.neighbors[i] = neighbors[i].ToInt3();
            }
            for (var i = 0; i < matchingRulesCount; ++i)
            {
                input.matchingRules[i] = matchingRules[i];
            }
        }

        /// <summary>
        /// Converts the RuleMatch to a RuleMatchOutput struct for use with Unity Jobs
        /// </summary>
        /// <returns>RuleMatchOutput struct</returns>
        public RuleMatchOutput ToOutput()
        {
            var spritesCount = 0;
            var gameObjectsCount = 0;
            if (neighbors != null)
                spritesCount = sprites.Count;
            if (matchingRules != null)
                gameObjectsCount = gameObjects.Count;

            var output = new RuleMatchOutput()
            {
                sprites = new NativeArray<EntityId>(spritesCount, Allocator.Persistent),
                gameObjects = new NativeArray<EntityId>(gameObjectsCount, Allocator.Persistent),
                minAnimationSpeed = minAnimationSpeed,
                maxAnimationSpeed = maxAnimationSpeed,
                perlinScale = perlinScale,
                spriteOutput = spriteOutput,
                colliderType = colliderType,
            };
            for (var i = 0; i < spritesCount; ++i)
            {
                var sprite = sprites[i];
                output.sprites[i++] = sprite ? sprite.GetEntityId() : EntityId.None;
            }
            for (var i = 0; i < gameObjectsCount; ++i)
            {
                var go = gameObjects[i];
                output.gameObjects[i++] = go ? go.GetEntityId() : EntityId.None;
            }
            return output;
        }

        /// <summary>
        /// Copies the RuleMatch to a RuleMatchOutput struct for use with Unity Jobs
        /// </summary>
        /// <param name="output">RuleMatchOutput struct to copy to</param>
        public void ToOutput(ref RuleMatchOutput output)
        {
            var spritesCount = 0;
            var gameObjectsCount = 0;
            if (sprites != null)
                spritesCount = sprites.Count;
            if (gameObjects != null)
                gameObjectsCount = gameObjects.Count;

            output.sprites = new NativeArray<EntityId>(spritesCount, Allocator.Persistent);
            output.gameObjects = new NativeArray<EntityId>(gameObjectsCount, Allocator.Persistent);
            output.minAnimationSpeed = minAnimationSpeed;
            output.maxAnimationSpeed = maxAnimationSpeed;
            output.perlinScale = perlinScale;
            output.spriteOutput = spriteOutput;
            output.colliderType = colliderType;

            for (var i = 0; i < spritesCount; ++i)
            {
                var sprite = sprites[i];
                output.sprites[i++] = sprite ? sprite.GetEntityId() : EntityId.None;
            }
            for (var i = 0; i < gameObjectsCount; ++i)
            {
                var go = gameObjects[i];
                output.gameObjects[i++] = go ? go.GetEntityId() : EntityId.None;
            }
        }
    }

    /// <summary>
    /// Data struct containing Rule Match Input
    /// </summary>
    [BurstCompile]
    public struct RuleMatchInput : IDisposable
    {
        /// <summary>
        /// EntityId of Tile to match with
        /// </summary>
        public EntityId tileId;
        /// <summary>
        /// Group to match with
        /// </summary>
        public int groupId;
        /// <summary>
        /// Whether this Rule is enabled and used for matching
        /// </summary>
        public int enabled;

        /// <summary>
        /// NativeArray of neighbor positions to match with
        /// </summary>
        public NativeArray<int3> neighbors;
        /// <summary>
        /// NativeArray of Rules to match neighbors with
        /// </summary>
        public NativeArray<int> matchingRules;
        /// <summary>
        /// Bounds of the neighbors
        /// </summary>
        public BoundsInt bounds;
        /// <summary>
        /// Transform rules to match neighbors with
        /// </summary>
        public RuleMatchSerializable.TransformMatch transformMatch;

        /// <summary>
        /// Disposes memory allocations used by RuleMatchInput
        /// </summary>
        public void Dispose()
        {
            if (neighbors.IsCreated)
                neighbors.Dispose();
            if (matchingRules.IsCreated)
                matchingRules.Dispose();
        }
    }

    /// <summary>
    /// Data struct containing Rule Match Output
    /// </summary>
    [BurstCompile]
    public struct RuleMatchOutput : IDisposable
    {
        /// <summary>
        /// NativeArray of output Sprite EntityIds if Rule matches
        /// </summary>
        public NativeArray<EntityId> sprites;
        /// <summary>
        /// NativeArray of output GameObject EntityIds if Rule matches
        /// </summary>
        public NativeArray<EntityId> gameObjects;
        /// <summary>
        /// Minimum range for Animation Speed
        /// </summary>
        public float minAnimationSpeed;
        /// <summary>
        /// Maximum range for Animation Speed
        /// </summary>
        public float maxAnimationSpeed;
        /// <summary>
        /// Perlin Scale for randomizing output
        /// </summary>
        public float perlinScale;
        /// <summary>
        /// Type of Sprite output by Tile
        /// </summary>
        public RuleMatchSerializable.OutputSprite spriteOutput;
        /// <summary>
        /// Collider Shape output for Tile
        /// </summary>
        public Tile.ColliderType colliderType;

        /// <summary>
        /// Disposes memory allocations used by RuleMatchOutput
        /// </summary>
        public void Dispose()
        {
            if (sprites.IsCreated)
                sprites.Dispose();
            if (gameObjects.IsCreated)
                gameObjects.Dispose();
        }
    }
}
