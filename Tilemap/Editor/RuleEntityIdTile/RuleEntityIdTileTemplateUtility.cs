using System;
using System.Collections.Generic;
using Unity.Tilemaps.Experimental;
using UnityEngine;

namespace UnityEditor.Tilemaps.Experimental
{
    /// <summary>
    /// Class containing utility methods for RuleEntityIdTile Template
    /// </summary>
    public static class RuleEntityIdTileTemplateUtility
    {
        /// <summary>
        /// Applies an RuleEntityIdTileTemplate to an RuleEntityIdTile with a source Texture2D
        /// </summary>
        /// <param name="template">RuleEntityIdTileTemplate to apply.</param>
        /// <param name="texture">Source Texture2D containing Sprites for the RuleEntityIdTileTemplate.</param>
        /// <param name="ruleEntityIdTile">RuleEntityIdTile updated with RuleEntityIdTileTemplate.</param>
        /// <param name="matchExact">Match Sprites from Source exactly with positional data from RuleEntityIdTileTemplate
        /// or match based on relative positional size.</param>
        public static void ApplyTemplateToRuleEntityIdTile(this RuleEntityIdTileTemplate template
            , Texture2D texture
            , RuleEntityIdTile ruleEntityIdTile
            , bool matchExact = true)
        {
            if (template == null || texture == null || ruleEntityIdTile == null)
                return;

            ruleEntityIdTile.defaultSprite = template.defaultSprite;
            ruleEntityIdTile.defaultGameObject = template.defaultGameObject;
            ruleEntityIdTile.defaultColliderType = template.defaultColliderType;
            if (ruleEntityIdTile.m_TilingRules == null)
                ruleEntityIdTile.m_TilingRules = new List<RuleEntityIdTile.TilingRule>(template.rules.Count);

            var assets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(texture));
            var j = 0;
            foreach (var rule in template.rules)
            {
                var copyRule = rule.tilingRule.Clone();
                for (var i = 0; i < rule.tilingRule.m_Sprites.Length; ++i)
                {
                    var spritePosition = rule.spritePositions[i];
                    foreach (var asset in assets)
                    {
                        var sprite = asset as Sprite;
                        if (sprite == null)
                            continue;

                        var match = false;
                        if (matchExact)
                        {
                            match = Mathf.Approximately(spritePosition.x, sprite.rect.x)
                                    && Mathf.Approximately(spritePosition.y, sprite.rect.y);
                        }
                        else
                        {
                            match = Mathf.Approximately(spritePosition.x / template.textureWidth, sprite.rect.x / texture.width)
                                    && Mathf.Approximately(spritePosition.y / template.textureHeight, sprite.rect.y / texture.height);
                        }
                        if (match)
                        {
                            copyRule.m_Sprites[i] = sprite;
                            break;
                        }
                    }
                }
                ruleEntityIdTile.m_TilingRules.Add(copyRule);
                j++;
            }
        }

        /// <summary>
        /// Creates an RuleEntityIdTileTemplate with the given parameters.
        /// </summary>
        /// <param name="ruleEntityIdTile">RuleEntityIdTile to save template with.</param>
        /// <returns>RuleEntityIdTileTemplate generated with the given parameters.</returns>
        public static RuleEntityIdTileTemplate CreateTemplate(RuleEntityIdTile ruleEntityIdTile)
        {
            var template = ScriptableObject.CreateInstance<RuleEntityIdTileTemplate>();
            template.defaultSprite = ruleEntityIdTile.defaultSprite;
            template.defaultGameObject = ruleEntityIdTile.defaultGameObject;
            template.defaultColliderType = ruleEntityIdTile.defaultColliderType;

            var count = ruleEntityIdTile.m_TilingRules.Count;
            template.rules = new List<RuleEntityIdTileTemplate.RuleData>(count);
            for (var j = 0; j < count; j++)
            {
                var ruleData = new RuleEntityIdTileTemplate.RuleData();
                ruleData.tilingRule = ruleEntityIdTile.m_TilingRules[j];
                int spriteCount = ruleData.tilingRule.m_Sprites.Length;
                ruleData.spritePositions = new List<Vector2>(spriteCount);
                for (var i = 0; i < spriteCount; ++i)
                {
                    var sprite = ruleData.tilingRule.m_Sprites[i];
                    var position = sprite != null ? sprite.rect.position : Vector2.zero;
                    ruleData.spritePositions.Add(position);
                }
                template.rules.Add(ruleData);
            }
            return template;
        }

        /// <summary>
        /// Creates and saves an RuleEntityIdTileTemplate with a FilePanel.
        /// </summary>
        /// <param name="ruleEntityIdTile">RuleEntityIdTile to save template with.</param>
        public static void SaveTemplateToFile(RuleEntityIdTile ruleEntityIdTile)
        {
            var template = CreateTemplate(ruleEntityIdTile);
            var path = EditorUtility.SaveFilePanelInProject("Save RuleEntityIdTile template", "New RuleEntityIdTile Template", RuleEntityIdTileTemplate.kExtension, "");
            if (!String.IsNullOrWhiteSpace(path))
                AssetDatabase.CreateAsset(template, path);
        }
    }
}
