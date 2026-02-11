using Unity.Tilemaps.Experimental;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace UnityEditor.Tilemaps.Experimental
{
    /// <summary>
    /// Utility class for creating Tiles
    /// </summary>
    public class EntityIdTileUtility
    {
        /// <summary>Creates an EntityIdTile with defaults based on the EntityIdTile preset</summary>
        /// <returns>An EntityIdTile with defaults based on the EntityIdTile preset</returns>
        public static EntityIdTile CreateDefaultEntityIdTile()
        {
            return ObjectFactory.CreateInstance<EntityIdTile>();
        }

        /// <summary>Creates a EntityIdTile with defaults based on the EntityIdTile preset and a Sprite set</summary>
        /// <param name="sprite">A Sprite to set the Tile with</param>
        /// <returns>An EntityIdTile with defaults based on the EntityIdTile preset and a Sprite set</returns>
        [CreateTileFromPalette]
        public static TileBase DefaultEntityIdTile(Sprite sprite)
        {
            EntityIdTile tile = CreateDefaultEntityIdTile();
            tile.name = sprite.name;
            tile.sprite = sprite;
            tile.color = Color.white;
            return tile;
        }
    }
}
