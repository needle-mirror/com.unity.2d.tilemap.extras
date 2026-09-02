using System.Collections.Generic;
using Unity.Tilemaps;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UIElements;

namespace UnityEditor.Tilemaps
{
    /// <summary>
    /// Editor for AutoEntityIdTile.
    /// </summary>
    [CustomEditor(typeof(AutoEntityIdTile))]
    [MovedFrom(true, "UnityEditor.Tilemaps.Experimental", "Unity.2D.Tilemap.Editor.Experimental")]
    public class AutoEntityIdTileEditor : Editor
    {
        private AutoEntityIdTile autoEntityIdTile => target as AutoEntityIdTile;
        private AutoEntityIdTileEditorElement m_EditorElement;
        private Hash128 m_AutoEntityIdTileHash;
        private Dictionary<Texture2D, Hash128> m_TextureHashes = new Dictionary<Texture2D, Hash128>();

        /// <summary>
        /// Creates a VisualElement for AutoEntityIdTile Editor.
        /// </summary>
        /// <returns>A VisualElement for AutoEntityIdTile Editor.</returns>
        public override VisualElement CreateInspectorGUI()
        {
            m_EditorElement = new AutoEntityIdTileEditorElement();
            m_EditorElement.Bind(serializedObject);
            m_EditorElement.autoEntityIdTile = autoEntityIdTile;
            return m_EditorElement;
        }

        private void OnEnable()
        {
            CacheAssetHashes();
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        private void CacheAssetHashes()
        {
            if (autoEntityIdTile == null)
                return;

            m_AutoEntityIdTileHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(autoEntityIdTile));
            m_TextureHashes.Clear();
            foreach (var texture in autoEntityIdTile.m_TextureList)
            {
                if (texture != null)
                    m_TextureHashes[texture] = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(texture));
            }
        }

        private void OnProjectChanged()
        {
            if (autoEntityIdTile == null || m_EditorElement == null)
                return;

            if (!HaveAssetsChanged())
                return;

            serializedObject.Update();
            m_EditorElement.autoEntityIdTile = autoEntityIdTile;
            CacheAssetHashes();
            Repaint();
        }

        /// <summary>
        /// Renders a static preview for the AutoEntityIdTile asset using its default Sprite,
        /// or any available Sprite if no default is set.
        /// </summary>
        /// <param name="assetPath">The path of the asset.</param>
        /// <param name="subAssets">Sub-assets of the asset.</param>
        /// <param name="width">Width of the preview.</param>
        /// <param name="height">Height of the preview.</param>
        /// <returns>A Texture2D preview, or null if no Sprite is available.</returns>
        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            if (autoEntityIdTile == null)
                return base.RenderStaticPreview(assetPath, subAssets, width, height);

            var sprite = autoEntityIdTile.m_DefaultSprite;
            if (sprite == null)
            {
                foreach (var pair in autoEntityIdTile.m_AutoTileDictionary)
                {
                    var spriteList = pair.Value.spriteList;
                    if (spriteList == null)
                        continue;
                    foreach (var s in spriteList)
                    {
                        if (s != null)
                        {
                            sprite = s;
                            break;
                        }
                    }
                    if (sprite != null)
                        break;
                }
            }

            if (sprite == null)
                return base.RenderStaticPreview(assetPath, subAssets, width, height);

            var preview = AssetPreview.GetAssetPreview(sprite);
            if (preview == null)
                return base.RenderStaticPreview(assetPath, subAssets, width, height);

            var tex = new Texture2D(width, height);
            EditorUtility.CopySerialized(preview, tex);
            return tex;
        }

        private bool HaveAssetsChanged()
        {
            var currentHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(autoEntityIdTile));
            if (currentHash != m_AutoEntityIdTileHash)
                return true;

            foreach (var texture in autoEntityIdTile.m_TextureList)
            {
                if (texture == null)
                    continue;

                var textureHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(texture));
                if (!m_TextureHashes.TryGetValue(texture, out var cachedHash) || textureHash != cachedHash)
                    return true;
            }

            return false;
        }
    }
}
