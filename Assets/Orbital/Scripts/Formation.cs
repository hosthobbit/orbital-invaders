using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Orbital
{
    /// <summary>
    /// The invader wall. Marches sideways in discrete ticks like the original, but each time it
    /// reaches an edge it steps TOWARD the player (along -Z) instead of down the screen.
    /// </summary>
    public class Formation : MonoBehaviour
    {
        public static Formation I { get; private set; }

        public readonly List<Invader> Invaders = new List<Invader>();
        public int Total { get; private set; }
        public int Alive { get; private set; }
        public bool Active { get; private set; }
        public bool Warping { get; private set; }
        public bool Frozen;

        const float StepX = 0.6f;
        const float StepZ = 1.3f;      // how far the wall creeps toward the player at each edge
        const float EdgeLimit = 15.5f;
        const float Spacing = 1.45f;   // layout scale

        int dir = 1;
        float tickTimer;
        int frame;
        int note;
        float fireTimer;
        float diveTimer;
        int wave;

        // regrouping: when the player is winning, the survivors fly into a new shape and get angrier
        int regroups;
        float lastRegroup;
        bool regrouping;
        int shapeIndex = -1;
        readonly Queue<float> recentKills = new Queue<float>();
        static readonly string[] ShapeNames = { "RING OF FIRE", "ARROWHEAD", "THE CUBE", "CORKSCREW" };
        public int Regroups => regroups;
        public bool Regrouping => regrouping;
        float Rage => 1f + 0.18f * regroups;

        void Awake() { I = this; }

        public float FrontZ
        {
            get
            {
                float z = float.MaxValue;
                foreach (var inv in Invaders) if (inv.Alive && !inv.Diving) z = Mathf.Min(z, inv.transform.position.z);
                return z == float.MaxValue ? 999f : z;
            }
        }

        public void Clear()
        {
            StopAllCoroutines();
            foreach (var inv in Invaders) if (inv) Destroy(inv.gameObject);
            Invaders.Clear();
            Alive = Total = 0;
            Active = false;
            Warping = false;
        }

        // ------------------------------------------------------------ layouts

        public void Build(int waveNumber)
        {
            Clear();
            wave = waveNumber;
            regroups = 0;
            regrouping = false;
            shapeIndex = -1;
            lastRegroup = Time.time;
            recentKills.Clear();
            dir = 1;
            frame = 0;
            note = 0;
            int layout = ((waveNumber - 1) % 5);
            int cycle = (waveNumber - 1) / 5;
            float startZ = 46f - Mathf.Min(cycle, 4) * 2.5f;
            transform.position = new Vector3(0f, 1.6f, startZ);

            switch (layout)
            {
                case 1: Double(); break;
                case 2: Chevron(); break;
                case 3: Diamond(); break;
                default: Classic(); break;
            }
            Total = Alive = Invaders.Count;
            tickTimer = 0.6f;
            fireTimer = 2.5f;
            diveTimer = 4f;
            StartCoroutine(WarpIn());
        }

        void Add(InvaderKind k, float x, float y, float z) => Invaders.Add(Invader.Create(k, new Vector3(x * Spacing, y * Spacing, z), transform));

        void Classic()
        {
            InvaderKind[] rows = { InvaderKind.Squid, InvaderKind.Crab, InvaderKind.Crab, InvaderKind.Octopus, InvaderKind.Octopus };
            for (int r = 0; r < 5; r++)
            for (int c = 0; c < 11; c++)
                Add(rows[r], (c - 5) * 1.85f, (2 - r) * 1.5f, 0f);
        }

        void Double()
        {
            // a front wall that shields a back wall of squids
            InvaderKind[] rows = { InvaderKind.Crab, InvaderKind.Octopus, InvaderKind.Octopus };
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 11; c++)
                Add(rows[r], (c - 5) * 1.85f, (1 - r) * 1.5f, 0f);
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 9; c++)
                Add(r == 0 ? InvaderKind.Squid : InvaderKind.Crab, (c - 4) * 1.85f, (2.5f - r) * 1.5f, 4.5f);
        }

        void Chevron()
        {
            for (int r = 0; r < 6; r++)
            {
                int half = 6 - r;
                for (int c = -half; c <= half; c++)
                {
                    if (Mathf.Abs(c) < half - 1 && r < 4) continue; // hollow V
                    var k = r == 0 ? InvaderKind.Squid : r < 3 ? InvaderKind.Crab : InvaderKind.Octopus;
                    Add(k, c * 1.7f, (2.5f - r) * 1.35f, Mathf.Abs(c) * 0.6f);
                }
            }
            // centre column of squids sitting deeper
            for (int r = 0; r < 4; r++) Add(InvaderKind.Squid, 0f, (2.5f - r) * 1.35f, 5f);
        }

        void Diamond()
        {
            for (int layer = 0; layer < 2; layer++)
            for (int r = -3; r <= 3; r++)
            {
                int w = 3 - Mathf.Abs(r);
                for (int c = -w; c <= w; c++)
                {
                    float x = c * 1.85f + (layer == 0 ? -4.5f : 4.5f);
                    var k = Mathf.Abs(r) == 3 ? InvaderKind.Squid : Mathf.Abs(r) >= 1 ? InvaderKind.Crab : InvaderKind.Octopus;
                    Add(k, x, 1.2f + r * 1.35f, layer * 3f);
                }
            }
        }

        IEnumerator WarpIn()
        {
            Warping = true;
            Active = false;
            SoundBank.I.Play(Sfx.Warp, 0.8f);
            var starts = new Dictionary<Invader, float>();
            foreach (var inv in Invaders)
            {
                starts[inv] = Random.Range(0f, 0.6f);
                inv.transform.localPosition = inv.Slot + new Vector3(0, 0, 160f);
                var tr = inv.gameObject.AddComponent<TrailRenderer>();
                tr.sharedMaterial = OrbitalLibrary.I.streak;
                tr.time = 0.12f;
                tr.widthMultiplier = 0.5f;
                tr.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
                Color c = inv.Kind == InvaderKind.Squid ? new Color(1f, 0.3f, 0.9f) : inv.Kind == InvaderKind.Crab ? new Color(0.3f, 0.9f, 1f) : new Color(0.4f, 1f, 0.5f);
                tr.startColor = c * 2f;
                tr.endColor = new Color(c.r, c.g, c.b, 0f);
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            float t = 0f, dur = 1.6f;
            while (t < dur)
            {
                foreach (var inv in Invaders)
                {
                    if (!inv) continue;
                    float k = Mathf.Clamp01((t - starts[inv]) / 0.8f);
                    float e = 1f - Mathf.Pow(1f - k, 4f);
                    inv.transform.localPosition = inv.Slot + new Vector3(0, 0, 160f * (1f - e));
                    if (k >= 1f && inv.Flash == 0f && t - starts[inv] < 0.85f) inv.Flash = 1f;
                }
                yield return null;
                t += Time.deltaTime;
            }
            foreach (var inv in Invaders)
            {
                if (!inv) continue;
                inv.transform.localPosition = inv.Slot;
                var tr = inv.GetComponent<TrailRenderer>();
                if (tr) Destroy(tr);
            }
            CameraRig.I.AddTrauma(0.25f);
            Warping = false;
            Active = true;
        }

        // ------------------------------------------------------------ marching

        float TickInterval()
        {
            float frac = Total > 0 ? Alive / (float)Total : 0f;
            float speedUp = 1f + 0.07f * (wave - 1);
            return Mathf.Lerp(0.07f, 0.65f, Mathf.Pow(frac, 1.3f)) / speedUp;
        }

        void Update()
        {
            if (!Active || Frozen || Alive == 0 || regrouping) return;
            tickTimer -= Time.deltaTime;
            if (tickTimer <= 0f)
            {
                tickTimer += TickInterval();
                Tick();
            }

            fireTimer -= Time.deltaTime;
            if (fireTimer <= 0f)
            {
                float frac = Alive / (float)Total;
                fireTimer = Mathf.Max(0.22f, 1.3f - wave * 0.07f) * (0.45f + 0.55f * frac) * Random.Range(0.6f, 1.3f) / Rage;
                TryFire();
            }

            if (wave >= 2 || regroups > 0)
            {
                diveTimer -= Time.deltaTime * Rage;
                if (diveTimer <= 0f)
                {
                    diveTimer = Random.Range(2.5f, 4.5f) / (1f + 0.08f * wave);
                    TryDive();
                }
            }
        }

        // ------------------------------------------------------------ regrouping

        /// <summary>Called for every invader the player kills; regroups when the player is winning.</summary>
        public void NotifyKill()
        {
            float now = Time.time;
            recentKills.Enqueue(now);
            while (recentKills.Count > 0 && now - recentKills.Peek() > 4f) recentKills.Dequeue();
            if (!Active || regrouping || Alive < 5 || regroups >= 3 || now - lastRegroup < 7f) return;
            float frac = Alive / (float)Total;
            bool losing = (regroups == 0 && frac <= 0.67f) || (regroups == 1 && frac <= 0.4f) || (regroups == 2 && frac <= 0.2f);
            bool streak = recentKills.Count >= 8;   // eight kills inside four seconds
            if (losing || streak) StartCoroutine(Regroup());
        }

        public void ForceRegroup(int shape = -1)
        {
            if (!Active || regrouping || Alive == 0) return;
            if (shape >= 0) shapeIndex = shape - 1;
            StartCoroutine(Regroup(shape >= 0));
        }

        IEnumerator Regroup(bool exactShape = false)
        {
            regrouping = true;
            regroups++;
            lastRegroup = Time.time;
            recentKills.Clear();
            var alive = Invaders.FindAll(i => i.Alive);
            int shape = (shapeIndex + 1 + (exactShape ? 0 : Random.Range(0, 3))) % ShapeNames.Length;
            shapeIndex = shape;
            var slots = ShapeSlots(shape, alive.Count);
            // pair invaders with slots left-to-right so they don't all cross over each other
            alive.Sort((a, b) => a.Slot.x.CompareTo(b.Slot.x));
            slots.Sort((a, b) => a.x.CompareTo(b.x));
            var from = new Vector3[alive.Count];
            for (int i = 0; i < alive.Count; i++)
            {
                from[i] = alive[i].transform.localPosition;
                alive[i].Slot = slots[i];
            }
            Game.I.ShowBanner("THEY'RE REGROUPING", ShapeNames[shape]);
            SoundBank.I.Play(Sfx.Warp, 0.8f, 1.25f, 0f);
            SoundBank.I.Play(Sfx.BossAlarm, 0.35f, 1.4f, 0f);
            CameraRig.I.AddTrauma(0.2f);
            Vector3 rootFrom = transform.position, rootTo = new Vector3(0f, rootFrom.y, rootFrom.z);
            float t = 0f, dur = 1.5f;
            while (t < dur)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                transform.position = Vector3.Lerp(rootFrom, rootTo, k);
                for (int i = 0; i < alive.Count; i++)
                {
                    var inv = alive[i];
                    if (!inv || !inv.Alive || inv.Diving) continue;
                    // swing out in depth and sideways on the way, so it reads as a manoeuvre
                    Vector3 swing = new Vector3(Mathf.Sin(i * 1.7f) * 2f, 0f, 4f) * Mathf.Sin(k * Mathf.PI);
                    inv.transform.localPosition = Vector3.Lerp(from[i], slots[i], k) + swing;
                }
                yield return null;
                t += Time.deltaTime;
            }
            foreach (var inv in alive)
                if (inv && inv.Alive && !inv.Diving) { inv.transform.localPosition = inv.Slot; inv.Flash = 0.7f; }
            dir = Random.value < 0.5f ? 1 : -1;
            tickTimer = 0.3f;
            regrouping = false;
        }

        static List<Vector3> ShapeSlots(int shape, int n)
        {
            var s = new List<Vector3>(n);
            switch (shape)
            {
                case 0: // concentric rings facing the player
                {
                    float[] radius = { 6.5f, 4.2f, 2.0f };
                    float[] depth = { 0f, 2.5f, 5f };
                    int left = n;
                    for (int ring = 0; ring < 3 && left > 0; ring++)
                    {
                        int cap = ring == 2 ? left : Mathf.Min(left, Mathf.FloorToInt(2f * Mathf.PI * radius[ring] / 1.9f));
                        for (int i = 0; i < cap; i++)
                        {
                            float a = (i / (float)cap) * Mathf.PI * 2f + ring * 0.4f;
                            s.Add(new Vector3(Mathf.Cos(a) * radius[ring] * 1.25f, 1.2f + Mathf.Sin(a) * radius[ring] * 0.8f, depth[ring]));
                        }
                        left -= cap;
                    }
                    break;
                }
                case 1: // stacked arrowheads pointing at the player
                {
                    int rows = Mathf.CeilToInt(n / 13f);
                    for (int i = 0; i < n; i++)
                    {
                        int row = i / 13, j = i % 13;
                        int k = (j + 1) / 2;
                        float side = j % 2 == 0 ? 1f : -1f;
                        // wings sweep up and back, so the V reads from the chase camera too
                        s.Add(new Vector3(side * k * 1.9f, ((rows - 1) * 0.5f - row) * 2.0f + k * 0.55f - 0.6f, k * 1.2f + row * 0.8f));
                    }
                    break;
                }
                case 2: // a 3D block, several layers deep
                {
                    int layers = n > 36 ? 3 : 2;
                    int per = Mathf.CeilToInt(n / (float)layers);
                    int cols = Mathf.CeilToInt(Mathf.Sqrt(per * 2.2f));
                    int rowsN = Mathf.CeilToInt(per / (float)cols);
                    for (int i = 0; i < n; i++)
                    {
                        int l = i / per, j = i % per, c = j % cols, r = j / cols;
                        // offset back layers by half a cell so every layer stays visible through the gaps
                        s.Add(new Vector3((c - (cols - 1) * 0.5f) * 2.7f + (l % 2) * 1.35f, ((rowsN - 1) * 0.5f - r) * 2.3f + 1.2f + (l % 2) * 1.1f, l * 4f));
                    }
                    break;
                }
                default: // side-by-side corkscrews boring toward the player
                {
                    int strands = Mathf.Clamp(Mathf.CeilToInt(n / 9f), 1, 4);
                    for (int i = 0; i < n; i++)
                    {
                        int st = i % strands, j = i / strands;
                        float a = j * 0.8f + st * 1.3f;
                        float cx = (st - (strands - 1) * 0.5f) * 8.5f;
                        s.Add(new Vector3(cx + Mathf.Cos(a) * 3.2f, 1.2f + Mathf.Sin(a) * 3.0f, j * 1.4f));
                    }
                    break;
                }
            }
            return s;
        }

        // ------------------------------------------------------------ divers

        public int Divers { get { int n = 0; foreach (var inv in Invaders) if (inv.Alive && inv.Dive == DiveState.Swoop) n++; return n; } }
        int MaxDivers => Mathf.Clamp(1 + (wave - 2) / 2 + regroups, 1, 5);

        /// <summary>Send one front-line invader on a dive at the player. Returns false if nobody could go.</summary>
        public bool TryDive(bool force = false)
        {
            if (!Active || Frozen || !Player.I || !Player.I.Alive) return false;
            if (!force && Divers >= MaxDivers) return false;
            FindShooters();
            if (shooters.Count == 0) return false;
            // squids on the flanks love to dive; anyone else will do
            Invader pick = shooters[Random.Range(0, shooters.Count)];
            if (Random.value < 0.6f)
            {
                float best = -1f;
                foreach (var c in shooters)
                {
                    float score = Mathf.Abs(c.transform.position.x) + (c.Kind == InvaderKind.Squid ? 6f : 0f) + Random.value * 4f;
                    if (score > best) { best = score; pick = c; }
                }
            }
            float dur = Mathf.Lerp(3.0f, 2.0f, Mathf.Clamp01((wave - 2) / 10f));
            pick.StartDive(Player.I.transform.position, dur);
            pick.Flash = 1f;
            SoundBank.I.Play(Sfx.Dive, 0.55f, Random.Range(0.92f, 1.08f), 0f);
            return true;
        }

        public void DiverFire(Invader inv)
        {
            if (!Player.I || !Player.I.Alive) return;
            Vector3 from = inv.transform.position + Vector3.back * 0.6f;
            Vector3 to = Player.I.transform.position;
            if (to.z > from.z - 3f) return;   // already past the player
            Game.I.FireEnemy(from, (to - from).normalized * (13f + wave * 0.6f), 1);
        }

        public void RecallDivers()
        {
            foreach (var inv in Invaders) if (inv && inv.Alive) inv.Recall();
        }

        void Tick()
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var inv in Invaders)
            {
                if (!inv.Alive || inv.Diving) continue;
                float x = inv.transform.position.x;
                min = Mathf.Min(min, x - inv.HalfWidth);
                max = Mathf.Max(max, x + inv.HalfWidth);
            }
            if ((dir > 0 && max + StepX > EdgeLimit) || (dir < 0 && min - StepX < -EdgeLimit))
            {
                transform.position += new Vector3(0, 0, -StepZ);
                dir = -dir;
                CameraRig.I.AddTrauma(0.05f);
            }
            else transform.position += new Vector3(StepX * dir, 0, 0);

            frame ^= 1;
            foreach (var inv in Invaders) if (inv.Alive) inv.SetFrame(frame);
            SoundBank.I.Play((Sfx)((int)Sfx.March0 + note), 0.9f, 1f, 0f);
            note = (note + 1) & 3;
        }

        // ------------------------------------------------------------ shooting

        readonly List<Invader> shooters = new List<Invader>();

        /// <summary>Invaders in formation with nothing in front of them (toward the player).</summary>
        void FindShooters()
        {
            shooters.Clear();
            foreach (var a in Invaders)
            {
                if (!a.Alive || a.Diving) continue;
                bool blocked = false;
                foreach (var b in Invaders)
                {
                    if (b == a || !b.Alive || b.Diving) continue;
                    if (Mathf.Abs(b.Slot.x - a.Slot.x) < 0.5f && Mathf.Abs(b.Slot.y - a.Slot.y) < 0.5f && b.Slot.z < a.Slot.z) { blocked = true; break; }
                }
                if (!blocked) shooters.Add(a);
            }
        }

        void TryFire()
        {
            var game = Game.I;
            if (game.EnemyShotCount >= 3 + wave) return;
            FindShooters();
            if (shooters.Count == 0) return;
            var s = shooters[Random.Range(0, shooters.Count)];
            // prefer an invader roughly in line with the player half the time
            if (Random.value < 0.5f && Player.I)
            {
                float best = float.MaxValue;
                foreach (var c in shooters)
                {
                    float d = Mathf.Abs(c.transform.position.x - Player.I.transform.position.x);
                    if (d < best) { best = d; s = c; }
                }
            }
            Vector3 from = s.transform.position + Vector3.back * 0.6f;
            float speed = 12f + wave * 0.7f;
            int kind = Random.value < 0.35f + wave * 0.04f ? 1 : (Random.value < 0.5f ? 2 : 0);
            Vector3 vel = Vector3.back * speed;
            if (kind == 1 && Player.I)
            {
                // aimed at where the player is now
                vel = (Player.I.transform.position - from).normalized * speed;
            }
            game.FireEnemy(from, vel, kind);
            s.Flash = 0.5f;
        }

        // ------------------------------------------------------------ damage

        public void Kill(Invader inv, Vector3 impulse)
        {
            if (!inv.Alive) return;
            inv.Alive = false;
            Alive--;
            Color c = inv.Kind == InvaderKind.Squid ? new Color(1f, 0.3f, 0.9f) : inv.Kind == InvaderKind.Crab ? new Color(0.3f, 0.9f, 1f) : new Color(0.4f, 1f, 0.5f);
            Fx.I.VoxelBurst(inv.CurrentMesh, inv.transform, Invader.VoxelSize, impulse, 4.5f);
            Fx.I.Flash(inv.transform.position, c, 8f, 5f, 0.25f);
            Fx.I.Sparks(inv.transform.position, c, 18, 6f);
            Fx.I.Shockwave(inv.transform.position, 2.2f, c, 0.35f);
            SoundBank.I.Play(Sfx.InvaderDie, 0.7f);
            inv.gameObject.SetActive(false);
        }
    }
}
