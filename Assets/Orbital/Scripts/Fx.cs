using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital
{
    public class ScorePopup
    {
        public string Text;
        public Vector3 Pos;
        public Color Colour;
        public float Age;
        public float Size;
    }

    /// <summary>Shared particle systems and one-shot effects.</summary>
    public class Fx : MonoBehaviour
    {
        public static Fx I { get; private set; }
        static OrbitalLibrary Lib => OrbitalLibrary.I;

        ParticleSystem debris, fire, sparks, smoke, embers;
        readonly Dictionary<Mesh, (Vector3[] pos, Color[] col)> voxelCache = new Dictionary<Mesh, (Vector3[], Color[])>();
        public readonly List<ScorePopup> Popups = new List<ScorePopup>();

        void Awake()
        {
            I = this;
            debris = MakeSystem("Debris", Lib.debris, 6000);
            var r = debris.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Lib.cube;
            r.alignment = ParticleSystemRenderSpace.World;
            r.shadowCastingMode = ShadowCastingMode.Off;
            var dm = debris.main;
            dm.startRotation3D = true;
            var sol = debris.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.7f, 0.85f), new Keyframe(1, 0)));
            var rol = debris.rotationOverLifetime; rol.enabled = true; rol.separateAxes = true;
            rol.x = new ParticleSystem.MinMaxCurve(-6f, 6f); rol.y = new ParticleSystem.MinMaxCurve(-6f, 6f); rol.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var lim = debris.limitVelocityOverLifetime; lim.enabled = true; lim.dampen = 0.05f; lim.limit = 0.5f;

            fire = MakeSystem("Fire", Lib.fire, 2000);
            var fs = fire.sizeOverLifetime; fs.enabled = true; fs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.4f), new Keyframe(0.25f, 1f), new Keyframe(1, 0.2f)));
            var fc = fire.colorOverLifetime; fc.enabled = true; fc.color = Grad(new Color(1f, 0.95f, 0.8f), new Color(1f, 0.25f, 0.05f), 1f);
            var fr = fire.rotationOverLifetime; fr.enabled = true; fr.z = new ParticleSystem.MinMaxCurve(-2f, 2f);

            sparks = MakeSystem("Sparks", Lib.spark, 3000);
            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch; sr.velocityScale = 0.04f; sr.lengthScale = 1.5f;
            var sc = sparks.colorOverLifetime; sc.enabled = true; sc.color = Grad(Color.white, new Color(1f, 0.4f, 0.1f), 1f);

            smoke = MakeSystem("Smoke", Lib.smoke, 800);
            var ss = smoke.sizeOverLifetime; ss.enabled = true; ss.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.2f));
            var sco = smoke.colorOverLifetime; sco.enabled = true; sco.color = Grad(Color.white, Color.white, 0.7f);

            embers = MakeSystem("Embers", Lib.ember, 2000);
            var en = embers.noise; en.enabled = true; en.strength = 0.8f; en.frequency = 1.2f;
            var ec = embers.colorOverLifetime; ec.enabled = true; ec.color = Grad(Color.white, new Color(1f, 0.3f, 0.1f), 1f);
        }

        static ParticleSystem MakeSystem(string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(I ? I.transform : null, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startSpeed = 0f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static Gradient Grad(Color a, Color b, float alpha)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
                      new[] { new GradientAlphaKey(alpha, 0), new GradientAlphaKey(alpha, 0.5f), new GradientAlphaKey(0, 1) });
            return g;
        }

        public void ClearAll()
        {
            foreach (var ps in new[] { debris, fire, sparks, smoke, embers }) ps.Clear();
            Popups.Clear();
        }

        // ------------------------------------------------------------ voxel debris

        (Vector3[] pos, Color[] col) VoxelsOf(Mesh m)
        {
            if (voxelCache.TryGetValue(m, out var v)) return v;
            var verts = m.vertices;
            var cols = m.colors;
            int n = verts.Length / 24;
            var p = new Vector3[n];
            var c = new Color[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 s = Vector3.zero;
                for (int k = 0; k < 24; k++) s += verts[i * 24 + k];
                p[i] = s / 24f;
                c[i] = cols.Length > 0 ? cols[i * 24] : Color.white;
            }
            voxelCache[m] = (p, c);
            return (p, c);
        }

        /// <summary>Break a voxel mesh rendered with `t` into tumbling cubes.</summary>
        public void VoxelBurst(Mesh mesh, Transform t, float voxelSize, Vector3 impulse, float speed, float life = 2.2f, int stride = 1)
        {
            var (pos, col) = VoxelsOf(mesh);
            var ep = new ParticleSystem.EmitParams();
            Vector3 centre = t.position;
            float scale = t.lossyScale.x;
            for (int i = 0; i < pos.Length; i += stride)
            {
                Vector3 wp = t.TransformPoint(pos[i]);
                Vector3 dir = (wp - centre);
                dir = dir.sqrMagnitude < 1e-4f ? Random.onUnitSphere : dir.normalized;
                ep.position = wp;
                ep.velocity = dir * speed * Random.Range(0.5f, 1.3f) + impulse + Random.insideUnitSphere * speed * 0.4f;
                ep.startSize = voxelSize * scale * 0.9f * stride * 0.85f;
                ep.startLifetime = life * Random.Range(0.6f, 1.2f);
                ep.rotation3D = new Vector3(Random.Range(0, 360f), Random.Range(0, 360f), Random.Range(0, 360f));
                Color c = col[i];
                c.a = Mathf.Max(c.a, 0.6f);
                ep.startColor = c;
                debris.Emit(ep, 1);
            }
        }

        public void Cubes(Vector3 at, Color colour, int count, float size, float speed, float life = 1.2f)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.position = at + Random.insideUnitSphere * size;
                ep.velocity = Random.onUnitSphere * speed * Random.Range(0.3f, 1f);
                ep.startSize = size * Random.Range(0.6f, 1.1f);
                ep.startLifetime = life * Random.Range(0.6f, 1.2f);
                ep.rotation3D = new Vector3(Random.Range(0, 360f), Random.Range(0, 360f), Random.Range(0, 360f));
                ep.startColor = colour;
                debris.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------ explosions

        public void Explosion(Vector3 at, float size, Color tint, bool smokePuff = true)
        {
            var ep = new ParticleSystem.EmitParams();
            int n = Mathf.RoundToInt(14 * size);
            for (int i = 0; i < n; i++)
            {
                ep.position = at + Random.insideUnitSphere * size * 0.3f;
                ep.velocity = Random.onUnitSphere * size * Random.Range(1f, 4f);
                ep.startSize = size * Random.Range(0.7f, 1.6f);
                ep.startLifetime = Random.Range(0.35f, 0.8f);
                ep.rotation = Random.Range(0, 360f);
                ep.startColor = Color.Lerp(Color.white, tint, 0.5f);
                fire.Emit(ep, 1);
            }
            Sparks(at, tint, Mathf.RoundToInt(30 * size), 9f * Mathf.Sqrt(size));
            if (smokePuff)
            {
                for (int i = 0; i < Mathf.RoundToInt(5 * size); i++)
                {
                    ep.position = at + Random.insideUnitSphere * size * 0.4f;
                    ep.velocity = Random.onUnitSphere * size * 0.8f;
                    ep.startSize = size * Random.Range(1.2f, 2f);
                    ep.startLifetime = Random.Range(1.2f, 2.4f);
                    ep.rotation = Random.Range(0, 360f);
                    ep.startColor = new Color(1, 1, 1, 0.6f);
                    smoke.Emit(ep, 1);
                }
            }
            Embers(at, tint, Mathf.RoundToInt(16 * size), size);
            Flash(at, tint, 18f * size, 6f + 3f * size, 0.35f);
            Shockwave(at, size * 3.5f, tint, 0.5f);
        }

        public void Sparks(Vector3 at, Color tint, int count, float speed, Vector3? dir = null, float spread = 1f)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Vector3 d = Random.onUnitSphere;
                if (dir.HasValue) d = (dir.Value.normalized + Random.insideUnitSphere * spread).normalized;
                ep.position = at;
                ep.velocity = d * speed * Random.Range(0.4f, 1.1f);
                ep.startSize = Random.Range(0.03f, 0.08f);
                ep.startLifetime = Random.Range(0.25f, 0.7f);
                ep.startColor = tint;
                sparks.Emit(ep, 1);
            }
        }

        public void Embers(Vector3 at, Color tint, int count, float radius)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.position = at + Random.insideUnitSphere * radius * 0.5f;
                ep.velocity = Random.onUnitSphere * Random.Range(0.5f, 2.5f);
                ep.startSize = Random.Range(0.04f, 0.12f);
                ep.startLifetime = Random.Range(0.8f, 2f);
                ep.startColor = tint;
                embers.Emit(ep, 1);
            }
        }

        public void Flash(Vector3 at, Color c, float intensity, float range, float duration) => StartCoroutine(FlashRoutine(at, c, intensity, range, duration));

        IEnumerator FlashRoutine(Vector3 at, Color c, float intensity, float range, float duration)
        {
            var go = new GameObject("Flash");
            go.transform.position = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.range = range; l.shadows = LightShadows.None;
            float t = 0;
            while (t < duration)
            {
                l.intensity = intensity * Mathf.Pow(1f - t / duration, 2f);
                yield return null;
                t += Time.unscaledDeltaTime;
            }
            Destroy(go);
        }

        public void Shockwave(Vector3 at, float radius, Color c, float duration) => StartCoroutine(ShockRoutine(at, radius, c, duration));

        IEnumerator ShockRoutine(Vector3 at, float radius, Color c, float duration)
        {
            var go = new GameObject("Shockwave");
            go.transform.position = at;
            go.AddComponent<MeshFilter>().sharedMesh = Lib.quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Lib.ring;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            var cam = CameraRig.I ? CameraRig.I.transform : null;
            float t = 0;
            while (t < duration)
            {
                float k = t / duration;
                if (cam) go.transform.rotation = Quaternion.LookRotation(at - cam.position) * Quaternion.Euler(90, 0, 0);
                go.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, radius, 1f - Mathf.Pow(1f - k, 3f));
                mpb.SetColor("_Color", c * 2f * (1f - k) * (1f - k));
                mr.SetPropertyBlock(mpb);
                yield return null;
                t += Time.deltaTime;
            }
            Destroy(go);
        }

        public void Popup(string text, Vector3 at, Color c, float size = 1f)
        {
            Popups.Add(new ScorePopup { Text = text, Pos = at, Colour = c, Size = size });
        }

        void Update()
        {
            for (int i = Popups.Count - 1; i >= 0; i--)
            {
                var p = Popups[i];
                p.Age += Time.unscaledDeltaTime;
                p.Pos += Vector3.up * Time.unscaledDeltaTime * 1.2f;
                if (p.Age > 1.3f) Popups.RemoveAt(i);
            }
        }
    }
}
