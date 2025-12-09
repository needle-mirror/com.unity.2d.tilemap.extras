using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.Tilemaps.Experimental
{
    /// <summary>
    /// Extension helper functions for converting Vector3Int to int3 and vice versa
    /// </summary>
    public static class Vector3IntExtensions
    {
        /// <summary>
        /// Converts an int3 to Vector3Int
        /// </summary>
        /// <param name="v">int3 to convert</param>
        /// <returns>Converted Vector3Int</returns>
        public static Vector3Int ToVector3Int(this int3 v)
        {
            return UnsafeUtility.As<int3, Vector3Int>(ref v);
        }

        /// <summary>
        /// Converts a Vector3Int to int3
        /// </summary>
        /// <param name="v">Vector3Int to convert</param>
        /// <returns>Converted int3</returns>
        public static int3 ToInt3(this Vector3Int v)
        {
            return UnsafeUtility.As<Vector3Int, int3>(ref v);
        }
    }
}
