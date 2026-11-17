using System;

using UnityEngine;

namespace Unity.U2D.Physics
{
    // A cell as a native hash key. Vector3Int's hash collides heavily on grids (a 1000 by 1000 map gets only 16,384 distinct values),
    // so this mixes all three coordinates into every bit.
    internal readonly struct TilemapCellKey : IEquatable<TilemapCellKey>
    {
        public TilemapCellKey(Vector3Int cell) => m_Cell = cell;

        public static implicit operator TilemapCellKey(Vector3Int cell) => new(cell);

        public static implicit operator Vector3Int(TilemapCellKey key) => key.m_Cell;

        public bool Equals(TilemapCellKey other) => m_Cell == other.m_Cell;

        public override bool Equals(object obj) => obj is TilemapCellKey other && Equals(other);

        // Each coordinate is spread by its own odd multiplier before mixing, so neighboring cells differ in every bit of the result.
        public override int GetHashCode() => Mix((uint)m_Cell.x * 0x9E3779B1u + (uint)m_Cell.y * 0x85EBCA77u + (uint)m_Cell.z * 0xC2B2AE3Du);

        public override string ToString() => m_Cell.ToString();

        // Spread a hash so every bit of the input reaches every bit of the result, since a native container picks a bucket from the low bits alone.
        public static int Mix(uint hash)
        {
            hash ^= hash >> 16;
            hash *= 0x85EBCA6Bu;
            hash ^= hash >> 13;
            hash *= 0xC2B2AE35u;
            hash ^= hash >> 16;

            return (int)hash;
        }

        public Vector3Int cell => m_Cell;

        #region Internal

        private readonly Vector3Int m_Cell;

        #endregion
    }
}
