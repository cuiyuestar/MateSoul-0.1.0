using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.RoomNavigation
{
    // A block owns its measured coverage and projected obstacle cells. The floor
    // remains the walking surface; tabletops never become walkable platforms.
    internal sealed class LiveRoomSurface
    {
        public const float CellSize = .16f;
        public readonly HashSet<Vector2Int> Occupied = new HashSet<Vector2Int>();
        public readonly HashSet<Vector2Int> Covered = new HashSet<Vector2Int>();

        public LiveRoomSurface(Mesh mesh, Matrix4x4 world, float floorY, Bounds roomBounds)
        {
            var vertices = mesh.vertices;
            var indices = mesh.triangles;
            for (var i = 0; i < indices.Length; i += 3)
            {
                var a = world.MultiplyPoint3x4(vertices[indices[i]]);
                var b = world.MultiplyPoint3x4(vertices[indices[i + 1]]);
                var c = world.MultiplyPoint3x4(vertices[indices[i + 2]]);
                var minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                var maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                var floor = Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) > .75f &&
                            minY >= floorY - .12f && maxY <= floorY + .12f;
                var obstacle = maxY > floorY + .12f &&
                               minY < floorY + RuntimeRoomNavigation.CharacterHeight;
                if (!floor && !obstacle) continue;
                var minX = Mathf.Max(roomBounds.min.x, Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
                var maxX = Mathf.Min(roomBounds.max.x, Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                var minZ = Mathf.Max(roomBounds.min.z, Mathf.Min(a.z, Mathf.Min(b.z, c.z)));
                var maxZ = Mathf.Min(roomBounds.max.z, Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
                for (var x = Mathf.FloorToInt(minX / CellSize); x <= Mathf.FloorToInt(maxX / CellSize); x++)
                    for (var z = Mathf.FloorToInt(minZ / CellSize); z <= Mathf.FloorToInt(maxZ / CellSize); z++)
                    {
                        var cell = new Vector2Int(x, z);
                        if (!IntersectsCell(a, b, c, cell)) continue;
                        Covered.Add(cell);
                        if (obstacle) Occupied.Add(cell);
                    }
            }
        }

        private static bool IntersectsCell(Vector3 a, Vector3 b, Vector3 c, Vector2Int cell)
        {
            var center = new Vector2((cell.x + .5f) * CellSize, (cell.y + .5f) * CellSize);
            var p = new Vector2(a.x, a.z) - center;
            var q = new Vector2(b.x, b.z) - center;
            var r = new Vector2(c.x, c.z) - center;
            return !Separates(q - p) && !Separates(r - q) && !Separates(p - r);

            bool Separates(Vector2 edge)
            {
                var axis = new Vector2(-edge.y, edge.x);
                var radius = CellSize * .5f * (Mathf.Abs(axis.x) + Mathf.Abs(axis.y));
                var x = Vector2.Dot(p, axis);
                var y = Vector2.Dot(q, axis);
                var z = Vector2.Dot(r, axis);
                return Mathf.Min(x, Mathf.Min(y, z)) > radius ||
                       Mathf.Max(x, Mathf.Max(y, z)) < -radius;
            }
        }
    }
}
