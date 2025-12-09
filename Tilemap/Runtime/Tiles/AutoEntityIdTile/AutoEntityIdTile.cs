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
    /// Tile using AutoTiling mask and rules
    /// </summary>
    [HelpURL(
        "https://docs.unity3d.com/Packages/com.unity.2d.tilemap.extras@latest/index.html?subfolder=/manual/AutoEntityIdTile.html")]
    [BurstCompile]
    public class AutoEntityIdTile : EntityIdTileBase
    {
        internal static readonly float s_DefaultTextureScale = 1f;

        [Serializable]
        internal abstract class SerializedDictionary<TKey, TValue> : Dictionary<TKey, TValue>,
            ISerializationCallbackReceiver
        {
            [SerializeField, HideInInspector] private List<TKey> keyData = new List<TKey>();

            [SerializeField, HideInInspector] private List<TValue> valueData = new List<TValue>();

            void ISerializationCallbackReceiver.OnAfterDeserialize()
            {
                Clear();
                for (int i = 0; i < keyData.Count && i < valueData.Count; i++)
                {
                    this[keyData[i]] = valueData[i];
                }

                keyData.Clear();
                valueData.Clear();
            }

            void ISerializationCallbackReceiver.OnBeforeSerialize()
            {
                keyData.Clear();
                valueData.Clear();

                foreach (var item in this)
                {
                    keyData.Add(item.Key);
                    valueData.Add(item.Value);
                }
            }
        }

        [Serializable]
        internal class AutoTileData
        {
            [SerializeField] public List<Sprite> spriteList = new List<Sprite>();
            [SerializeField] public List<Texture2D> textureList = new List<Texture2D>();
        }

        [Serializable]
        internal class AutoTileDictionary : SerializedDictionary<uint, AutoTileData>
        {
        };

        /// <summary>
        /// MaskType for AutoTile
        /// </summary>
        public enum AutoTileMaskType
        {
            /// <summary>
            /// Mask for 2x2 blocks
            /// </summary>
            Mask_2x2,

            /// <summary>
            /// Mask for 3x3 blocks
            /// </summary>
            Mask_3x3
        }

        #region Tile Data

        /// <summary>
        /// The Default Sprite set when creating a new Rule.
        /// </summary>
        [SerializeField] public Sprite m_DefaultSprite;

        /// <summary>
        /// The Default GameObject set when creating a new Rule.
        /// </summary>
        [SerializeField] public GameObject m_DefaultGameObject;

        /// <summary>
        /// The Default Collider Type set when creating a new Rule.
        /// </summary>
        [SerializeField] public Tile.ColliderType m_DefaultColliderType = Tile.ColliderType.Sprite;

        /// <summary>
        /// Mask Type for the AutoTile
        /// </summary>
        [SerializeField] public AutoTileMaskType m_MaskType;

        [SerializeField] private bool m_Random;

        [SerializeField] private bool m_PhysicsShapeCheck;

        /// <summary>
        /// Use random Sprite for mask
        /// </summary>
        public bool random
        {
            get { return m_Random; }
            set
            {
                m_Random = value;
                OnValidate();
            }
        }

        /// <summary>
        /// Checks Physics Shape of Sprite before determining Collider Type
        /// </summary>
        public bool physicsShapeCheck
        {
            get { return m_PhysicsShapeCheck; }
            set
            {
                m_PhysicsShapeCheck = value;
                OnValidate();
            }
        }

        [SerializeField, HideInInspector] internal AutoTileDictionary m_AutoTileDictionary = new AutoTileDictionary();

        #endregion

        #region Editor Data

        /// <summary>
        /// List of Texture2Ds used by the AutoTile
        /// </summary>
        [SerializeField] public List<Texture2D> m_TextureList = new List<Texture2D>();

        /// <summary>
        /// List of Texture Scale used by the AutoTile
        /// </summary>
        [SerializeField] public List<float> m_TextureScaleList = new List<float>();

        #endregion

        private struct AutoEntityIdTileDataStruct : IDisposable
        {
            public EntityId entityId;
            public int spriteRandom;
            public TileData defaultTileData;
            public AutoTileMaskType maskType;
            public bool hasPhysicsShape;
            public NativeHashMap<uint, NativeArray<EntityId>> masktoSpriteEntityIdMap;
            public NativeHashMap<EntityId, bool> entityIdToPhysicsShapeMap;

            public void Dispose()
            {
                if (masktoSpriteEntityIdMap.IsCreated)
                {
                    foreach (var item in masktoSpriteEntityIdMap)
                    {
                        item.Value.Dispose();
                    }

                    masktoSpriteEntityIdMap.Dispose();
                }

                if (entityIdToPhysicsShapeMap.IsCreated)
                {
                    entityIdToPhysicsShapeMap.Dispose();
                }
            }
        }

        private AutoEntityIdTileDataStruct m_Data;

        /// <summary>
        /// Does initialization for AutoEntityIdTile
        /// </summary>
        public override void OnEnable()
        {
            base.OnEnable();
            OnValidate();
        }

        /// <summary>
        /// Does cleanup for AutoEntityIdTile
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

            m_Data = new AutoEntityIdTileDataStruct()
            {
                entityId = cachedEntityId,
                spriteRandom = m_Random ? 1 : 0,
                defaultTileData = new TileData()
                {
                    spriteEntityId = m_DefaultSprite != null ? m_DefaultSprite.GetEntityId() : EntityId.None,
                    color = Color.white,
                    transform = Matrix4x4.identity,
                    gameObjectEntityId =
                        m_DefaultGameObject != null ? m_DefaultGameObject.GetEntityId() : EntityId.None,
                    flags = TileFlags.LockAll,
                    colliderType = m_DefaultColliderType
                },
                maskType = m_MaskType,
                hasPhysicsShape = m_PhysicsShapeCheck,
                masktoSpriteEntityIdMap =
                    new NativeHashMap<uint, NativeArray<EntityId>>(m_AutoTileDictionary.Count, Allocator.Persistent),
                entityIdToPhysicsShapeMap =
                    new NativeHashMap<EntityId, bool>(m_AutoTileDictionary.Count, Allocator.Persistent),
            };
            foreach (var item in m_AutoTileDictionary)
            {
                uint mask = item.Key;
                var spriteEntityIds = new NativeArray<EntityId>(item.Value.spriteList.Count, Allocator.Persistent);
                for (var i = 0; i < item.Value.spriteList.Count; i++)
                {
                    var sprite = item.Value.spriteList[i];
                    var spriteEntityId = sprite != null ? sprite.GetEntityId() : EntityId.None;
                    spriteEntityIds[i] = spriteEntityId;
                    if (m_PhysicsShapeCheck && spriteEntityId != EntityId.None)
                    {
                        m_Data.entityIdToPhysicsShapeMap.TryAdd(spriteEntityId, sprite.GetPhysicsShapeCount() > 0);
                    }
                }
                m_Data.masktoSpriteEntityIdMap[mask] = spriteEntityIds;
            }
        }

        /// <summary>
        /// This method is called when the tile is refreshed.
        /// </summary>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="tilemap">The Tilemap the tile is present on.</param>
        public override void RefreshTile(Vector3Int position, ITilemap tilemap)
        {
            for (var y = -1; y <= 1; ++y)
            {
                for (var x = -1; x <= 1; ++x)
                {
                    tilemap.RefreshTile(new Vector3Int(position.x + x, position.y + y, position.z));
                }
            }
        }

        /// <summary>
        /// Retrieves any tile rendering data from the scripted tile.
        /// </summary>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="itilemap">The Tilemap the tile is present on.</param>
        /// <param name="tileData">Data to render the tile.</param>
        public override void GetTileData(Vector3Int position, ITilemap itilemap, ref TileData tileData)
        {
            var tilemapData = new TilemapDataStruct(itilemap.GetComponent<Tilemap>());
            unsafe
            {
                var pos = position.ToInt3();
                GetTileDataJob(1, &pos
                    , UnsafeUtility.AddressOf(ref m_Data)
                    , ref tilemapData
                    , (TileData*) UnsafeUtility.AddressOf(ref tileData));
            }
        }

        internal void AddSprite(Sprite sprite, Texture2D texture, uint mask)
        {
            if ((m_MaskType == AutoTileMaskType.Mask_2x2 && (mask >> 4) > 0)
                || (mask >> 9) > 0)
            {
                throw new ArgumentOutOfRangeException($"Mask {mask} is not valid for {m_MaskType}");
            }

            if (!m_AutoTileDictionary.TryGetValue(mask, out var autoTileData))
            {
                autoTileData = new AutoTileData();
                m_AutoTileDictionary.Add(mask, autoTileData);
            }

            var isInList = false;
            foreach (var spriteData in autoTileData.spriteList)
            {
                isInList = spriteData == sprite;
                if (isInList)
                    break;
            }

            if (!isInList)
            {
                autoTileData.spriteList.Add(sprite);
                autoTileData.textureList.Add(texture);
            }
        }

        internal void RemoveSprite(Sprite sprite, uint mask)
        {
            if (!m_AutoTileDictionary.TryGetValue(mask, out var autoTileData))
                return;

            var index = autoTileData.spriteList.IndexOf(sprite);
            if (index < 0)
                return;

            autoTileData.spriteList.RemoveAt(index);
            autoTileData.textureList.RemoveAt(index);
        }

        /// <summary>
        /// Validate AutoTile Data
        /// </summary>
        public void Validate()
        {
            if (m_MaskType == AutoTileMaskType.Mask_2x2)
            {
                var keyList = new List<uint>(m_AutoTileDictionary.Keys);
                foreach (var mask in keyList)
                {
                    if ((mask >> 4) > 0)
                    {
                        m_AutoTileDictionary.Remove(mask);
                    }
                }
            }

            foreach (var pair in m_AutoTileDictionary)
            {
                var autoTileData = pair.Value;
                for (var i = 0; i < autoTileData.spriteList.Count;)
                {
                    var sprite = autoTileData.spriteList[i];
                    var texture = autoTileData.textureList[i];
                    if (m_TextureList.Contains(texture))
                    {
                        ++i;
                    }
                    else
                    {
                        autoTileData.spriteList.RemoveAt(i);
                        autoTileData.textureList.RemoveAt(i);
                    }
                }
            }

            if (m_TextureList.Count != m_TextureScaleList.Count)
            {
                if (m_TextureList.Count > m_TextureScaleList.Count)
                    while (m_TextureList.Count - m_TextureScaleList.Count > 0)
                        m_TextureScaleList.Add(s_DefaultTextureScale);
                else if (m_TextureList.Count < m_TextureScaleList.Count)
                    while (m_TextureScaleList.Count - m_TextureList.Count > 0)
                        m_TextureScaleList.RemoveAt(m_TextureScaleList.Count - 1);
            }

            OnValidate();
        }

        [BurstCompile]
        private static void Convert2x2Mask(uint inMask, ref uint convertedMask)
        {
            // 4 8
            // 1 2
            convertedMask = 0;
            if ((inMask & 1 << 0) > 0 && (inMask & 1 << 1) > 0 && (inMask & 1 << 3) > 0)
                convertedMask |= 1 << 0;
            if ((inMask & 1 << 1) > 0 && (inMask & 1 << 2) > 0 && (inMask & 1 << 5) > 0)
                convertedMask |= 1 << 1;
            if ((inMask & 1 << 3) > 0 && (inMask & 1 << 6) > 0 && (inMask & 1 << 7) > 0)
                convertedMask |= 1 << 2;
            if ((inMask & 1 << 5) > 0 && (inMask & 1 << 7) > 0 && (inMask & 1 << 8) > 0)
                convertedMask |= 1 << 3;
        }

        [BurstCompile]
        private static void Convert3x3Mask(uint inMask, ref uint convertedMask)
        {
            convertedMask = inMask;
            // 64 128 256
            //  8  16  32
            //  1   2   4
            switch (inMask)
            {
                // Leftq
                case 1 + 16 + 32:
                case 4 + 16 + 32:
                case 16 + 32 + 64:
                case 16 + 32 + 256:
                case 1 + 16 + 32 + 64:
                case 4 + 16 + 32 + 256:
                case 1 + 4 + 16 + 32:
                case 1 + 16 + 32 + 256:
                case 4 + 16 + 32 + 64:
                case 16 + 32 + 64 + 256:
                case 1 + 16 + 32 + 64 + 256:
                case 1 + 4 + 16 + 32 + 64:
                case 1 + 4 + 16 + 32 + 256:
                case 4 + 16 + 32 + 64 + 256:
                case 1 + 4 + 16 + 32 + 64 + 256:
                {
                    convertedMask = 16 + 32;
                    break;
                }
                // Right
                case 1 + 8 + 16:
                case 8 + 16 + 64:
                case 1 + 8 + 16 + 64:
                case 4 + 8 + 16:
                case 8 + 16 + 256:
                case 1 + 4 + 8 + 16:
                case 1 + 8 + 16 + 256:
                case 4 + 8 + 16 + 64:
                case 4 + 8 + 16 + 256:
                case 8 + 16 + 64 + 256:
                case 1 + 4 + 8 + 16 + 64:
                case 1 + 4 + 8 + 16 + 256:
                case 1 + 8 + 16 + 64 + 256:
                case 4 + 8 + 16 + 64 + 256:
                case 1 + 4 + 8 + 16 + 64 + 256:
                {
                    convertedMask = 8 + 16;
                    break;
                }
                // Top
                case 1 + 2 + 16:
                case 2 + 4 + 16:
                case 1 + 2 + 4 + 16:
                case 2 + 16 + 64:
                case 2 + 16 + 256:
                case 2 + 16 + 64 + 256:
                case 1 + 2 + 16 + 64:
                case 1 + 2 + 16 + 256:
                case 1 + 2 + 16 + 64 + 256:
                case 2 + 4 + 16 + 64:
                case 2 + 4 + 16 + 256:
                case 2 + 4 + 16 + 64 + 256:
                case 1 + 2 + 4 + 16 + 64:
                case 1 + 2 + 4 + 16 + 256:
                case 1 + 2 + 4 + 16 + 64 + 256:
                {
                    convertedMask = 2 + 16;
                    break;
                }
                // Bottom
                case 16 + 64 + 128:
                case 16 + 128 + 256:
                case 16 + 64 + 128 + 256:
                case 1 + 16 + 64 + 128:
                case 4 + 16 + 64 + 128:
                case 1 + 4 + 16 + 64 + 128:
                case 1 + 16 + 128 + 256:
                case 4 + 16 + 128 + 256:
                case 1 + 4 + 16 + 128 + 256:
                case 1 + 16 + 64 + 128 + 256:
                case 4 + 16 + 64 + 128 + 256:
                case 1 + 4 + 16 + 64 + 128 + 256:
                case 1 + 16 + 128:
                case 4 + 16 + 128:
                case 1 + 4 + 16 + 128:
                {
                    convertedMask = 16 + 128;
                    break;
                }
                // Vertical Straight
                case 1 + 2 + 16 + 128:
                case 2 + 4 + 16 + 128:
                case 1 + 2 + 4 + 16 + 128:
                case 2 + 16 + 64 + 128:
                case 2 + 16 + 128 + 256:
                case 2 + 16 + 64 + 128 + 256:
                case 1 + 2 + 16 + 64 + 128:
                case 1 + 2 + 16 + 128 + 256:
                case 2 + 4 + 16 + 64 + 128:
                case 2 + 4 + 16 + 128 + 256:
                case 1 + 2 + 16 + 64 + 128 + 256:
                case 1 + 2 + 4 + 64 + 128 + 256:
                case 1 + 2 + 4 + 16 + 128 + 256:
                case 1 + 2 + 4 + 16 + 64 + 128:
                case 2 + 4 + 16 + 64 + 128 + 256:
                case 1 + 2 + 4 + 16 + 64 + 128 + 256:
                {
                    convertedMask = 2 + 16 + 128;
                    break;
                }
                // Horizontal Straight
                case 1 + 8 + 16 + 32:
                case 8 + 16 + 32 + 64:
                case 1 + 8 + 16 + 32 + 64:
                case 4 + 8 + 16 + 32:
                case 8 + 16 + 32 + 256:
                case 4 + 8 + 16 + 32 + 64:
                case 4 + 8 + 16 + 32 + 256:
                case 1 + 4 + 8 + 16 + 32:
                case 1 + 8 + 16 + 32 + 256:
                case 8 + 16 + 32 + 64 + 256:
                case 1 + 4 + 8 + 16 + 32 + 64:
                case 1 + 4 + 8 + 16 + 32 + 256:
                case 1 + 8 + 16 + 32 + 64 + 256:
                case 4 + 8 + 16 + 32 + 64 + 256:
                case 1 + 4 + 8 + 16 + 32 + 64 + 256:
                {
                    convertedMask = 8 + 16 + 32;
                    break;
                }
                // Top Left Corner
                case 1 + 2 + 4 + 16 + 32:
                case 2 + 4 + 16 + 32 + 256:
                case 2 + 4 + 16 + 32 + 64:
                case 1 + 2 + 4 + 16 + 32 + 256:
                case 1 + 2 + 4 + 16 + 32 + 64:
                case 2 + 4 + 16 + 32 + 64 + 256:
                case 1 + 2 + 4 + 16 + 32 + 64 + 256:
                {
                    convertedMask = 2 + 4 + 16 + 32;
                    break;
                }
                // Bottom Left Corner
                case 1 + 16 + 32 + 128 + 256:
                case 4 + 16 + 32 + 128 + 256:
                case 16 + 32 + 64 + 128 + 256:
                case 4 + 16 + 32 + 64 + 128 + 256:
                case 1 + 4 + 16 + 32 + 128 + 256:
                case 1 + 16 + 32 + 64 + 128 + 256:
                case 1 + 4 + 16 + 32 + 64 + 128 + 256:
                {
                    convertedMask = 16 + 32 + 128 + 256;
                    break;
                }
                // Top Right Corner
                case 1 + 2 + 4 + 8 + 16:
                case 1 + 2 + 8 + 16 + 64:
                case 1 + 2 + 8 + 16 + 256:
                case 1 + 2 + 4 + 8 + 16 + 64:
                case 1 + 2 + 8 + 16 + 64 + 256:
                case 1 + 2 + 4 + 8 + 16 + 256:
                case 1 + 2 + 4 + 8 + 16 + 64 + 256:
                {
                    convertedMask = 1 + 2 + 8 + 16;
                    break;
                }
                // Bottom Right Corner
                case 1 + 8 + 16 + 64 + 128:
                case 8 + 16 + 64 + 128 + 256:
                case 4 + 8 + 16 + 64 + 128:
                case 1 + 4 + 8 + 16 + 64 + 128:
                case 1 + 8 + 16 + 64 + 128 + 256:
                case 4 + 8 + 16 + 64 + 128 + 256:
                case 1 + 4 + 8 + 16 + 64 + 128 + 256:
                {
                    convertedMask = 8 + 16 + 64 + 128;
                    break;
                }
                // Full Top
                case 1 + 2 + 4 + 8 + 16 + 32 + 64:
                case 1 + 2 + 4 + 8 + 16 + 32 + 256:
                case 1 + 2 + 4 + 8 + 16 + 32 + 64 + 256:
                {
                    convertedMask = 1 + 2 + 4 + 8 + 16 + 32;
                    break;
                }
                // Full Bottom
                case 1 + 8 + 16 + 32 + 64 + 128 + 256:
                case 4 + 8 + 16 + 32 + 64 + 128 + 256:
                case 1 + 4 + 8 + 16 + 32 + 64 + 128 + 256:
                {
                    convertedMask = 8 + 16 + 32 + 64 + 128 + 256;
                    break;
                }
                // Full Left
                case 1 + 2 + 4 + 16 + 32 + 128 + 256:
                case 2 + 4 + 16 + 32 + 64 + 128 + 256:
                case 1 + 2 + 4 + 16 + 32 + 64 + 128 + 256:
                {
                    convertedMask = 2 + 4 + 16 + 32 + 128 + 256;
                    break;
                }
                // Full Right
                case 1 + 2 + 4 + 8 + 16 + 64 + 128:
                case 1 + 2 + 8 + 16 + 64 + 128 + 256:
                case 1 + 2 + 4 + 8 + 16 + 64 + 128 + 256:
                {
                    convertedMask = 1 + 2 + 8 + 16 + 64 + 128;
                    break;
                }
                // Top Left Tricorner
                case 1 + 2 + 16 + 32:
                case 2 + 16 + 32 + 64:
                case 2 + 16 + 32 + 256:
                case 1 + 2 + 16 + 32 + 64:
                case 1 + 2 + 16 + 32 + 256:
                case 2 + 16 + 32 + 64 + 256:
                case 1 + 2 + 16 + 32 + 64 + 256:
                {
                    convertedMask = 2 + 16 + 32;
                    break;
                }
                // Bottom Left Tricorner
                case 1 + 16 + 32 + 128:
                case 4 + 16 + 32 + 128:
                case 16 + 32 + 64 + 128:
                case 4 + 16 + 32 + 64 + 128:
                case 1 + 16 + 32 + 64 + 128:
                case 1 + 4 + 16 + 32 + 64 + 128:
                case 1 + 4 + 16 + 32 + 128:
                {
                    convertedMask = 16 + 32 + 128;
                    break;
                }
                // Top Right Tricorner
                case 2 + 4 + 8 + 16:
                case 2 + 8 + 16 + 64:
                case 2 + 8 + 16 + 256:
                case 2 + 4 + 8 + 16 + 64:
                case 2 + 8 + 16 + 64 + 256:
                case 2 + 4 + 8 + 16 + 256:
                case 2 + 4 + 8 + 16 + 64 + 256:
                {
                    convertedMask = 2 + 8 + 16;
                    break;
                }
                // Bottom Right Tricorner
                case 1 + 8 + 16 + 128:
                case 4 + 8 + 16 + 128:
                case 8 + 16 + 128 + 256:
                case 1 + 8 + 16 + 128 + 256:
                case 1 + 4 + 8 + 16 + 128:
                case 4 + 8 + 16 + 128 + 256:
                case 1 + 4 + 8 + 16 + 128 + 256:
                {
                    convertedMask = 8 + 16 + 128;
                    break;
                }
                // Three-way Left
                case 2 + 4 + 8 + 16 + 128:
                case 2 + 8 + 16 + 128 + 256:
                case 2 + 4 + 8 + 16 + 128 + 256:
                {
                    convertedMask = 2 + 8 + 16 + 128;
                    break;
                }
                // Three-way Right
                case 1 + 2 + 16 + 32 + 128:
                case 2 + 16 + 32 + 64 + 128:
                case 1 + 2 + 16 + 32 + 64 + 128:
                {
                    convertedMask = 2 + 16 + 32 + 128;
                    break;
                }
                // Three-way Top
                case 1 + 8 + 16 + 32 + 128:
                case 4 + 8 + 16 + 32 + 128:
                case 1 + 4 + 8 + 16 + 32 + 128:
                {
                    convertedMask = 8 + 16 + 32 + 128;
                    break;
                }
                // Three-way Bottom
                case 2 + 8 + 16 + 32 + 64:
                case 2 + 8 + 16 + 32 + 256:
                case 2 + 8 + 16 + 32 + 64 + 256:
                {
                    convertedMask = 2 + 8 + 16 + 32;
                    break;
                }
                // Three-corner Top Left
                case 2 + 4 + 8 + 16 + 32 + 64:
                case 2 + 4 + 8 + 16 + 32 + 256:
                case 2 + 4 + 8 + 16 + 32 + 64 + 256:
                {
                    convertedMask = 2 + 4 + 8 + 16 + 32;
                    break;
                }
                // Three-corner Bottom Left
                case 1 + 8 + 16 + 32 + 128 + 256:
                case 4 + 8 + 16 + 32 + 128 + 256:
                case 1 + 4 + 8 + 16 + 32 + 128 + 256:
                {
                    convertedMask = 8 + 16 + 32 + 128 + 256;
                    break;
                }
                // Three-corner Top Right
                case 1 + 2 + 8 + 16 + 32 + 64:
                case 1 + 2 + 8 + 16 + 32 + 256:
                case 1 + 2 + 8 + 16 + 32 + 64 + 256:
                {
                    convertedMask = 1 + 2 + 8 + 16 + 32;
                    break;
                }
                // Three-corner Bottom Right
                case 1 + 8 + 16 + 32 + 64 + 128:
                case 4 + 8 + 16 + 32 + 64 + 128:
                case 1 + 4 + 8 + 16 + 32 + 64 + 128:
                {
                    convertedMask = 8 + 16 + 32 + 64 + 128;
                    break;
                }
                // Left Side Top Right Corner
                case 1 + 2 + 4 + 16 + 32 + 128:
                case 2 + 4 + 16 + 32 + 64 + 128:
                case 1 + 2 + 4 + 16 + 32 + 64 + 128:
                {
                    convertedMask = 2 + 4 + 16 + 32 + 128;
                    break;
                }
                // Left Side Bottom Right Corner
                case 1 + 2 + 16 + 32 + 128 + 256:
                case 2 + 16 + 32 + 64 + 128 + 256:
                case 1 + 2 + 16 + 32 + 64 + 128 + 256:
                {
                    convertedMask = 2 + 16 + 32 + 128 + 256;
                    break;
                }
                // Right Side Top Left Corner
                case 1 + 2 + 4 + 8 + 16 + 128:
                case 1 + 2 + 8 + 16 + 128 + 256:
                case 1 + 2 + 4 + 8 + 16 + 128 + 256:
                {
                    convertedMask = 1 + 2 + 8 + 16 + 128;
                    break;
                }
                // Right Side Bottom Left Corner
                case 2 + 4 + 8 + 16 + 64 + 128:
                case 2 + 8 + 16 + 64 + 128 + 256:
                case 2 + 4 + 8 + 16 + 64 + 128 + 256:
                {
                    convertedMask = 2 + 8 + 16 + 64 + 128;
                    break;
                }
                // Single
                case 1 + 16:
                case 4 + 16:
                case 16 + 64:
                case 16 + 256:
                case 1 + 4 + 16:
                case 1 + 16 + 64:
                case 1 + 16 + 256:
                case 4 + 16 + 64:
                case 4 + 16 + 256:
                case 16 + 64 + 256:
                case 1 + 4 + 16 + 64:
                case 1 + 4 + 16 + 256:
                case 1 + 16 + 64 + 256:
                case 4 + 16 + 64 + 256:
                case 1 + 4 + 16 + 64 + 256:
                {
                    convertedMask = 16;
                    break;
                }
            }
        }

        /// <summary>
        /// Returns the type of data struct used for this AutoEntityIdTile
        /// </summary>
        public override Type structType => typeof(AutoEntityIdTileDataStruct);

        /// <summary>
        /// Copies the data struct used for this AutoEntityIdTile to the outPtr buffer
        /// for use in Unity Jobs.
        /// </summary>
        /// <param name="outPtr">Data buffer to copy data struct from AutoEntityIdTile to.</param>
        public override unsafe void CopyDataStruct(void* outPtr)
        {
            UnsafeUtility.CopyStructureToPtr(ref m_Data, outPtr);
        }

        /// <summary>
        /// Returns the delegate function to refresh tiles for AutoEntityIdTile for use in Unity Jobs
        /// </summary>
        protected override unsafe RefreshTileJobDelegate refreshTileJobDelegate => RefreshTileJob;
        /// <summary>
        /// Returns the delegate function to get Tile Data for AutoEntityIdTile for use in Unity Jobs
        /// </summary>
        protected override unsafe GetTileDataJobDelegate getTileDataJobDelegate => GetTileDataJob;
        /// <summary>
        /// Returns null as AutoEntityIdTile has no Tile Animation Data
        /// </summary>
        protected override GetTileAnimationDataJobDelegate getTileAnimationDataJobDelegate => null;

        [BurstCompile]
        [MonoPInvokeCallback(typeof(RefreshTileJobDelegate))]
        static unsafe void RefreshTileJob(int count, int3* position, void* data,
            ref TilemapRefreshStruct tilemapRefreshStruct)
        {
            var refreshPositions = new NativeArray<int3>(9, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < count; ++i)
            {
                var pos = *(position + i);
                for (var y = -1; y <= 1; ++y)
                {
                    for (var x = -1; x <= 1; ++x)
                    {
                        refreshPositions[(y + 1) * 3 + x + 1] = pos + new int3(x, y, 0);
                    }
                }
                tilemapRefreshStruct.RefreshTiles(refreshPositions);
            }
            refreshPositions.Dispose();
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(GetTileDataJobDelegate))]
        static unsafe void GetTileDataJob(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileData* outTileData)
        {
            var dataStruct = UnsafeUtility.AsRef<AutoEntityIdTileDataStruct>(data);
            var entityIds = new NativeArray<EntityId>(9, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var jobRandom = new Unity.Mathematics.Random();
            for (var i = 0; i < count; ++i)
            {
                ref int3 inPos = ref *(position + i);
                ref TileData tileData = ref *(outTileData + i);
                UnsafeUtility.CopyPtrToStructure(UnsafeUtility.AddressOf(ref dataStruct.defaultTileData), out tileData);

                var boundsInt = new BoundsInt(-1, -1, 0, 3, 3, 1);
                tilemapDataStruct.GetTilesFromBlockOffset(inPos, boundsInt, entityIds);

                uint mask = 0;
                for (var idx = 0; idx < entityIds.Length; ++idx)
                {
                    if (entityIds[idx] == dataStruct.entityId)
                    {
                        mask |= (uint) 1 << idx;
                    }
                }

                uint outMask = 0;
                switch (dataStruct.maskType)
                {
                    case AutoTileMaskType.Mask_2x2:
                        Convert2x2Mask(mask, ref outMask);
                        break;
                    case AutoTileMaskType.Mask_3x3:
                        Convert3x3Mask(mask, ref outMask);
                        break;
                };

                if (dataStruct.masktoSpriteEntityIdMap.TryGetValue(outMask, out var spriteEntityIds))
                {
                    if (spriteEntityIds.Length > 0)
                    {
                        if (dataStruct.spriteRandom > 0 && spriteEntityIds.Length > 1)
                        {
                            var rPos = inPos.GetHashCode() ^ dataStruct.entityId.GetHashCode();
                            var r = UnsafeUtility.As<int, uint>(ref rPos);
                            jobRandom.InitState(r);
                            tileData.spriteEntityId = spriteEntityIds[jobRandom.NextInt(spriteEntityIds.Length)];
                        }
                        else
                        {
                            tileData.spriteEntityId = spriteEntityIds[0];
                        }
                    }
                }

                if (dataStruct.hasPhysicsShape && tileData.colliderType == Tile.ColliderType.Sprite)
                {
                    if (dataStruct.entityIdToPhysicsShapeMap.TryGetValue(tileData.spriteEntityId, out var hasPhysicsShape))
                    {
                        tileData.colliderType = hasPhysicsShape ? Tile.ColliderType.Sprite : Tile.ColliderType.None;
                    }
                }
            }
        }
    }
}
