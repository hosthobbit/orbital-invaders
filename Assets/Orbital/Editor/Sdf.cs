using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital.EditorTools
{
    /// <summary>Signed distance helpers used to sculpt every mesh in the game.</summary>
    public static class Sdf
    {
        public static float Sphere(Vector3 p, Vector3 c, float r) => (p - c).magnitude - r;

        public static float Capsule(Vector3 p, Vector3 a, Vector3 b, float r)
        {
            Vector3 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector3.Dot(pa, ba) / Vector3.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        /// <summary>Capsule with radius blending linearly from ra at a to rb at b.</summary>
        public static float RoundCone(Vector3 p, Vector3 a, Vector3 b, float ra, float rb)
        {
            Vector3 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector3.Dot(pa, ba) / Vector3.Dot(ba, ba));
            return (pa - ba * h).magnitude - Mathf.Lerp(ra, rb, h);
        }

        public static float Box(Vector3 p, Vector3 c, Vector3 half, float round = 0f)
        {
            Vector3 q = Abs(p - c) - half + Vector3.one * round;
            return Max(q, 0f).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - round;
        }

        public static float Torus(Vector3 p, Vector3 c, float major, float minor)
        {
            Vector3 d = p - c;
            float qx = new Vector2(d.x, d.z).magnitude - major;
            return new Vector2(qx, d.y).magnitude - minor;
        }

        public static float Cylinder(Vector3 p, Vector3 c, float r, float halfH)
        {
            Vector3 d = p - c;
            Vector2 q = new Vector2(new Vector2(d.x, d.z).magnitude - r, Mathf.Abs(d.y) - halfH);
            return Mathf.Min(Mathf.Max(q.x, q.y), 0f) + Vector2.Max(q, Vector2.zero).magnitude;
        }

        public static float Ellipsoid(Vector3 p, Vector3 c, Vector3 r)
        {
            Vector3 d = p - c;
            float k0 = new Vector3(d.x / r.x, d.y / r.y, d.z / r.z).magnitude;
            float k1 = new Vector3(d.x / (r.x * r.x), d.y / (r.y * r.y), d.z / (r.z * r.z)).magnitude;
            return k1 < 1e-6f ? -Mathf.Min(r.x, Mathf.Min(r.y, r.z)) : k0 * (k0 - 1f) / k1;
        }

        public static float SMin(float a, float b, float k)
        {
            if (k <= 0f) return Mathf.Min(a, b);
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        public static float SMax(float a, float b, float k) => -SMin(-a, -b, k);
        public static float Sub(float a, float b, float k = 0f) => k <= 0f ? Mathf.Max(a, -b) : SMax(a, -b, k);

        public static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        public static Vector3 Max(Vector3 v, float m) => new Vector3(Mathf.Max(v.x, m), Mathf.Max(v.y, m), Mathf.Max(v.z, m));

        public static Vector3 RotY(Vector3 p, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector3(c * p.x - s * p.z, p.y, s * p.x + c * p.z);
        }

        public static Vector3 RotZ(Vector3 p, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector3(c * p.x - s * p.y, s * p.x + c * p.y, p.z);
        }

        public static Vector3 RotX(Vector3 p, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector3(p.x, c * p.y - s * p.z, s * p.y + c * p.z);
        }
    }

    /// <summary>Surface of revolution from a (radius, height) profile, as an exact 2D polygon distance.</summary>
    public class Lathe
    {
        readonly Vector2[] poly;
        public readonly float MinY, MaxY, MaxR;

        public Lathe(IList<Vector2> controlPoints, int samplesPerSegment = 6)
        {
            // Catmull-Rom through the control points, then close along the axis.
            var pts = new List<Vector2>();
            int n = controlPoints.Count;
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 p0 = controlPoints[Mathf.Max(i - 1, 0)], p1 = controlPoints[i], p2 = controlPoints[i + 1], p3 = controlPoints[Mathf.Min(i + 2, n - 1)];
                for (int s = 0; s < samplesPerSegment; s++)
                {
                    float t = s / (float)samplesPerSegment;
                    float t2 = t * t, t3 = t2 * t;
                    Vector2 v = 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    v.x = Mathf.Max(v.x, 0f);
                    pts.Add(v);
                }
            }
            pts.Add(controlPoints[n - 1]);
            // Drop on-axis points, then mirror across r = 0 so caps are real edges and the axis is not.
            pts.RemoveAll(p => p.x <= 1e-5f);
            var full = new List<Vector2>(pts);
            for (int i = pts.Count - 1; i >= 0; i--) full.Add(new Vector2(-pts[i].x, pts[i].y));
            poly = full.ToArray();
            MinY = float.MaxValue; MaxY = float.MinValue; MaxR = 0f;
            foreach (var p in poly) { MinY = Mathf.Min(MinY, p.y); MaxY = Mathf.Max(MaxY, p.y); MaxR = Mathf.Max(MaxR, p.x); }
        }

        public float Eval(Vector3 p)
        {
            var q = new Vector2(new Vector2(p.x, p.z).magnitude, p.y);
            float d = float.MaxValue;
            bool inside = false;
            int n = poly.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 vi = poly[i], vj = poly[j];
                Vector2 e = vj - vi, w = q - vi;
                float ee = Vector2.Dot(e, e);
                float h = ee > 1e-12f ? Mathf.Clamp01(Vector2.Dot(w, e) / ee) : 0f;
                d = Mathf.Min(d, (w - e * h).sqrMagnitude);
                if ((vi.y > q.y) != (vj.y > q.y) && q.x < (vj.x - vi.x) * (q.y - vi.y) / (vj.y - vi.y) + vi.x) inside = !inside;
            }
            return inside ? -Mathf.Sqrt(d) : Mathf.Sqrt(d);
        }
    }

    /// <summary>Naive surface nets with Newton projection and SDF-gradient normals.</summary>
    public static class SurfaceNets
    {
        public static Mesh Build(Func<Vector3, float> f, Bounds bounds, float cell,
            Func<Vector3, Color> colour = null, int projectIterations = 3, bool flatBottom = false)
        {
            Vector3 min = bounds.min - Vector3.one * cell * 2f;
            Vector3 size = bounds.size + Vector3.one * cell * 4f;
            int nx = Mathf.CeilToInt(size.x / cell) + 1;
            int ny = Mathf.CeilToInt(size.y / cell) + 1;
            int nz = Mathf.CeilToInt(size.z / cell) + 1;
            var v = new float[nx * ny * nz];
            int Idx(int x, int y, int z) => x + nx * (y + ny * z);

            System.Threading.Tasks.Parallel.For(0, nz, z =>
            {
                for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                    v[x + nx * (y + ny * z)] = f(min + new Vector3(x, y, z) * cell);
            });

            int cx = nx - 1, cy = ny - 1, cz = nz - 1;
            var cellVert = new int[cx * cy * cz];
            for (int i = 0; i < cellVert.Length; i++) cellVert[i] = -1;
            int CIdx(int x, int y, int z) => x + cx * (y + cy * z);

            var verts = new List<Vector3>();
            var corner = new float[8];
            int[,] edges = { {0,1},{2,3},{4,5},{6,7},{0,2},{1,3},{4,6},{5,7},{0,4},{1,5},{2,6},{3,7} };
            Vector3[] cornerOffset = new Vector3[8];
            for (int c = 0; c < 8; c++) cornerOffset[c] = new Vector3(c & 1, (c >> 1) & 1, (c >> 2) & 1);

            for (int z = 0; z < cz; z++)
            for (int y = 0; y < cy; y++)
            for (int x = 0; x < cx; x++)
            {
                int mask = 0;
                for (int c = 0; c < 8; c++)
                {
                    corner[c] = v[Idx(x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1))];
                    if (corner[c] < 0f) mask |= 1 << c;
                }
                if (mask == 0 || mask == 255) continue;
                Vector3 sum = Vector3.zero;
                int count = 0;
                for (int e = 0; e < 12; e++)
                {
                    int a = edges[e, 0], b = edges[e, 1];
                    bool ia = corner[a] < 0f, ib = corner[b] < 0f;
                    if (ia == ib) continue;
                    float t = corner[a] / (corner[a] - corner[b]);
                    sum += Vector3.Lerp(cornerOffset[a], cornerOffset[b], t);
                    count++;
                }
                Vector3 local = sum / count;
                cellVert[CIdx(x, y, z)] = verts.Count;
                verts.Add(min + (new Vector3(x, y, z) + local) * cell);
            }

            // Newton projection onto the true surface + gradient normals.
            var normals = new Vector3[verts.Count];
            float h = cell * 0.35f;
            System.Threading.Tasks.Parallel.For(0, verts.Count, i =>
            {
                Vector3 p = verts[i];
                for (int it = 0; it < projectIterations; it++)
                {
                    float d = f(p);
                    Vector3 g = Grad(f, p, h);
                    float gl = g.magnitude;
                    if (gl < 1e-5f) break;
                    Vector3 step = g / gl * (d / gl);
                    if (step.magnitude > cell) step = step.normalized * cell;
                    p -= step;
                }
                verts[i] = p;
                Vector3 n = Grad(f, p, h);
                normals[i] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            });

            var tris = new List<int>();
            void Quad(int a, int b, int c, int d)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0) return;
                Tri(a, b, c);
                Tri(a, c, d);
            }
            void Tri(int a, int b, int c)
            {
                if (a == b || b == c || a == c) return;
                Vector3 fn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                Vector3 avg = normals[a] + normals[b] + normals[c];
                if (Vector3.Dot(fn, avg) < 0f) { tris.Add(a); tris.Add(c); tris.Add(b); }
                else { tris.Add(a); tris.Add(b); tris.Add(c); }
            }

            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                bool inside = v[Idx(x, y, z)] < 0f;
                if (x < cx && y > 0 && z > 0 && y < ny - 1 && z < nz - 1 && inside != (v[Idx(x + 1, y, z)] < 0f))
                    Quad(cellVert[CIdx(x, y - 1, z - 1)], cellVert[CIdx(x, y, z - 1)], cellVert[CIdx(x, y, z)], cellVert[CIdx(x, y - 1, z)]);
                if (y < cy && x > 0 && z > 0 && x < nx - 1 && z < nz - 1 && inside != (v[Idx(x, y + 1, z)] < 0f))
                    Quad(cellVert[CIdx(x - 1, y, z - 1)], cellVert[CIdx(x, y, z - 1)], cellVert[CIdx(x, y, z)], cellVert[CIdx(x - 1, y, z)]);
                if (z < cz && x > 0 && y > 0 && x < nx - 1 && y < ny - 1 && inside != (v[Idx(x, y, z + 1)] < 0f))
                    Quad(cellVert[CIdx(x - 1, y - 1, z)], cellVert[CIdx(x, y - 1, z)], cellVert[CIdx(x, y, z)], cellVert[CIdx(x - 1, y, z)]);
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            if (colour != null)
            {
                var cols = new Color[verts.Count];
                System.Threading.Tasks.Parallel.For(0, verts.Count, i => cols[i] = colour(verts[i]));
                mesh.colors = cols;
            }
            else
            {
                var cols = new Color[verts.Count];
                for (int i = 0; i < cols.Length; i++) cols[i] = new Color(0, 0, 0, 1);
                mesh.colors = cols;
            }
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Vector3 Grad(Func<Vector3, float> f, Vector3 p, float h)
        {
            return new Vector3(
                f(p + new Vector3(h, 0, 0)) - f(p - new Vector3(h, 0, 0)),
                f(p + new Vector3(0, h, 0)) - f(p - new Vector3(0, h, 0)),
                f(p + new Vector3(0, 0, h)) - f(p - new Vector3(0, 0, h))) / (2f * h);
        }
    }
}
