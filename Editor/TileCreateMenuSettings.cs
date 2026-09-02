using System;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.Tilemaps
{
    /// <summary>
    /// Which Tiles are listed in the Assets/Create/2D/Tiles menu.
    /// </summary>
    internal enum TileCreateMenuFilter
    {
        /// <summary>Lists every Tile.</summary>
        All = 0,

        /// <summary>Lists only the Entity Id Tiles.</summary>
        EntityIdOnly = 1,

        /// <summary>Lists only the Tiles which are not Entity Id Tiles.</summary>
        NonEntityIdOnly = 2
    }

    /// <summary>
    /// The Tile creation menu items are compiled in or out with these Scripting Define Symbols, as
    /// there is no supported way to add and remove menu items while the Editor is running. Changing
    /// the setting recompiles the scripts, and the menu is rebuilt once that finishes.
    /// </summary>
    /// <remarks>
    /// These symbols are necessarily defined for every assembly in the project, not just for this
    /// one. Player settings is the only place a define can be written to while the Editor is running,
    /// and it has no per assembly scope. The two mechanisms which do have one cannot be used here:
    /// a csc.rsp beside the assembly definition, and versionDefines within it, both live inside this
    /// package, which is immutable once it has been installed. The TILEMAP_EXTRAS_ prefix is what
    /// keeps the symbols from colliding with anything else in the project.
    /// </remarks>
    internal static class TileCreateMenuSettings
    {
        /// <summary>Leaves out the Tiles which are not Entity Id Tiles.</summary>
        public const string hideTileMenuDefine = "TILEMAP_EXTRAS_HIDE_TILE_MENU";

        /// <summary>Leaves out the Entity Id Tiles.</summary>
        public const string hideEntityIdTileMenuDefine = "TILEMAP_EXTRAS_HIDE_ENTITY_ID_TILE_MENU";

        private const string k_SettingsTitle = "Tilemap Extras";

        private static readonly GUIContent k_FilterLabel = EditorGUIUtility.TrTextContent("Tile Creation Menu",
            "Choose which Tiles are listed in the Assets/Create/2D/Tiles menu. This is stored as a Scripting"
            + " Define Symbol, so it applies to everyone who opens this project, and the menu is updated once"
            + " the scripts have finished recompiling.");

        // Ordered so that each index matches the TileCreateMenuFilter with the same value.
        private static readonly List<string> k_FilterChoices = new List<string>
        {
            "Show both",
            "Show only Entity Id Tiles",
            "Show only non-Entity Id Tiles"
        };

        private static NamedBuildTarget activeBuildTarget =>
            NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);

        /// <summary>
        /// The Scripting Define Symbols are the only source of truth, so that the setting cannot
        /// disagree with the menu items which were actually compiled.
        /// </summary>
        public static TileCreateMenuFilter filter
        {
            get => GetFilter(activeBuildTarget);
            set => SetFilter(activeBuildTarget, value);
        }

        public static TileCreateMenuFilter GetFilter(NamedBuildTarget buildTarget)
        {
            PlayerSettings.GetScriptingDefineSymbols(buildTarget, out var defines);

            var hideTiles = Contains(defines, hideTileMenuDefine);
            var hideEntityIdTiles = Contains(defines, hideEntityIdTileMenuDefine);

            // Both being set leaves no Tiles in the menu at all, which is not reachable through the
            // UI but could have been set by hand. Report it as All, so that picking anything in the
            // UI writes over both of them.
            if (hideTiles && !hideEntityIdTiles)
                return TileCreateMenuFilter.EntityIdOnly;
            if (hideEntityIdTiles && !hideTiles)
                return TileCreateMenuFilter.NonEntityIdOnly;
            return TileCreateMenuFilter.All;
        }

        public static void SetFilter(NamedBuildTarget buildTarget, TileCreateMenuFilter filter)
        {
            PlayerSettings.GetScriptingDefineSymbols(buildTarget, out var defines);

            var hideTiles = filter == TileCreateMenuFilter.EntityIdOnly;
            var hideEntityIdTiles = filter == TileCreateMenuFilter.NonEntityIdOnly;

            if (Contains(defines, hideTileMenuDefine) == hideTiles
                && Contains(defines, hideEntityIdTileMenuDefine) == hideEntityIdTiles)
                return;

            // Rebuilt rather than edited in place, so that a symbol which was added more than once
            // by hand does not leave a copy behind.
            var updated = new List<string>(defines.Length + 1);
            foreach (var define in defines)
            {
                if (define != hideTileMenuDefine && define != hideEntityIdTileMenuDefine)
                    updated.Add(define);
            }

            if (hideTiles)
                updated.Add(hideTileMenuDefine);
            if (hideEntityIdTiles)
                updated.Add(hideEntityIdTileMenuDefine);

            // This recompiles the scripts, which is what adds or removes the menu items.
            PlayerSettings.SetScriptingDefineSymbols(buildTarget, updated.ToArray());
        }

        private static bool Contains(string[] defines, string define)
        {
            return Array.IndexOf(defines, define) >= 0;
        }

        public static void SetupUI(VisualElement rootElement)
        {
            var container = new VisualElement();
            container.style.paddingLeft = 5;
            rootElement.Add(container);

            var header = new Label(k_SettingsTitle);
            header.style.paddingLeft = 5;
            header.style.paddingBottom = 10;
            header.style.fontSize = 20;
            header.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
            container.Add(header);

            var dropdown = new DropdownField(k_FilterLabel.text, k_FilterChoices, (int)filter)
            {
                tooltip = k_FilterLabel.tooltip
            };
            dropdown.RegisterValueChangedCallback(x =>
            {
                var index = k_FilterChoices.IndexOf(x.newValue);
                if (index >= 0)
                    filter = (TileCreateMenuFilter)index;
            });
            container.Add(dropdown);
        }
    }

    /// <summary>
    /// Scripting Define Symbols are set for one build target at a time, and Editor scripts are
    /// compiled with those of the active one. Carry the setting over so that switching platform does
    /// not bring the hidden menu items back.
    /// </summary>
    internal class TileCreateMenuBuildTargetTracker : IActiveBuildTargetChanged
    {
        public int callbackOrder => 0;

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
        {
            // Leave the setting alone rather than carrying it over from or to a build target which
            // has no Scripting Define Symbols of its own.
            if (!TryGetNamedBuildTarget(previousTarget, out var previous)
                || !TryGetNamedBuildTarget(newTarget, out var current))
                return;

            TileCreateMenuSettings.SetFilter(current, TileCreateMenuSettings.GetFilter(previous));
        }

        /// <summary>
        /// NamedBuildTarget.FromBuildTargetGroup rejects some build target groups. This runs as part
        /// of the Editor changing build target, where throwing would be reported as an unrelated
        /// failure, so treat a build target which cannot be converted as one to skip.
        /// </summary>
        private static bool TryGetNamedBuildTarget(BuildTarget buildTarget, out NamedBuildTarget namedBuildTarget)
        {
            namedBuildTarget = default;

            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
            if (buildTargetGroup == BuildTargetGroup.Unknown)
                return false;

            try
            {
                namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
