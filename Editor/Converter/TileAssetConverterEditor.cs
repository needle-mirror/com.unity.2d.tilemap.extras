using System;
using System.Collections.Generic;
using Unity.Tilemaps.Experimental;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace UnityEditor
{
    internal class TileAssetConverterEditor : EditorWindow
    {
        static readonly List<Type> oldTypes = new List<Type>()
        {
            typeof(Tile), typeof(AnimatedTile), typeof(AutoTile), typeof(RuleTile)
        };
        static readonly List<Type> newTypes = new List<Type>()
        {
            typeof(EntityIdTile), typeof(AnimatedEntityIdTile), typeof(AutoEntityIdTile), typeof(RuleEntityIdTile)
        };

        private List<Type> tileTypes;
        private List<String> tileTypeNames;

        private ListView convertingObjects;
        private DropdownField convertTypeDropdown;
        private DropdownField findTypeDropdown;

        [SerializeField]
        private TileStore tileStore;
        private SerializedObject serializedTileStore;

        [MenuItem("Window/2D/Tile Asset Converter", false, 5)]
        private static void OpenTilemapPalette()
        {
            var w = GetWindow<TileAssetConverterEditor>("Tile Asset Converter");
            w.Show();
        }

        private void CreateTileStore()
        {
            if (tileStore != null)
                return;
            tileStore = CreateInstance<TileStore>();
            tileStore.hideFlags = HideFlags.DontSave;
            serializedTileStore = new SerializedObject(tileStore);
        }

        private void OnEnable()
        {
            var tileTypeCollection = TypeCache.GetTypesDerivedFrom<TileBase>();
            tileTypes = new List<Type>();
            foreach (var tileType in tileTypeCollection)
            {
                if (!tileType.IsGenericTypeDefinition
                    && !tileType.IsAbstract)
                    tileTypes.Add(tileType);
            }
            tileTypes.Sort((a, b) => a.Name.CompareTo(b.Name));
            tileTypeNames = new List<string>(tileTypes.Count);
            foreach (var tileType in tileTypes)
            {
                tileTypeNames.Add(tileType.FullName);
            }
            CreateTileStore();
            if (convertingObjects != null)
            {
                convertingObjects.Bind(serializedTileStore);
            }
        }

        private void OnDisable()
        {
            DestroyImmediate(tileStore);
            tileStore = null;
        }

        public void CreateGUI()
        {
            var ve = new VisualElement();
            ve.style.flexDirection = FlexDirection.Column;

            var he1 = new VisualElement();
            he1.style.flexDirection = FlexDirection.Row;
            he1.style.minHeight = 20;
            he1.style.flexWrap = Wrap.Wrap;
            he1.style.flexGrow = 0.15f;
            he1.style.flexShrink = 0f;

            findTypeDropdown = new DropdownField("Find Tiles", tileTypeNames, "UnityEngine.Tilemaps.Tile");
            findTypeDropdown.style.flexGrow = 1;
            findTypeDropdown.style.flexWrap = Wrap.Wrap;
            he1.Add(findTypeDropdown);

            var findButton = new Button(FindTiles);
            findButton.text = "Find";
            findButton.style.minWidth = 100;
            he1.Add(findButton);

            ve.Add(he1);

            convertingObjects = new ListView()
            {
                reorderable = false,
                showBorder = true,
                showFoldoutHeader = true,
                showBoundCollectionSize = true,
                headerTitle = "Tiles to Convert",
                showAddRemoveFooter = true,
                bindingPath = "tilesToConvert"
            };
            convertingObjects.Bind(serializedTileStore);
            convertingObjects.dragAndDropUpdate += ConvertingObjectsOnDragAndDropUpdate;
            convertingObjects.handleDrop += ConvertingObjectsOnHandleDrop;
            convertingObjects.style.flexGrow = 0.8f;
            ve.Add(convertingObjects);

            var foldout = convertingObjects.Q<Foldout>();
            foldout.value = true;

            var he2 = new VisualElement();
            he2.style.flexDirection = FlexDirection.Row;
            he2.style.minHeight = 20;
            he2.style.flexWrap = Wrap.Wrap;
            he2.style.flexGrow = 0.15f;
            he2.style.flexShrink = 0f;

            convertTypeDropdown = new DropdownField("Convert to Tile", tileTypeNames, "UnityEngine.Tilemaps.Tile");
            convertTypeDropdown.style.flexGrow = 1;
            convertTypeDropdown.style.flexWrap = Wrap.Wrap;
            he2.Add(convertTypeDropdown);

            var convertButton = new Button(ConvertTiles);
            convertButton.text = "Convert";
            convertButton.style.minWidth = 100;
            he2.Add(convertButton);

            ve.Add(he2);

            var autoConvertTooltip = "Auto Convert Unity Tiles:\n";
            for (var i = 0; i < oldTypes.Count; i++)
            {
                autoConvertTooltip += $"{oldTypes[i].FullName} to {newTypes[i].FullName}\n";
            }

            var autoConvertButton = new Button(AutoConvertTiles);
            autoConvertButton.text = "Convert Unity Tiles";
            autoConvertButton.tooltip = autoConvertTooltip;
            ve.Add(autoConvertButton);

            rootVisualElement.Add(ve);
        }

        private DragVisualMode ConvertingObjectsOnHandleDrop(HandleDragAndDropArgs arg)
        {
            CreateTileStore();
            Undo.RecordObject(tileStore, "Add Tiles");
            var i = 0;
            var count = tileStore.tilesToConvert.Count;
            foreach (var entityId in arg.dragAndDropData.entityIds)
            {
                var convertTile = Resources.EntityIdToObject(entityId) as TileBase;
                if (convertTile != null && !tileStore.tilesToConvert.Contains(convertTile))
                    tileStore.tilesToConvert.Insert(arg.insertAtIndex + i++, convertTile);
            }
            serializedTileStore.Update();
            return (tileStore.tilesToConvert.Count > count) ? DragVisualMode.Copy : DragVisualMode.None;
        }

        private DragVisualMode ConvertingObjectsOnDragAndDropUpdate(HandleDragAndDropArgs arg)
        {
            foreach (var entityId in arg.dragAndDropData.entityIds)
            {
                var convertTile = Resources.EntityIdToObject(entityId) as TileBase;
                if (convertTile == null)
                    return DragVisualMode.Rejected;
            }
            return DragVisualMode.Copy;
        }

        private void FindTiles()
        {
            var tileType = tileTypes[findTypeDropdown.index];
            var tiles = TileAssetConverter.GetAllTiles(tileType);

            CreateTileStore();
            Undo.RecordObject(tileStore, "Find Tiles");
            foreach (var tile in tiles)
            {
                if (!tileStore.tilesToConvert.Contains(tile))
                    tileStore.tilesToConvert.Add(tile);
            }
            serializedTileStore.Update();
        }

        private void ConvertTiles()
        {
            if (tileStore.tilesToConvert.Count == 0)
                return;

            var tileType = tileTypes[convertTypeDropdown.index];
            TileAssetConverter.Convert(tileStore.tilesToConvert, tileType);
            tileStore.tilesToConvert.Clear();
        }

        private void AutoConvertTiles()
        {
            for (var i = 0; i < oldTypes.Count; i++)
            {
                var tiles = TileAssetConverter.GetAllTiles(oldTypes[i]);
                TileAssetConverter.Convert(tiles, newTypes[i]);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
