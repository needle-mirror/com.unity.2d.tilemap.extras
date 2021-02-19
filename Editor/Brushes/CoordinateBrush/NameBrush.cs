using UnityEngine;
using UnityEngine.Tilemaps;

namespace UnityEditor.Tilemaps
{
    [CustomGridBrush(true, false, false, "Name Brush")]
    public class NameBrush : GridBrush
    {
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(NameBrush))]
    public class NameBrushEditor : GridBrushEditor
    {
        public override void OnPaintSceneGUI(GridLayout gridLayout, GameObject brushTarget, BoundsInt position, GridBrushBase.Tool tool, bool executing)
        {
            base.OnPaintSceneGUI(gridLayout, brushTarget, position, tool, executing);

            var tilemap = brushTarget.GetComponent<Tilemap>();
            if (tilemap != null)
            {
                if (tilemap.HasTile(position.position))
                {
                    var sprite = tilemap.GetSprite(position.position);
                    var labelText = sprite ? sprite.name : "No Sprite";
                    Handles.Label(gridLayout.CellToWorld(new Vector3Int(position.x, position.y, position.z)), labelText);                    
                }
            }
        }
    }
#endif
}

