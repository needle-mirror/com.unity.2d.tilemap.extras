using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Unity.Tilemaps.Experimental
{
    internal static class RuleEntityIdTileExtensions
    {
        /// <summary>
        ///     Returns custom fields for this RuleEntityIdTile
        /// </summary>
        /// <param name="tile">Tile to get custom fields for</param>
        /// <param name="isOverrideInstance">Whether override fields are returned</param>
        /// <returns>Custom fields for this RuleEntityIdTile</returns>
        internal static FieldInfo[] GetCustomFields(this RuleEntityIdTile tile, bool isOverrideInstance)
        {
            const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var type = tile.GetType();
            var fields = type.GetFields(bindingFlags);
            return fields
                .Where(field => !field.IsDefined(typeof(HideInInspector)))
                .Where(field => field.IsPublic || field.IsDefined(typeof(SerializeField)))
                .Where(field => typeof(RuleEntityIdTile).GetField(field.Name, bindingFlags) == null)
                .Where(field => !isOverrideInstance || !field.IsDefined(typeof(RuleEntityIdTile.DontOverride)))
                .ToArray();
        }
    }
}
