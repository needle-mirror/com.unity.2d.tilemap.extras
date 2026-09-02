using System;
using Unity.Burst;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Unity.Tilemaps
{
    /// <summary>
    ///     Generic visual tile for creating different tilesets like terrain, pipeline, random or animated tiles.
    ///     Use this for Isometric Grids.
    /// </summary>
    [BurstCompile]
    [Serializable]
    [MovedFrom(true, "Unity.Tilemaps.Experimental", "Unity.2D.Tilemap.Experimental")]
    [HelpURL(
        "https://docs.unity3d.com/Packages/com.unity.2d.tilemap.extras@latest/index.html?subfolder=/manual/EntityIdTile.html")]
    public class IsometricRuleEntityIdTile : RuleEntityIdTile
    {
        // This has no differences with the RuleEntityIdTile
    }
}
