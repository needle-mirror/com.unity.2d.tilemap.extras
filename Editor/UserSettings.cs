using UnityEngine.UIElements;

namespace UnityEditor.Tilemaps
{
    internal class UserSettings : SettingsProvider
    {
        UserSettings() : base("Project/2D/Tilemap Extras", SettingsScope.Project)
        {
        }

        [SettingsProvider]
        static SettingsProvider CreateSettingsProvider()
        {
            return new UserSettings();
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            TileCreateMenuSettings.SetupUI(rootElement);
        }
    }
}
