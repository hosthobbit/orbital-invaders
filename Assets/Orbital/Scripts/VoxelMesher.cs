using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital
{
    public struct Voxel
    {
        public Vector3Int P;
        /// <summary>rgb = colour, a = emission strength (0..1).</summary>
        public Color C;
        public Voxel(int x, int y, int z, Color c) { P = new Vector3Int(x, y, z); C = c; }
    }

    /// <summary>Builds meshes of separated, slightly bevelled cubes from voxel lists.</summary>
    public static class VoxelMesher
    {
        static readonly Vector3[] FaceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        /// <summary>
        /// size = voxel pitch, fill = cube size as a fraction of the pitch (gaps read as "pixels").
        /// centre = the voxel-space point placed at the mesh origin.
        /// Faces touching a neighbour are skipped when fill >= 0.999.
        /// </summary>
        public static Mesh Build(IList<Voxel> voxels, float size, float fill, Vector3 centre, Mesh into = null)
        {
            var occupied = new HashSet<Vector3Int>();
            foreach (var v in voxels) occupied.Add(v.P);
            bool cull = fill >= 0.999f;

            var verts = new List<Vector3>(voxels.Count * 24);
            var norms = new List<Vector3>(voxels.Count * 24);
            var cols = new List<Color>(voxels.Count * 24);
            var tris = new List<int>(voxels.Count * 36);
            float h = size * fill * 0.5f;
            foreach (var v in voxels)
            {
                Vector3 c = ((Vector3)v.P - centre) * size;
                for (int f = 0; f < 6; f++)
                {
                    Vector3 n = FaceNormals[f];
                    if (cull && occupied.Contains(v.P + Vector3Int.RoundToInt(n))) continue;
                    // two tangents for this face
                    Vector3 t1 = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                    Vector3 t2 = Vector3.Cross(n, t1);
                    int b = verts.Count;
                    Vector3 fc = c + n * h;
                    verts.Add(fc + (-t1 - t2) * h);
                    verts.Add(fc + (-t1 + t2) * h);
                    verts.Add(fc + (t1 + t2) * h);
                    verts.Add(fc + (t1 - t2) * h);
                    for (int k = 0; k < 4; k++) { norms.Add(n); cols.Add(v.C); }
                    // orient so the face points along n
                    Vector3 fn = Vector3.Cross(verts[b + 1] - verts[b], verts[b + 2] - verts[b]);
                    if (Vector3.Dot(fn, n) >= 0) { tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3); }
                    else { tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2); }
                }
            }
            var mesh = into ?? new Mesh();
            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Turns a text bitmap ('#' = on) into voxels, extruded `depth` deep. Row 0 is the top.</summary>
        public static List<Voxel> FromRows(string[] rows, int depth, Color front, Color back, float frontGlow, float backGlow)
        {
            var list = new List<Voxel>();
            int h = rows.Length;
            for (int r = 0; r < h; r++)
            for (int x = 0; x < rows[r].Length; x++)
            {
                if (rows[r][x] != '#') continue;
                for (int z = 0; z < depth; z++)
                {
                    Color c = z == 0 ? front : back;
                    c.a = z == 0 ? frontGlow : backGlow;
                    list.Add(new Voxel(x, h - 1 - r, z, c));
                }
            }
            return list;
        }

        public static Vector3 Centre(IList<Voxel> voxels)
        {
            if (voxels.Count == 0) return Vector3.zero;
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            foreach (var v in voxels) { min = Vector3.Min(min, v.P); max = Vector3.Max(max, v.P); }
            return (min + max) * 0.5f;
        }
    }
}
