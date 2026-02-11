using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace UnityEditor
{
    [Serializable]
    internal class TileStore : ScriptableObject
    {
        [SerializeField]
        public List<TileBase> tilesToConvert = new List<TileBase>();
    }
}
