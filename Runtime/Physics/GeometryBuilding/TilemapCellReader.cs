using UnityEngine;
using UnityEngine.Tilemaps;

namespace Unity.U2D.Physics
{
    // Reads each cell's collider type, sprite and transform from the Tilemap, with positions from a placement sampled once.
    // Call Begin at the start of every build or update.
    internal sealed class TilemapCellReader
    {
        // What one cell contributes: its collider type, and for a Sprite cell its sprite and transform.
        public readonly struct Content
        {
            public Content(Tile.ColliderType colliderType, Sprite sprite, Matrix4x4 tileMatrix)
            {
                this.colliderType = colliderType;
                this.sprite = sprite;
                this.tileMatrix = tileMatrix;
            }

            public readonly Tile.ColliderType colliderType;
            public readonly Sprite sprite;
            public readonly Matrix4x4 tileMatrix;
        }

        public void Begin(Tilemap map)
        {
            placement = TilemapCellTransforms.CellPlacement.Sample(map);
        }

        // Read what an occupied cell contributes.
        // False when the cell contributes nothing: a collider type of None, or a Sprite cell with no sprite.
        public static bool TryRead(Tilemap map, Vector3Int cell, out Content content)
        {
            content = default;

            var colliderType = map.GetColliderType(cell);
            if (colliderType == Tile.ColliderType.None)
                return false;

            // Every Grid cell is the cell shape itself, whatever the cell's transform.
            if (colliderType == Tile.ColliderType.Grid)
            {
                content = new Content(Tile.ColliderType.Grid, null, Matrix4x4.identity);
                return true;
            }

            // Sprite, and anything a future version adds.
            var sprite = map.GetSprite(cell);
            if (sprite == null)
                return false;

            content = new Content(colliderType, sprite, map.GetTransformMatrix(cell));
            return true;
        }

        // Where the map's cells sit, sampled by Begin.
        public TilemapCellTransforms.CellPlacement placement { get; private set; }
    }
}
