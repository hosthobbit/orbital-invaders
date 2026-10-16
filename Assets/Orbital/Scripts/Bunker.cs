using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital
{
    /// <summary>A floating voxel asteroid shield that erodes shot by shot, from both sides.</summary>
    public class Bunker : MonoBehaviour
    {
        public const float Size = 0.17f;
        const int W = 22, H = 16, D = 3;

        static readonly string[] Shape =
        {
            "....##############....",
            "...################...",
            "..##################..",
            ".####################.",
            "######################",
            "######################",
            "######################",
            "######################",
            "######################",
            "######################",
            "######################",
            "#######........#######",
            "######..........######",
            "#####............#####",
            "#####............#####",
            "#####............#####",
        };

        readonly bool[,,] solid = new bool[W, H, D];
        readonly Color[,,] colour = new Color[W, H, D];
        Mesh mesh;
        bool dirty;
        float spin;
        Vector3 basePos;
        public int Remaining { get; private set; }

        public static Bunker Create(Vector3 pos, Transform parent, int seed)
        {
            var go = new GameObject("Bunker");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var b = go.AddComponent<Bunker>();
            b.basePos = pos;
            var rng = new System.Random(seed);
            for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            for (int z = 0; z < D; z++)
            {
                if (Shape[H - 1 - y][x] != '#') continue;
                b.solid[x, y, z] = true;
                float g = 0.32f + (float)rng.NextDouble() * 0.12f;
                // rock with occasional glowing mineral veins
                bool vein = rng.NextDouble() < 0.06;
                b.colour[x, y, z] = vein ? new Color(1f, 0.55f, 0.2f, 0.9f) : new Color(g * 1.05f, g, g * 0.95f, 0f);
                b.Remaining++;
            }
            b.mesh = new Mesh { name = "Bunker" };
            go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = OrbitalLibrary.I.bunker;
            mr.shadowCastingMode = ShadowCastingMode.On;
            b.spin = (float)rng.NextDouble() * 10f;
            b.Rebuild();
            return b;
        }

        void Rebuild()
        {
            var list = new List<Voxel>();
            for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            for (int z = 0; z < D; z++)
                if (solid[x, y, z]) list.Add(new Voxel(x, y, z, colour[x, y, z]));
            VoxelMesher.Build(list, Size, 0.94f, new Vector3((W - 1) * 0.5f, (H - 1) * 0.5f, (D - 1) * 0.5f), mesh);
            dirty = false;
        }

        void LateUpdate()
        {
            if (dirty) Rebuild();
            // gentle float
            transform.position = basePos + new Vector3(0, Mathf.Sin(Time.time * 0.6f + spin) * 0.08f, 0);
        }

        Vector3Int ToGrid(Vector3 world)
        {
            Vector3 l = transform.InverseTransformPoint(world) / Size + new Vector3((W - 1) * 0.5f, (H - 1) * 0.5f, (D - 1) * 0.5f);
            return new Vector3Int(Mathf.RoundToInt(l.x), Mathf.RoundToInt(l.y), Mathf.RoundToInt(l.z));
        }

        Vector3 ToWorld(int x, int y, int z) => transform.TransformPoint(new Vector3(x - (W - 1) * 0.5f, y - (H - 1) * 0.5f, z - (D - 1) * 0.5f) * Size);

        public float ZMin => transform.position.z - D * Size * 0.5f - 0.1f;
        public float ZMax => transform.position.z + D * Size * 0.5f + 0.1f;

        /// <summary>Does a shot travelling from a to b hit solid rock? Carves a crater if so.</summary>
        public bool TryHit(Vector3 a, Vector3 b, float radius, bool fromFront)
        {
            if (Remaining == 0) return false;
            float zLo = Mathf.Min(a.z, b.z), zHi = Mathf.Max(a.z, b.z);
            if (zHi < ZMin || zLo > ZMax) return false;
            float t = Mathf.Abs(b.z - a.z) > 1e-5f ? Mathf.Clamp01((transform.position.z - a.z) / (b.z - a.z)) : 0f;
            Vector3 p = Vector3.Lerp(a, b, t);
            var g = ToGrid(p);
            int r = Mathf.CeilToInt(radius / Size);
            bool hit = false;
            for (int dx = -r; dx <= r && !hit; dx++)
            for (int dy = -r; dy <= r && !hit; dy++)
            for (int z = 0; z < D && !hit; z++)
            {
                int x = g.x + dx, y = g.y + dy;
                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                if (solid[x, y, z]) hit = true;
            }
            if (!hit) return false;
            Crater(g, 2.2f, fromFront);
            return true;
        }

        void Crater(Vector3Int c, float rad, bool fromFront)
        {
            int removed = 0;
            int r = Mathf.CeilToInt(rad);
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            for (int z = 0; z < D; z++)
            {
                int x = c.x + dx, y = c.y + dy;
                if (x < 0 || y < 0 || x >= W || y >= H || !solid[x, y, z]) continue;
                float d = Mathf.Sqrt(dx * dx + dy * dy) + Random.Range(-0.6f, 0.6f);
                if (d > rad) continue;
                solid[x, y, z] = false;
                removed++;
                Remaining--;
                if (Random.value < 0.5f)
                    Fx.I.Cubes(ToWorld(x, y, z), colour[x, y, z].a > 0.5f ? colour[x, y, z] : new Color(0.4f, 0.38f, 0.36f, 0.05f), 1, Size * 0.9f, 3f, 1.5f);
            }
            if (removed > 0)
            {
                dirty = true;
                SoundBank.I.Play(Sfx.BunkerHit, 0.5f);
            }
        }

        /// <summary>Invaders grinding through the shield erase what they touch.</summary>
        public void EraseBox(Vector3 centre, Vector3 half)
        {
            if (Remaining == 0) return;
            if (Mathf.Abs(centre.z - transform.position.z) > half.z + D * Size) return;
            bool any = false;
            for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            for (int z = 0; z < D; z++)
            {
                if (!solid[x, y, z]) continue;
                Vector3 w = ToWorld(x, y, z);
                if (Mathf.Abs(w.x - centre.x) < half.x && Mathf.Abs(w.y - centre.y) < half.y)
                {
                    solid[x, y, z] = false;
                    Remaining--;
                    any = true;
                }
            }
            if (any) dirty = true;
        }
    }
}
