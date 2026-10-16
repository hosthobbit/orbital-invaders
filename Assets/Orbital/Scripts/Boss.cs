using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital
{
    /// <summary>The mothership: four turrets guard its core; every fifth wave.</summary>
    public class Boss : MonoBehaviour
    {
        public static Boss I { get; private set; }
        public const float VoxelSize = 0.3f;
        const float Scale = 1.6f;          // the hull mesh is ~12 units wide; scaled so it dominates the screen
        const float TurretScale = 1.9f;    // local, on top of Scale

        public bool Alive { get; private set; }
        public bool Dying { get; private set; }
        public float Health, MaxHealth;
        public Vector2 Half;

        class Turret
        {
            public Transform T;
            public MeshRenderer R;
            public float Hp;
            public float Flash;
            public bool Alive;
            public float FireTimer;
        }

        readonly Turret[] turrets = new Turret[4];
        MeshRenderer body;
        MaterialPropertyBlock block;
        float flash;
        float age;
        float spreadTimer, spiralTimer;
        float spiralAngle;
        int cycle;
        Vector3 home;

        void Awake()
        {
            I = this;
            var lib = OrbitalLibrary.I;
            gameObject.AddComponent<MeshFilter>().sharedMesh = lib.bossMesh;
            body = gameObject.AddComponent<MeshRenderer>();
            body.sharedMaterial = lib.bossHull;
            body.shadowCastingMode = ShadowCastingMode.On;
            block = new MaterialPropertyBlock();
            transform.localScale = Vector3.one * Scale;
            var b = lib.bossMesh.bounds;
            Half = new Vector2(b.extents.x * 0.92f, b.extents.y * 0.9f) * Scale;
            float[] xs = { -4.6f, -1.6f, 1.6f, 4.6f };
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Turret");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(xs[i], -b.extents.y - 0.2f, -1.6f);
                go.transform.localScale = Vector3.one * TurretScale;
                go.AddComponent<MeshFilter>().sharedMesh = lib.bossTurretMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = lib.voxel;
                turrets[i] = new Turret { T = go.transform, R = r };
            }
            gameObject.SetActive(false);
        }

        public void Spawn(int bossNumber)
        {
            cycle = bossNumber;
            gameObject.SetActive(true);
            MaxHealth = Health = 90f + 40f * bossNumber;
            foreach (var t in turrets)
            {
                t.Hp = 10f + 4f * bossNumber;
                t.Alive = true;
                t.T.gameObject.SetActive(true);
                t.FireTimer = Random.Range(1.5f, 3f);
            }
            home = new Vector3(0, 4.2f, 34f);
            transform.position = home + new Vector3(0, 0, 150f);
            Alive = false;
            Dying = false;
            age = 0f;
            spreadTimer = 3f;
            spiralTimer = 6f;
            StartCoroutine(Arrive());
        }

        IEnumerator Arrive()
        {
            SoundBank.I.Play(Sfx.BossAlarm, 0.8f, 1f, 0f);
            yield return new WaitForSeconds(1.2f);
            SoundBank.I.Play(Sfx.Warp, 1f, 0.7f, 0f);
            Vector3 from = transform.position;
            float t = 0;
            while (t < 2.2f)
            {
                float k = 1f - Mathf.Pow(1f - t / 2.2f, 3f);
                transform.position = Vector3.Lerp(from, home, k);
                yield return null;
                t += Time.deltaTime;
            }
            CameraRig.I.AddTrauma(0.5f);
            Alive = true;
        }

        /// <summary>Where the autopilot should shoot: the turret nearest the centre line, then the core.</summary>
        public Vector3 AimPoint
        {
            get
            {
                Turret best = null;
                foreach (var t in turrets)
                    if (t.Alive && (best == null || Mathf.Abs(t.T.position.x) < Mathf.Abs(best.T.position.x))) best = t;
                return best != null ? best.T.position : transform.position + Vector3.down * Half.y * 0.5f;
            }
        }

        public int TurretsAlive
        {
            get { int n = 0; foreach (var t in turrets) if (t.Alive) n++; return n; }
        }

        void Update()
        {
            if (!Alive || Dying) { UpdateFlash(); return; }
            float dt = Time.deltaTime;
            age += dt;
            bool enraged = Health < MaxHealth * 0.5f;
            // patrol, and creep closer the longer the fight lasts
            float advance = Mathf.Min(age * 0.2f, 14f);
            transform.position = new Vector3(Mathf.Sin(age * 0.45f) * 6.5f, home.y + Mathf.Sin(age * 0.8f) * 1.2f, home.z - advance);
            transform.rotation = Quaternion.Euler(Mathf.Sin(age * 0.7f) * 4f, Mathf.Sin(age * 0.3f) * 8f, Mathf.Sin(age * 0.45f) * -6f);

            var game = Game.I;
            foreach (var t in turrets)
            {
                if (!t.Alive) continue;
                t.FireTimer -= dt;
                if (t.FireTimer <= 0f && Player.I)
                {
                    t.FireTimer = Random.Range(1.4f, 2.4f) / (1f + 0.15f * cycle);
                    Vector3 from = t.T.position + Vector3.back * 1.2f;
                    game.FireEnemy(from, (Player.I.transform.position - from).normalized * (15f + cycle), 1);
                }
            }
            spreadTimer -= dt;
            if (spreadTimer <= 0f)
            {
                spreadTimer = enraged ? 1.8f : 3f;
                Vector3 from = transform.position + new Vector3(0, -Half.y, -2f);
                int n = enraged ? 9 : 7;
                for (int i = 0; i < n; i++)
                {
                    float a = (i - (n - 1) * 0.5f) * 5f;
                    Vector3 v = Quaternion.Euler(Random.Range(-3f, 3f), a, 0) * Vector3.back * 14f;
                    game.FireEnemy(from, v, 2);
                }
                SoundBank.I.Play(Sfx.EnemyShot, 0.8f, 0.7f);
            }
            if (enraged)
            {
                spiralTimer -= dt;
                if (spiralTimer <= 0f)
                {
                    spiralTimer = 0.09f;
                    spiralAngle += 37f;
                    Vector3 dirXY = Quaternion.Euler(0, 0, spiralAngle) * Vector3.right;
                    game.FireEnemy(transform.position + Vector3.back * 2f, (Vector3.back * 13f + dirXY * 3.5f), 0);
                    if (Random.value < 0.02f) spiralTimer = 2.5f; // breathing room
                }
            }
            UpdateFlash();
        }

        void UpdateFlash()
        {
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 5f);
            block.SetFloat("_Flash", flash);
            body.SetPropertyBlock(block);
            foreach (var t in turrets)
            {
                if (!t.Alive) continue;
                t.Flash = Mathf.MoveTowards(t.Flash, 0f, Time.deltaTime * 6f);
                var b = new MaterialPropertyBlock();
                b.SetFloat("_Flash", t.Flash);
                t.R.SetPropertyBlock(b);
            }
        }

        /// <summary>Test a shot segment; returns true if it hit something.</summary>
        public bool TryHit(Vector3 a, Vector3 b, float damage, out Vector3 at)
        {
            at = b;
            if (!Alive || Dying) return false;
            foreach (var t in turrets)
            {
                if (!t.Alive) continue;
                Vector3 tp = t.T.position;
                float ts = t.T.lossyScale.x;
                if (Crosses(a, b, tp.z, 1.2f * ts, out var p) && Mathf.Abs(p.x - tp.x) < 0.55f * ts && Mathf.Abs(p.y - tp.y) < 0.5f * ts)
                {
                    at = p;
                    t.Hp -= damage;
                    t.Flash = 1f;
                    Fx.I.Sparks(p, new Color(1f, 0.6f, 0.2f), 10, 5f);
                    if (t.Hp <= 0f) KillTurret(t);
                    return true;
                }
            }
            Vector3 c = transform.position;
            if (Crosses(a, b, c.z, 2.5f * Scale, out var q) && Mathf.Abs(q.x - c.x) < Half.x && Mathf.Abs(q.y - c.y) < Half.y)
            {
                at = q;
                // the core is armoured while turrets stand
                float dmg = damage * (TurretsAlive > 0 ? 0.25f : 1f);
                Health -= dmg;
                flash = TurretsAlive > 0 ? 0.35f : 0.9f;
                Fx.I.Sparks(q, TurretsAlive > 0 ? new Color(0.5f, 0.7f, 1f) : new Color(1f, 0.5f, 0.2f), 8, 5f);
                if (Health <= 0f) StartCoroutine(Die());
                return true;
            }
            return false;
        }

        static bool Crosses(Vector3 a, Vector3 b, float z, float slack, out Vector3 p)
        {
            p = b;
            float lo = Mathf.Min(a.z, b.z) - slack, hi = Mathf.Max(a.z, b.z) + slack;
            if (z < lo || z > hi) return false;
            float t = Mathf.Abs(b.z - a.z) > 1e-5f ? Mathf.Clamp01((z - a.z) / (b.z - a.z)) : 1f;
            p = Vector3.Lerp(a, b, t);
            return true;
        }

        void KillTurret(Turret t)
        {
            t.Alive = false;
            Fx.I.VoxelBurst(OrbitalLibrary.I.bossTurretMesh, t.T, 0.18f, Vector3.zero, 5f);
            Fx.I.Explosion(t.T.position, 2.2f, new Color(1f, 0.5f, 0.2f));
            SoundBank.I.Play(Sfx.BigBoom, 0.7f, 1.3f);
            CameraRig.I.AddTrauma(0.35f);
            PostFx.I.Impact(0.5f);
            Game.I.AddScore(500, t.T.position, new Color(1f, 0.6f, 0.2f));
            t.T.gameObject.SetActive(false);
            if (TurretsAlive == 0) Fx.I.Popup("CORE EXPOSED", transform.position + Vector3.up * 4.5f, new Color(1f, 0.3f, 0.2f), 1.6f);
        }

        IEnumerator Die()
        {
            Dying = true;
            Game.I.ClearEnemyShots();
            PostFx.I.SlowMo(0.3f, 2.5f);
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = transform.position + new Vector3(Random.Range(-Half.x, Half.x), Random.Range(-Half.y, Half.y), Random.Range(-1f, 1f));
                Fx.I.Explosion(p, Random.Range(1.5f, 3f), new Color(1f, Random.Range(0.3f, 0.7f), 0.2f));
                SoundBank.I.Play(Sfx.BigBoom, 0.6f, Random.Range(0.8f, 1.3f));
                CameraRig.I.AddTrauma(0.25f);
                flash = 1f;
                yield return new WaitForSeconds(0.14f);
            }
            Fx.I.VoxelBurst(OrbitalLibrary.I.bossMesh, transform, VoxelSize, Vector3.zero, 9f, 4f);
            Fx.I.Explosion(transform.position, 8f, new Color(1f, 0.55f, 0.25f));
            Fx.I.Shockwave(transform.position, 40f, new Color(1f, 0.7f, 0.4f), 1.2f);
            SoundBank.I.Play(Sfx.Bomb, 1f, 0.8f, 0f);
            CameraRig.I.AddTrauma(1f);
            PostFx.I.Impact(1f);
            PostFx.I.Kick(1f);
            Game.I.AddScore(5000, transform.position + Vector3.up * 2f, new Color(1f, 0.85f, 0.3f));
            Alive = false;
            Dying = false;   // Game only clears the boss wave once this drops
            gameObject.SetActive(false);
        }

        public void Hide()
        {
            StopAllCoroutines();
            Alive = false;
            Dying = false;
            gameObject.SetActive(false);
        }
    }
}
