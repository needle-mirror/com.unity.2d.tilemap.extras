using UnityEngine;

namespace Unity.U2D.Physics
{
    /// <summary>
    /// Reads what the owner that produced a shape stamped onto it.
    /// </summary>
    public static class PhysicsShapeTileExtensions
    {
        /// <summary>
        /// The tile cell of the Tilemap that produced this shape.
        /// </summary>
        /// <param name="physicsShape">The shape to read the cell from.</param>
        /// <returns>The cell coordinates, in the Tilemap's cell space.</returns>
        public static Vector3Int GetTilePosition(this PhysicsShape physicsShape) => physicsShape.ownerUserData.vector3IntValue;
    }
}
