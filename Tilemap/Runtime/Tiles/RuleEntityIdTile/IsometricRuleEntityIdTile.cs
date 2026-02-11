using System;
using Unity.Burst;
using UnityEngine;

namespace Unity.Tilemaps.Experimental
{
    /// <summary>
    ///     Generic visual tile for creating different tilesets like terrain, pipeline, random or animated tiles.
    /// </summary>
    [BurstCompile]
    [Serializable]
    [HelpURL(
        "https://docs.unity3d.com/Packages/com.unity.2d.tilemap.extras@latest/index.html?subfolder=/manual/RuleEntityIdTile.html")]
    public class IsometricRuleEntityIdTile : RuleEntityIdTile
    {
        // This has no differences with the RuleEntityIdTile
    }
}
