using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace Unity.U2D.Physics.Editor
{
    // Draws a Physics Area Tilemap's build settings, showing only the settings in use.
    // Split Z Layers only applies to the Segments output, and the Polygons output only reads Vertex Tolerance while its cells are clipped, so each is hidden otherwise.
    // An info note under Cell Clipping gives the reason when clipping is on but not in use.
    // The Grid can change without any setting changing, so the note and the tolerance are also checked again whenever the area's Tilemap reports a change, which a Grid change sends.
    [CustomPropertyDrawer(typeof(PhysicsAreaTilemap.BuildSettings))]
    internal sealed class PhysicsAreaTilemapBuildSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var outputType = property.FindPropertyRelative(nameof(PhysicsAreaTilemap.BuildSettings.m_OutputType));
            var cellClipping = property.FindPropertyRelative(nameof(PhysicsAreaTilemap.BuildSettings.m_CellClipping));
            var note = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            var splitZLayers = new PropertyField(property.FindPropertyRelative(nameof(PhysicsAreaTilemap.BuildSettings.m_SplitZLayers)));
            var vertexTolerance = new PropertyField(property.FindPropertyRelative(nameof(PhysicsAreaTilemap.BuildSettings.m_VertexTolerance)));

            root.Add(new PropertyField(outputType));
            root.Add(new PropertyField(cellClipping));
            root.Add(note);
            root.Add(splitZLayers);
            root.Add(vertexTolerance);

            var area = property.serializedObject.targetObject as PhysicsAreaTilemap;
            Refresh();

            root.TrackPropertyValue(outputType, _ => Refresh());
            root.TrackPropertyValue(cellClipping, _ => Refresh());
            root.RegisterCallback<AttachToPanelEvent>(_ => Tilemap.tilemapChanged += OnTilemapChanged);
            root.RegisterCallback<DetachFromPanelEvent>(_ => Tilemap.tilemapChanged -= OnTilemapChanged);

            return root;

            #region Locals

            void Refresh()
            {
                var reason = area == null ? null : area.cellClippingInactiveReason;

                note.text = reason ?? string.Empty;
                note.style.display = reason == null ? DisplayStyle.None : DisplayStyle.Flex;

                // A selection mixing both output types shows every setting either of them uses.
                var usesSegments = outputType.hasMultipleDifferentValues || outputType.enumValueIndex == (int)PhysicsAreaTilemap.OutputType.Segments;
                var clipsCells = area != null && area.isCellClippingActive;

                splitZLayers.style.display = usesSegments ? DisplayStyle.Flex : DisplayStyle.None;
                vertexTolerance.style.display = usesSegments || clipsCells ? DisplayStyle.Flex : DisplayStyle.None;
            }

            void OnTilemapChanged(Tilemap changed, TilemapChangeReason reason)
            {
                if (area != null && changed != null && changed.gameObject == area.gameObject)
                    Refresh();
            }

            #endregion
        }
    }
}
