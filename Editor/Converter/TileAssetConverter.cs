using System;
using System.Collections.Generic;
using Unity.Tilemaps;
using Unity.Tilemaps.Experimental;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace UnityEditor
{
    internal static class TileAssetConverter
    {
        public static void Convert(List<TileBase> tileAssetsToConvert, Type tileType)
        {
            int count = 0;
            UnityEngine.Object newTile = null;
            try
            {
                AssetDatabase.StartAssetEditing();
                newTile = ScriptableObject.CreateInstance(tileType);
                var newSO = new SerializedObject(newTile);
                var scriptSP = newSO.FindProperty("m_Script");

                foreach (var tileAssetToConvert in tileAssetsToConvert)
                {
                    if (tileAssetToConvert == null)
                        continue;

                    if (tileAssetToConvert.GetType() == tileType)
                        continue;

                    var assetPath = AssetDatabase.GetAssetPath(tileAssetToConvert);
                    if (String.IsNullOrWhiteSpace(assetPath))
                    {
                        Debug.LogWarningFormat("{0} is not a valid asset and cannot be converted", tileAssetToConvert);
                        continue;
                    }

                    if (!AssetDatabase.IsMainAsset(tileAssetToConvert))
                    {
                        Debug.LogWarningFormat("{0} is not the main asset and cannot be converted", tileAssetToConvert);
                        continue;
                    }

                    var result = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tileAssetToConvert,
                        out var originalGuid, out long localId);
                    if (!result)
                    {
                        Debug.LogWarningFormat("Unable to get GUID for {0}", tileAssetToConvert);
                        continue;
                    }
                    var metaFilePath = AssetDatabase.GetTextMetaFilePathFromAssetPath(assetPath);
                    if (String.IsNullOrWhiteSpace(assetPath))
                    {
                        Debug.LogWarningFormat("{0} is not a valid asset and cannot be converted", tileAssetToConvert);
                        continue;
                    }
                    tileAssetToConvert.OnDisable();
                    var oldSO = new SerializedObject(tileAssetToConvert);
                    oldSO.CopyFromSerializedProperty(scriptSP);
                    oldSO.ApplyModifiedPropertiesWithoutUndo();
                    count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                if (newTile != null)
                    UnityEngine.Object.DestroyImmediate(newTile);
            }

            if (count > 0)
            {
                AssetDatabase.Refresh();

                foreach (var tileAssetToConvert in tileAssetsToConvert)
                {
                    if (tileAssetToConvert == null)
                        continue;

                    var tile = Resources.EntityIdToObject(tileAssetToConvert.GetEntityId()) as TileBase;
                    if (tile == null)
                        continue;

                    if (tile.GetType() != tileType)
                    {
                        Debug.LogErrorFormat("{0} is wrong asset type", tileAssetToConvert);
                    }
                    tile.OnEnable();
                }

                EditorUtility.RequestScriptReload();
            }
        }

        public static List<TileBase> GetAllTiles(Type tileType)
        {
            var allTiles = new List<TileBase>();
            var guids = AssetDatabase.FindAssets($"t:{tileType.Name}");
            foreach (var guid in guids)
            {
                var tile = AssetDatabase.LoadAssetByGUID<TileBase>(new GUID(guid));
                if (tile != null && AssetDatabase.IsMainAsset(tile) && tile.GetType() == tileType)
                {
                    allTiles.Add(tile);
                }
            }
            return allTiles;
        }
    }
}
