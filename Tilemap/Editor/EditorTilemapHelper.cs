using Unity.Tilemaps.Experimental;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace UnityEditor.Tilemaps.Experimental
{
    internal class EditorTilemapHelper : TilemapHelper
    {
        [InitializeOnLoadMethod]
        private static void InitializeTilemapHelper()
        {
            ITilemap.RegisterCreateITilemapFunc(CreateInstance, 3);
        }

        private static ITilemap CreateInstance(Tilemap tilemap)
        {
            return new EditorTilemapHelper(tilemap);
        }

        public EditorTilemapHelper(Tilemap tilemap) : base(tilemap)
        {
        }

        public override TileBase GetTile(Vector3Int position)
        {
            return m_Tilemap.GetAnyTile(position);
        }

        public override T GetTile<T>(Vector3Int position)
        {
            return m_Tilemap.GetAnyTile<T>(position);
        }

        public override EntityId GetTileEntityId(Vector3Int position)
        {
            return m_Tilemap.GetAnyTileEntityId(position);
        }
    }
}
