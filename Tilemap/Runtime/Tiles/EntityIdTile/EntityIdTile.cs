using System;
using Unity.Collections.LowLevel.Unsafe;
using AOT;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.Tilemaps.Experimental
{
    /// <summary>
    /// A default Tile to be placed in a Tilemap, using EntityId to
    /// reference managed Unity Objects.
    /// This is the Entity Id Tile equivalent for Tile.
    /// </summary>
    [BurstCompile]
    public class EntityIdTile : EntityIdTileBase
    {
        /// <summary>
        /// Sprite to be rendered at the Tile.
        /// </summary>
        public Sprite sprite
        {
            get { return m_Sprite; }
            set
            {
                m_Sprite = value;
                m_Data.spriteEntityId = m_Sprite != null ? m_Sprite.GetEntityId() : EntityId.None;
            }
        }

        /// <summary>
        /// Color of the Tile.
        /// </summary>
        public Color color
        {
            get { return m_Color; }
            set
            {
                m_Color = value;
                m_Data.color = m_Color;
            }
        }

        /// <summary>
        /// Transform matrix of the Tile.
        /// </summary>
        public Matrix4x4 transform
        {
            get { return m_Transform; }
            set
            {
                m_Transform = value;
                m_Data.transform = m_Transform;
            }
        }

        /// <summary>
        /// GameObject to be instantiated at the position
        /// where the Tile is placed.
        /// </summary>
        public GameObject gameObject
        {
            get { return m_InstancedGameObject; }
            set
            {
                m_InstancedGameObject = value;
                m_Data.gameObjectEntityId = m_InstancedGameObject != null ? m_InstancedGameObject.GetEntityId() : EntityId.None;
            }
        }

        /// <summary>
        /// TileFlags of the Tile.
        /// </summary>
        public TileFlags flags
        {
            get { return m_Flags; }
            set
            {
                m_Flags = value;
                m_Data.flags = m_Flags;
            }
        }

        /// <summary>
        /// ColliderType of the Tile.
        /// </summary>
        public Tile.ColliderType colliderType
        {
            get { return m_ColliderType; }
            set
            {
                m_ColliderType = value;
                m_Data.colliderType = m_ColliderType;
            }
        }

        [SerializeField]
        private Sprite m_Sprite;
        [SerializeField]
        private Color m_Color = Color.white;
        [SerializeField]
        private Matrix4x4 m_Transform = Matrix4x4.identity;
        [SerializeField]
        private GameObject m_InstancedGameObject;
        [SerializeField]
        private TileFlags m_Flags = TileFlags.LockColor;
        [SerializeField]
        private Tile.ColliderType m_ColliderType = Tile.ColliderType.Sprite;

        private TileData m_Data;

        /// <summary>
        /// Returns the type of data struct used for the EntityIdTile,
        /// which is the type of TileData.
        /// </summary>
        public override Type structType { get => typeof(TileData); }

        /// <summary>
        /// Copies the TileData struct used for this EntityIdTile to the outPtr buffer
        /// for use in Unity Jobs.
        /// </summary>
        /// <param name="outPtr">Data buffer to copy data struct from EntityIdTile to.</param>
        public override unsafe void CopyDataStruct(void* outPtr)
        {
            UnsafeUtility.CopyStructureToPtr(ref m_Data, outPtr);
        }

        private void OnValidate()
        {
            m_Data = new TileData()
            {
                spriteEntityId = m_Sprite != null ? m_Sprite.GetEntityId() : EntityId.None,
                color = m_Color,
                transform = m_Transform,
                gameObjectEntityId = m_InstancedGameObject  != null ? m_InstancedGameObject.GetEntityId() : EntityId.None,
                flags = m_Flags,
                colliderType = m_ColliderType,
            };
        }

        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to refresh EntityIdTiles
        /// </summary>
        protected override unsafe RefreshTileJobDelegate refreshTileJobDelegate => RefreshTileJob;

        /// <summary>
        /// Returns the delegate function called by Tilemap using Unity Jobs to get Tile Data from EntityIdTiles
        /// </summary>
        protected override unsafe GetTileDataJobDelegate getTileDataJobDelegate => GetTileDataJob;

        /// <summary>
        /// Returns null as the EntityIdTile has no Tile Animation Data
        /// </summary>
        protected override unsafe GetTileAnimationDataJobDelegate getTileAnimationDataJobDelegate => null;

        /// <summary>
        /// Initializes the EntityIdTile.
        /// </summary>
        public override void OnEnable()
        {
            base.OnEnable();
            OnValidate();
        }

        /// <summary>
        /// Retrieves the tile rendering data for the Tile.
        /// </summary>
        /// <param name="position">Position of the Tile on the Tilemap.</param>
        /// <param name="tilemap">The Tilemap the tile is present on.</param>
        /// <param name="tileData">Data to render the tile. This is filled with Tile, Tile.color and Tile.transform.</param>
        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            unsafe
            {
                UnsafeUtility.CopyPtrToStructure(UnsafeUtility.AddressOf(ref m_Data), out tileData);
            }
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(RefreshTileJobDelegate))]
        static unsafe void RefreshTileJob(int count, int3* position, void* data, ref TilemapRefreshStruct tilemapRefreshStruct)
        {
            for (var i = 0; i < count; ++i)
            {
                var pos = position + i;
                tilemapRefreshStruct.RefreshTile(*pos);
            }
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(GetTileDataJobDelegate))]
        static unsafe void GetTileDataJob(int count, int3* position, void* data, ref TilemapDataStruct tilemapDataStruct, TileData* outTileData)
        {
            for (var i = 0; i < count; ++i)
            {
                UnsafeUtility.CopyPtrToStructure(data, out *(outTileData + i));
            }
        }
    }
}
