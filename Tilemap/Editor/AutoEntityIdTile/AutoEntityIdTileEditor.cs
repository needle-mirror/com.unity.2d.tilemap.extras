using Unity.Tilemaps.Experimental;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace UnityEditor.Tilemaps.Experimental
{
    /// <summary>
    /// Editor for AutoTile.
    /// </summary>
    [CustomEditor(typeof(AutoEntityIdTile))]
    public class AutoEntityIdTileEditor : Editor
    {
        private AutoEntityIdTile autoEntityIdTile => target as AutoEntityIdTile;

        /// <summary>
        /// Creates a VisualElement for AutoTile Editor.
        /// </summary>
        /// <returns>A VisualElement for AutoTile Editor.</returns>
        public override VisualElement CreateInspectorGUI()
        {
            var autoTileEditorElement = new AutoEntityIdTileEditorElement();
            autoTileEditorElement.Bind(serializedObject);
            autoTileEditorElement.autoEntityIdTile = autoEntityIdTile;
            return autoTileEditorElement;
        }
    }
}
