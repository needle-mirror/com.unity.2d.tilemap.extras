using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace UnityEditor.Tilemaps
{
    /// <summary>
    /// Editor for AutoTile.
    /// </summary>
    [CustomEditor(typeof(AutoTile))]
    public class AutoTileEditor : Editor
    {
        private AutoTile autoTile => target as AutoTile;
        private AutoTileEditorElement m_EditorElement;
        private Hash128 m_AutoTileHash;
        private Dictionary<Texture2D, Hash128> m_TextureHashes = new Dictionary<Texture2D, Hash128>();

        /// <summary>
        /// Creates a VisualElement for AutoTile Editor.
        /// </summary>
        /// <returns>A VisualElement for AutoTile Editor.</returns>
        public override VisualElement CreateInspectorGUI()
        {
            m_EditorElement = new AutoTileEditorElement();
            m_EditorElement.Bind(serializedObject);
            m_EditorElement.autoTile = autoTile;
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
            if (autoTile == null)
                return;

            m_AutoTileHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(autoTile));
            m_TextureHashes.Clear();
            foreach (var texture in autoTile.m_TextureList)
            {
                if (texture != null)
                    m_TextureHashes[texture] = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(texture));
            }
        }

        private void OnProjectChanged()
        {
            if (autoTile == null || m_EditorElement == null)
                return;

            if (!HaveAssetsChanged())
                return;

            serializedObject.Update();
            m_EditorElement.autoTile = autoTile;
            CacheAssetHashes();
            Repaint();
        }

        /// <summary>
        /// Renders a static preview for the AutoTile asset using its default Sprite,
        /// or any available Sprite if no default is set.
        /// </summary>
        /// <param name="assetPath">The path of the asset.</param>
        /// <param name="subAssets">Sub-assets of the asset.</param>
        /// <param name="width">Width of the preview.</param>
        /// <param name="height">Height of the preview.</param>
        /// <returns>A Texture2D preview, or null if no Sprite is available.</returns>
        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            if (autoTile == null)
                return base.RenderStaticPreview(assetPath, subAssets, width, height);

            var sprite = autoTile.m_DefaultSprite;
            if (sprite == null)
            {
                foreach (var pair in autoTile.m_AutoTileDictionary)
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
            var currentHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(autoTile));
            if (currentHash != m_AutoTileHash)
                return true;

            foreach (var texture in autoTile.m_TextureList)
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
