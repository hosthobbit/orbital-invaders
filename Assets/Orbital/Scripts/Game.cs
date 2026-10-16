using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital
{
    public enum GameState { Title, Intro, Playing, Dying, Cleared, GameOver }

    public class Shot
    {
        public Vector3 pos, prev, vel;
        public bool player, pierce, live;
        public int kind;
        public float age;
        public Transform t;
        public MeshRenderer r;
        public Light light;
        public TrailRenderer trail;
    }

    /// <summary>Waves, shots, collisions, scoring and the overall game flow.</summary>
    public class Game : MonoBehaviour
    {
        public static Game I { get; private set; }

        public Transform Logo;
        public GameState State { get; private set; } = GameState.Title;
        public bool Demo { get; private set; } = true;
        public bool Paused { get; private set; }
        public int Score { get; private set; }
        public int HiScore => HighScores.Top;
        public int Lives { get; private set; }
        public int Wave { get; private set; }
        public int Combo { get; private set; } = 1;

        // panic fire: an all-directions burst; charges are earned by killing invaders
        public const int PanicMax = 3;
        const int KillsPerPanic = 20;
        public int PanicCharges { get; private set; }
        public float PanicMeter { get; private set; }
        public string Banner { get; private set; } = "";
        public string SubBanner { get; private set; } = "";
        public float BannerAge { get; private set; } = 99f;
        public string GameOverReason { get; private set; } = "";
        public bool NewHiScore { get; private set; }

        // arcade initials entry at game over
        public bool EnteringInitials { get; private set; }
        public readonly char[] Initials = { 'A', 'A', 'A' };
        public int InitialsCursor { get; private set; }
        public int PendingRank { get; private set; }
        public int HighScoreRow { get; private set; } = -1;
        const string InitialsChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!?";
        float padRepeat;
        public bool BossWave => Wave > 0 && Wave % 5 == 0;

        public bool ControlsEnabled => !Paused && (State == GameState.Playing || State == GameState.Intro || State == GameState.Cleared);

        Formation formation;
        Player player;
        Ufo ufo;
        Boss boss;
        readonly List<Bunker> bunkers = new List<Bunker>();
        readonly List<PowerUp> powerups = new List<PowerUp>();
        readonly List<Shot> shots = new List<Shot>();
        MaterialPropertyBlock shotBlock;
        Transform world;
        float stateTimer;
        float ufoTimer;
        float lastKill;
        int nextExtra;
        bool bossSpawned;

        // automation
        bool autoshot;
        public bool Automated => autoshot;
        string autoshotDir;

        void Awake()
        {
            I = this;
            shotBlock = new MaterialPropertyBlock();
#if UNITY_WEBGL && !UNITY_EDITOR
            Application.targetFrameRate = -1;   // the browser paces frames (requestAnimationFrame)
#else
            Application.targetFrameRate = 144;
            QualitySettings.vSyncCount = 1;
#endif
        }

        void Start()
        {
            formation = Formation.I;
            player = Player.I;
            ufo = Ufo.I;
            boss = Boss.I;
            world = new GameObject("World").transform;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--autoshot" && i + 1 < args.Length) { autoshot = true; autoshotDir = args[i + 1]; }
            HighScores.Init(autoshot);
            if (autoshot) { StartCoroutine(AutoShot()); return; }
            NewGame(true);
        }

        // ------------------------------------------------------------ flow

        public void NewGame(bool demo)
        {
            Demo = demo;
            Paused = false;
            PostFx.I.Paused = false;
            Score = 0;
            Lives = 3;
            Wave = 1;
            Combo = 1;
            PanicCharges = 1;
            PanicMeter = 0f;
            nextExtra = 10000;
            NewHiScore = false;
            EnteringInitials = false;
            HighScoreRow = -1;
            Fx.I.ClearAll();
            ClearAllShots();
            foreach (var p in powerups) if (p) Destroy(p.gameObject);
            powerups.Clear();
            ufo.Hide();
            boss.Hide();
            player.AutoPilot = demo;
            player.ResetForGame();
            CameraRig.I.Mode = demo ? CamMode.Title : CamMode.Follow;
            CameraRig.I.Target = player.transform;
            if (Logo) Logo.gameObject.SetActive(demo);
            BuildBunkers();
            StartWave();
        }

        void StartWave()
        {
            ClearEnemyShots();
            ufoTimer = Random.Range(12f, 20f);
            bossSpawned = false;
            formation.Frozen = false;
            if ((Wave - 1) % 5 == 0 && Wave > 1) BuildBunkers();
            if (BossWave)
            {
                formation.Clear();
                boss.Spawn(Wave / 5 - 1);
                bossSpawned = true;
                ShowBanner("WARNING", "MOTHERSHIP APPROACHING");
            }
            else
            {
                formation.Build(Wave);
                ShowBanner("WAVE " + Wave, Wave == 1 ? "DEFEND THE EARTH" : WaveTagline());
                SoundBank.I.Play(Sfx.WaveStart, 0.7f, 1f, 0f);
            }
            State = GameState.Intro;
            stateTimer = 0f;
        }

        string WaveTagline()
        {
            switch ((Wave - 1) % 5)
            {
                case 1: return "THEY BROUGHT REINFORCEMENTS";
                case 2: return "CHEVRON ASSAULT";
                case 3: return "TWIN DIAMONDS";
                default: return "THEY'RE FASTER NOW";
            }
        }

        public void ShowBanner(string a, string b)
        {
            Banner = a;
            SubBanner = b;
            BannerAge = 0f;
        }

        void BuildBunkers()
        {
            foreach (var b in bunkers) if (b) Destroy(b.gameObject);
            bunkers.Clear();
            float[] xs = { -10f, -3.4f, 3.4f, 10f };
            for (int i = 0; i < xs.Length; i++)
                bunkers.Add(Bunker.Create(new Vector3(xs[i], i % 2 == 0 ? -1.2f : 1.2f, 15f), world, 100 + i * 7));
        }

        public void TogglePause()
        {
            if (Demo || State == GameState.GameOver || State == GameState.Title) return;
            Paused = !Paused;
            PostFx.I.Paused = Paused;
            if (!Paused) Time.timeScale = 1f;
        }

        public void ToTitle()
        {
            NewGame(true);
        }

        void Update()
        {
            if (autoshot) { Tick(); return; }
            if (EnteringInitials) { UpdateInitials(); Tick(); return; }
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
            {
                if (Demo) { }
                else if (State == GameState.GameOver) ToTitle();
                else TogglePause();
            }
            if (Demo && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)))
            {
                SoundBank.I.Play(Sfx.Select, 0.8f);
                NewGame(false);
                return;
            }
            if (!Demo && State == GameState.GameOver && stateTimer > 2.5f && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)))
            {
                NewGame(false);
                return;
            }
            Tick();
        }

        // ------------------------------------------------------------ high score initials

        void UpdateInitials()
        {
            if (stateTimer < 1.2f) return;   // don't let held fire/move keys type letters
            foreach (char raw in Input.inputString)
            {
                if (raw == '\n' || raw == '\r') { SubmitInitials(); return; }
                if (raw == '\b') { InitialsCursor = Mathf.Max(0, InitialsCursor - 1); continue; }
                char c = char.ToUpperInvariant(raw);
                if (InitialsChars.IndexOf(c) < 0) continue;
                Initials[InitialsCursor] = c;
                SoundBank.I.Play(Sfx.Select, 0.6f);
                if (InitialsCursor < 2) InitialsCursor++;
            }
            if (Input.GetKeyDown(KeyCode.KeypadEnter)) { SubmitInitials(); return; }
            if (Input.GetKeyDown(KeyCode.UpArrow)) CycleInitial(1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) CycleInitial(-1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) InitialsCursor = Mathf.Max(0, InitialsCursor - 1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) InitialsCursor = Mathf.Min(2, InitialsCursor + 1);

            // gamepad: stick up/down picks a letter, A moves on (and saves on the last), B goes back
            padRepeat -= Time.unscaledDeltaTime;
            float v = Input.anyKey ? 0f : Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(v) > 0.5f && padRepeat <= 0f) { CycleInitial(v > 0 ? 1 : -1); padRepeat = 0.18f; }
            if (Mathf.Abs(v) <= 0.5f) padRepeat = 0f;
            if (Input.GetKeyDown(KeyCode.JoystickButton0)) { if (InitialsCursor < 2) InitialsCursor++; else SubmitInitials(); }
            if (Input.GetKeyDown(KeyCode.JoystickButton1)) InitialsCursor = Mathf.Max(0, InitialsCursor - 1);
        }

        void CycleInitial(int step)
        {
            int i = InitialsChars.IndexOf(Initials[InitialsCursor]);
            if (i < 0) i = 0;
            Initials[InitialsCursor] = InitialsChars[(i + step + InitialsChars.Length) % InitialsChars.Length];
            SoundBank.I.Play(Sfx.Select, 0.5f, step > 0 ? 1.1f : 0.9f);
        }

        public void SubmitInitials()
        {
            if (!EnteringInitials) return;
            string name = new string(Initials);
            HighScoreRow = HighScores.Add(name, Score, Wave);
            if (!autoshot) { PlayerPrefs.SetString("initials", name); PlayerPrefs.Save(); }
            EnteringInitials = false;
            stateTimer = 1.5f;   // play-again keys work a second later
            SoundBank.I.Play(Sfx.ExtraLife, 0.7f, 1f, 0f);
        }

        void Tick()
        {
            if (Paused) return;
            float dt = Time.deltaTime;
            stateTimer += dt;
            BannerAge += Time.unscaledDeltaTime;

            if (Logo && Logo.gameObject.activeSelf)
            {
                Logo.position = new Vector3(0, 10.4f + Mathf.Sin(Time.unscaledTime * 0.8f) * 0.25f, 12f);
                Logo.rotation = Quaternion.Euler(Mathf.Sin(Time.unscaledTime * 0.5f) * 5f, Mathf.Sin(Time.unscaledTime * 0.35f) * 12f, 0);
            }

            switch (State)
            {
                case GameState.Intro:
                    UpdateWorld(dt);
                    if ((BossWave ? boss.Alive : formation.Active) && stateTimer > 1f) State = GameState.Playing;
                    break;
                case GameState.Playing:
                    UpdateWorld(dt);
                    CheckWaveEnd();
                    break;
                case GameState.Dying:
                    UpdateWorld(dt);
                    if (stateTimer > 2.8f) AfterDeath();
                    break;
                case GameState.Cleared:
                    UpdateWorld(dt);
                    if (stateTimer > 2.6f) { Wave++; StartWave(); }
                    break;
                case GameState.GameOver:
                    UpdateWorld(dt);
                    if (Demo && stateTimer > 4f) NewGame(true);
                    break;
            }
        }

        void UpdateWorld(float dt)
        {
            UpdateShots(dt);
            UpdatePowerUps();

            // invaders grind through the shields
            if (formation.Alive > 0 && formation.FrontZ < 17f)
                foreach (var inv in formation.Invaders)
                {
                    if (!inv.Alive || inv.Diving) continue;
                    foreach (var b in bunkers) b.EraseBox(inv.transform.position, new Vector3(inv.HalfWidth, inv.HalfHeight, 0.8f));
                }

            if (State == GameState.Playing && !BossWave && formation.Active)
            {
                ufoTimer -= dt;
                if (ufoTimer <= 0f && !ufo.Active && formation.Alive > 6)
                {
                    ufo.Launch(formation.transform.position.z + 5f);
                    ufoTimer = Random.Range(18f, 28f);
                }
                if (formation.FrontZ < 3f) Invaded();
                DiverCollisions();
            }
        }

        /// <summary>A diving invader that reaches the fighter rams it (the shield wins).</summary>
        void DiverCollisions()
        {
            if (!player.Alive) return;
            Vector3 pp = player.transform.position;
            foreach (var inv in formation.Invaders)
            {
                if (!inv.Alive || inv.Dive != DiveState.Swoop) continue;
                if ((inv.transform.position - pp).sqrMagnitude > 1.3f * 1.3f) continue;
                if (player.ShieldUp)
                {
                    player.ShieldHit();
                    KillInvader(inv, inv.DiveVelocity);
                }
                else if (player.Invulnerable <= 0f)
                {
                    KillInvader(inv, inv.DiveVelocity);
                    PlayerHit();
                    return;
                }
            }
        }

        void CheckWaveEnd()
        {
            bool cleared = BossWave ? (bossSpawned && !boss.Alive && !boss.Dying && !boss.gameObject.activeSelf) : (formation.Total > 0 && formation.Alive == 0);
            if (!cleared) return;
            int bonus = 500 * Wave;
            AddScore(bonus, player.transform.position + new Vector3(0, 2f, 6f), new Color(1f, 0.85f, 0.3f), false);
            ShowBanner(BossWave ? "MOTHERSHIP DESTROYED" : "WAVE " + Wave + " CLEARED", "BONUS +" + bonus);
            SoundBank.I.Play(Sfx.ExtraLife, 0.6f, 0.8f, 0f);
            ufo.Hide();
            State = GameState.Cleared;
            stateTimer = 0f;
        }

        void PlayerHit()
        {
            if (State != GameState.Playing && State != GameState.Intro && State != GameState.Cleared) return;
            player.Die();
            SoundBank.I.Play(Sfx.PlayerDie, 1f, 1f, 0f);
            CameraRig.I.AddTrauma(1f);
            PostFx.I.Impact(1f);
            PostFx.I.Kick(0.6f);
            PostFx.I.SlowMo(0.25f, 1.3f);
            CameraRig.I.StartOrbit(player.transform.position);
            formation.Frozen = true;
            formation.RecallDivers();
            ClearEnemyShots();
            Lives--;
            State = GameState.Dying;
            stateTimer = 0f;
        }

        void AfterDeath()
        {
            if (Lives > 0)
            {
                player.Respawn();
                CameraRig.I.Mode = Demo ? CamMode.Title : CamMode.Follow;
                formation.Frozen = false;
                State = formation.Active || boss.Alive ? GameState.Playing : GameState.Intro;
                stateTimer = 0f;
            }
            else GameOver("YOUR FIGHTER WAS DESTROYED");
        }

        void Invaded()
        {
            if (player.Alive)
            {
                player.Die();
                SoundBank.I.Play(Sfx.PlayerDie, 1f, 0.8f, 0f);
            }
            CameraRig.I.AddTrauma(1f);
            PostFx.I.Kick(1f);
            formation.Frozen = true;
            Lives = 0;
            GameOver("THE INVADERS BROKE THROUGH");
        }

        void GameOver(string reason)
        {
            GameOverReason = reason;
            State = GameState.GameOver;
            stateTimer = 0f;
            formation.Frozen = true;
            ufo.Hide();
            CameraRig.I.StartOrbit(player.transform.position);
            HighScoreRow = -1;
            if (!Demo && HighScores.Qualifies(Score))
            {
                NewHiScore = Score > HiScore;
                PendingRank = HighScores.RankFor(Score);
                EnteringInitials = true;
                InitialsCursor = 0;
                string last = autoshot ? "AAA" : PlayerPrefs.GetString("initials", "AAA");
                for (int i = 0; i < 3; i++) Initials[i] = i < last.Length ? last[i] : 'A';
            }
            ShowBanner("GAME OVER", reason);
        }

        // ------------------------------------------------------------ scoring

        public void AddScore(int points, Vector3 at, Color c, bool popup = true)
        {
            if (popup) Fx.I.Popup(points.ToString(), at, c, points >= 1000 ? 1.6f : 1f);
            if (Demo) return;
            Score += points;
            if (Score >= nextExtra)
            {
                nextExtra += 10000;
                Lives = Mathf.Min(Lives + 1, 6);
                SoundBank.I.Play(Sfx.ExtraLife, 0.9f, 1f, 0f);
                Fx.I.Popup("1UP", player.transform.position + Vector3.up * 1.5f, new Color(0.4f, 1f, 0.5f), 1.6f);
            }
        }

        void KillInvader(Invader inv, Vector3 shotVel)
        {
            bool diver = inv.Diving;
            formation.Kill(inv, shotVel.normalized * 3f);
            formation.NotifyKill();
            if (PanicCharges < PanicMax && !Demo)
            {
                PanicMeter += 1f / KillsPerPanic;
                if (PanicMeter >= 1f)
                {
                    PanicMeter = 0f;
                    PanicCharges++;
                    Fx.I.Popup("PANIC READY", player.transform.position + new Vector3(0, 2.2f, 3f), new Color(1f, 0.55f, 0.2f), 1.2f);
                    SoundBank.I.Play(Sfx.PowerUp, 0.5f, 0.8f, 0f);
                }
            }
            if (Time.time - lastKill < 1.0f) Combo = Mathf.Min(Combo + 1, 8);
            else Combo = 1;
            lastKill = Time.time;
            Color c = inv.Kind == InvaderKind.Squid ? new Color(1f, 0.4f, 0.9f) : inv.Kind == InvaderKind.Crab ? new Color(0.4f, 0.9f, 1f) : new Color(0.5f, 1f, 0.55f);
            AddScore(inv.Points * Combo * (diver ? 2 : 1), inv.transform.position + Vector3.up * 0.6f, c);
            if (diver) Fx.I.Popup("DIVER x2", inv.transform.position + new Vector3(-0.9f, 1.6f, 0), c, 0.9f);
            if (Combo >= 3) Fx.I.Popup("x" + Combo, inv.transform.position + new Vector3(0.9f, 1.2f, 0), new Color(1f, 0.85f, 0.3f), 0.8f);
            CameraRig.I.AddTrauma(0.06f);
            if (powerups.Count < 2 && Random.value < 0.065f)
                powerups.Add(PowerUp.Create((PowerKind)Random.Range(0, 5), inv.transform.position));
        }

        // ------------------------------------------------------------ shots

        public int PlayerShotCount { get { int n = 0; foreach (var s in shots) if (s.live && s.player) n++; return n; } }
        public int EnemyShotCount { get { int n = 0; foreach (var s in shots) if (s.live && !s.player) n++; return n; } }

        public IEnumerable<Shot> EnemyShots()
        {
            foreach (var s in shots) if (s.live && !s.player) yield return s;
        }

        Shot Take()
        {
            foreach (var s in shots) if (!s.live) return s;
            var lib = OrbitalLibrary.I;
            var go = new GameObject("Shot");
            go.transform.SetParent(world, false);
            go.AddComponent<MeshFilter>().sharedMesh = lib.capsule;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = 3f; l.shadows = LightShadows.None;
            // a glowing streak behind player missiles so they stay readable far down the lane
            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = lib.shotTrail;
            tr.time = 0.18f;
            tr.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.emitting = false;
            var shot = new Shot { t = go.transform, r = r, light = l, trail = tr };
            shots.Add(shot);
            return shot;
        }

        public void FirePlayer(Vector3 at, Vector3 dir, bool pierce)
        {
            var s = Take();
            s.live = true; s.player = true; s.pierce = pierce; s.kind = 0; s.age = 0;
            s.pos = s.prev = at;
            s.vel = dir.normalized * (pierce ? 70f : 60f);
            s.r.sharedMaterial = OrbitalLibrary.I.playerShot;
            s.t.gameObject.SetActive(true);
            s.t.localScale = pierce ? new Vector3(0.38f, 0.38f, 3.4f) : new Vector3(0.26f, 0.26f, 2.6f);
            s.t.position = at;
            s.t.rotation = Quaternion.LookRotation(s.vel);
            Color c = pierce ? new Color(0.7f, 3.0f, 0.8f, 1f) : new Color(3.2f, 2.6f, 0.35f, 1f);
            shotBlock.SetColor("_Color", c);
            s.r.SetPropertyBlock(shotBlock);
            s.trail.Clear();
            s.trail.emitting = true;
            s.trail.startColor = pierce ? new Color(0.5f, 2.2f, 0.6f, 0.9f) : new Color(2.4f, 1.6f, 0.2f, 0.9f);
            s.trail.endColor = pierce ? new Color(0.3f, 1f, 0.3f, 0f) : new Color(1.6f, 0.6f, 0.1f, 0f);
            s.trail.widthMultiplier = pierce ? 0.5f : 0.36f;
            s.light.enabled = PlayerShotCount <= 8;
            s.light.color = pierce ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.8f, 0.3f);
            s.light.intensity = 3f;
        }

        public void FireEnemy(Vector3 at, Vector3 vel, int kind)
        {
            var s = Take();
            s.live = true; s.player = false; s.pierce = false; s.kind = kind; s.age = 0;
            s.pos = s.prev = at;
            s.vel = vel;
            s.r.sharedMaterial = OrbitalLibrary.I.enemyShot;
            s.trail.emitting = false;
            s.t.gameObject.SetActive(true);
            s.t.localScale = kind == 1 ? new Vector3(0.55f, 0.55f, 0.55f) : new Vector3(0.3f, 0.3f, 1.1f);
            s.t.position = at;
            Color c = kind == 1 ? new Color(2.6f, 0.35f, 1.6f) : kind == 2 ? new Color(2.8f, 1.2f, 0.25f) : new Color(2.8f, 0.45f, 0.2f);
            shotBlock.SetColor("_Color", c);
            s.r.SetPropertyBlock(shotBlock);
            s.light.enabled = EnemyShotCount <= 8;
            s.light.color = c / 2.8f;
            s.light.intensity = 2.5f;
            if (Random.value < 0.6f) SoundBank.I.Play(Sfx.EnemyShot, 0.35f);
        }

        void Kill(Shot s)
        {
            s.live = false;
            s.trail.Clear();
            s.t.gameObject.SetActive(false);
        }

        public void ClearEnemyShots()
        {
            foreach (var s in shots) if (s.live && !s.player) Kill(s);
        }

        void ClearAllShots()
        {
            foreach (var s in shots) if (s.live) Kill(s);
        }

        void UpdateShots(float dt)
        {
            for (int i = 0; i < shots.Count; i++)
            {
                var s = shots[i];
                if (!s.live) continue;
                s.age += dt;
                s.prev = s.pos;
                s.pos += s.vel * dt;
                if (!s.player && s.kind == 2) s.pos.x += Mathf.Cos(s.age * 14f) * dt * 4f; // the classic squiggle
                s.t.position = s.pos;
                if (s.player || s.kind != 1) s.t.rotation = Quaternion.LookRotation(s.vel) * Quaternion.Euler(0, 0, s.age * 720f);

                if (s.player) PlayerShotHits(s);
                else EnemyShotHits(s);
            }
        }

        void PlayerShotHits(Shot s)
        {
            if (s.pos.z > 130f || s.pos.z < -15f || Mathf.Abs(s.pos.x) > 40f || Mathf.Abs(s.pos.y) > 30f) { Kill(s); return; }
            foreach (var b in bunkers)
                if (b.TryHit(s.prev, s.pos, 0.12f, false)) { Kill(s); return; }

            // shoot down enemy bullets
            foreach (var e in shots)
            {
                if (!e.live || e.player) continue;
                if (Mathf.Abs(e.pos.z - s.pos.z) < 1.2f && ((Vector2)(e.pos - s.pos)).magnitude < 0.35f)
                {
                    Fx.I.Sparks(e.pos, new Color(1f, 0.7f, 0.4f), 12, 4f);
                    Kill(e);
                    if (!s.pierce) { Kill(s); return; }
                }
            }

            if (ufo.Active)
            {
                Vector3 u = ufo.transform.position;
                if (Crosses(s, u.z, 0.8f, out var p) && Mathf.Abs(p.x - u.x) < ufo.Half.x && Mathf.Abs(p.y - u.y) < ufo.Half.y)
                {
                    int pts = ufo.ShootDown();
                    CameraRig.I.AddTrauma(0.3f);
                    PostFx.I.Impact(0.4f);
                    AddScore(pts, u, new Color(1f, 0.4f, 0.8f), false);
                    if (!s.pierce) { Kill(s); return; }
                }
            }

            if (boss.Alive && boss.TryHit(s.prev, s.pos, s.pierce ? 2.5f : 1f, out var bp))
            {
                Kill(s);
                return;
            }

            foreach (var inv in formation.Invaders)
            {
                if (!inv.Alive) continue;
                Vector3 ip = inv.transform.position;
                if (!Crosses(s, ip.z, 0.45f, out var p)) continue;
                if (Mathf.Abs(p.x - ip.x) < inv.HalfWidth && Mathf.Abs(p.y - ip.y) < inv.HalfHeight)
                {
                    KillInvader(inv, s.vel);
                    if (!s.pierce) { Kill(s); return; }
                }
            }
        }

        void EnemyShotHits(Shot s)
        {
            if (s.pos.z < -12f || s.pos.z > 140f || Mathf.Abs(s.pos.x) > 30f || Mathf.Abs(s.pos.y) > 20f) { Kill(s); return; }
            foreach (var b in bunkers)
                if (b.TryHit(s.prev, s.pos, 0.15f, true)) { Kill(s); return; }
            if (!player.Alive) return;
            Vector3 pp = player.transform.position;
            if (!Crosses(s, pp.z, 0.6f, out var p)) return;
            float d = ((Vector2)(p - pp)).magnitude;
            if (player.ShieldUp && d < 1.8f)
            {
                player.ShieldHit();
                Fx.I.Sparks(p, new Color(0.4f, 0.7f, 1f), 16, 5f);
                Kill(s);
                return;
            }
            if (d < Player.HitRadius && player.Invulnerable <= 0f)
            {
                Kill(s);
                PlayerHit();
            }
        }

        static bool Crosses(Shot s, float z, float slack, out Vector3 p)
        {
            p = s.pos;
            float lo = Mathf.Min(s.prev.z, s.pos.z) - slack, hi = Mathf.Max(s.prev.z, s.pos.z) + slack;
            if (z < lo || z > hi) return false;
            float dz = s.pos.z - s.prev.z;
            float t = Mathf.Abs(dz) > 1e-5f ? Mathf.Clamp01((z - s.prev.z) / dz) : 1f;
            p = Vector3.Lerp(s.prev, s.pos, t);
            return true;
        }

        // ------------------------------------------------------------ power-ups

        void UpdatePowerUps()
        {
            for (int i = powerups.Count - 1; i >= 0; i--)
            {
                var p = powerups[i];
                if (!p) { powerups.RemoveAt(i); continue; }
                if (!player.Alive) continue;
                Vector3 d = p.transform.position - player.transform.position;
                if (Mathf.Abs(d.z) < 1.3f && ((Vector2)d).magnitude < 1.6f)
                {
                    Apply(p.Kind);
                    Fx.I.Sparks(p.transform.position, p.Colour, 30, 5f);
                    Fx.I.Popup(PowerUp.Names[(int)p.Kind], player.transform.position + new Vector3(0, 1.6f, 2f), p.Colour, 1.1f);
                    Destroy(p.gameObject);
                    powerups.RemoveAt(i);
                }
            }
        }

        void Apply(PowerKind k)
        {
            SoundBank.I.Play(Sfx.PowerUp, 0.9f, 1f, 0f);
            if (k == PowerKind.Bomb)
            {
                SmartBomb();
                return;
            }
            player.PowerTime[(int)k] = k == PowerKind.Shield ? 12f : 10f;
        }

        /// <summary>Panic fire: a storm of piercing bolts in every direction, clears nearby bullets.</summary>
        public bool TryPanic()
        {
            if (PanicCharges <= 0 || !player.Alive || !ControlsEnabled) return false;
            PanicCharges--;
            Vector3 at = player.transform.position + Vector3.forward * 0.6f;
            // forward storm: nested cones toward the invaders
            float[] cone = { 0f, 5f, 11f, 19f, 30f };
            int[] count = { 1, 8, 12, 16, 20 };
            for (int c = 0; c < cone.Length; c++)
                for (int i = 0; i < count[c]; i++)
                {
                    float around = i * 360f / count[c] + c * 9f;
                    Vector3 d = Quaternion.AngleAxis(around, Vector3.forward) * (Quaternion.AngleAxis(cone[c], Vector3.up) * Vector3.forward);
                    FirePlayer(at, d, true);
                }
            // and a ring all round the ship for anything diving in close
            for (int i = 0; i < 24; i++)
                FirePlayer(at, Quaternion.AngleAxis(i * 15f, Vector3.forward) * Vector3.right, true);
            // nearby enemy bullets are vaporised
            foreach (var e in shots)
                if (e.live && !e.player && (e.pos - at).sqrMagnitude < 14f * 14f)
                {
                    Fx.I.Sparks(e.pos, new Color(1f, 0.7f, 0.3f), 8, 4f);
                    Kill(e);
                }
            player.Invulnerable = Mathf.Max(player.Invulnerable, 1.2f);
            Fx.I.Shockwave(at, 16f, new Color(1f, 0.8f, 0.3f), 0.5f);
            Fx.I.Flash(at, new Color(1f, 0.8f, 0.4f), 14f, 8f, 0.3f);
            Fx.I.Popup("PANIC FIRE!", at + new Vector3(0, 2.4f, 2f), new Color(1f, 0.55f, 0.2f), 1.1f);
            SoundBank.I.Play(Sfx.Bomb, 0.7f, 1.4f, 0f);
            SoundBank.I.Play(Sfx.LaserHeavy, 1f, 0.7f, 0f);
            CameraRig.I.AddTrauma(0.5f);
            PostFx.I.Kick(0.7f);
            return true;
        }

        void SmartBomb()
        {
            SoundBank.I.Play(Sfx.Bomb, 1f, 1f, 0f);
            CameraRig.I.AddTrauma(0.8f);
            PostFx.I.Kick(1f);
            PostFx.I.Impact(0.8f);
            ClearEnemyShots();
            Vector3 centre = player.transform.position + Vector3.forward * 20f;
            Fx.I.Shockwave(player.transform.position + Vector3.forward * 4f, 60f, new Color(1f, 0.4f, 0.3f), 1.0f);
            // wipe out the nearest invaders
            var targets = new List<Invader>();
            foreach (var inv in formation.Invaders) if (inv.Alive) targets.Add(inv);
            targets.Sort((a, b) => a.transform.position.z.CompareTo(b.transform.position.z));
            for (int i = 0; i < Mathf.Min(14, targets.Count); i++) KillInvader(targets[i], Vector3.forward);
            if (boss.Alive) boss.TryHit(centre - Vector3.forward * 60f, boss.transform.position + Vector3.forward, 25f, out _);
        }

        // ------------------------------------------------------------ autopilot support

        public Vector2 AutoTarget(Vector2 me)
        {
            if (boss.Alive)
            {
                Vector3 aim = boss.AimPoint;
                return new Vector2(aim.x + Mathf.Sin(Time.time * 1.7f) * 1.2f, Mathf.Clamp(aim.y, Player.MinY, Player.MaxY));
            }
            if (ufo.Active && Mathf.Abs(ufo.transform.position.x) < 8f)
                return new Vector2(ufo.transform.position.x + Mathf.Sign(ufo.transform.position.x) * 0f, Mathf.Min(ufo.transform.position.y, Player.MaxY));
            float front = formation.FrontZ;
            Invader best = null;
            float bestScore = float.MaxValue;
            foreach (var inv in formation.Invaders)
            {
                if (!inv.Alive) continue;
                Vector3 p = inv.transform.position;
                float score = Vector2.Distance(me, p) + (p.z - front) * 0.6f;
                if (score < bestScore) { bestScore = score; best = inv; }
            }
            if (!best) return new Vector2(0, 0);
            Vector3 b = best.transform.position;
            return new Vector2(b.x, Mathf.Clamp(b.y, Player.MinY, Player.MaxY));
        }

        public bool WouldHit(Vector3 aim)
        {
            foreach (var inv in formation.Invaders)
            {
                if (!inv.Alive) continue;
                Vector3 p = inv.transform.position;
                if (Mathf.Abs(p.x - aim.x) < inv.HalfWidth && Mathf.Abs(p.y - aim.y) < inv.HalfHeight) return true;
            }
            if (boss.Alive)
            {
                Vector3 c = boss.transform.position;
                if (Mathf.Abs(c.x - aim.x) < boss.Half.x && Mathf.Abs(c.y - aim.y) < boss.Half.y) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ automated screenshots

        void Capture(string name)
        {
            Directory.CreateDirectory(autoshotDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(autoshotDir, name + ".png"));
        }

        IEnumerator Snap(string name)
        {
            yield return new WaitForEndOfFrame();
            Capture(name);
        }

        IEnumerator AutoShot()
        {
            Debug.Log("ORBITAL_AUTOSHOT_BEGIN");
            NewGame(true);
            yield return new WaitForSecondsRealtime(4f);
            yield return Snap("00_title");
            NewGame(false);
            player.AutoPilot = true;
            Lives = 99;
            yield return new WaitForSecondsRealtime(0.9f);
            yield return Snap("01_warp");
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Snap("02_wave1");
            // a held-fire burst, photographed mid-flight
            for (int i = 0; i < 10; i++)
            {
                Vector3 m = player.transform.position + new Vector3(0, 0.05f, 1.6f);
                FirePlayer(m + Vector3.left * 0.35f, Vector3.forward, false);
                FirePlayer(m + Vector3.right * 0.35f, Vector3.forward, false);
                yield return new WaitForSeconds(0.12f);
            }
            yield return Snap("02_volley");
            ufo.Launch(formation.transform.position.z + 5f);
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSecondsRealtime(2f);
                yield return Snap($"03_play{i}");
            }
            player.PowerTime[(int)PowerKind.Spread] = 10f;
            player.PowerTime[(int)PowerKind.Shield] = 10f;
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Snap("04_powered");
            powerups.Add(PowerUp.Create(PowerKind.Rapid, player.transform.position + new Vector3(2f, 1f, 14f)));
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Snap("05_powerup");
            // jump through layouts
            foreach (int w in new[] { 2, 3, 4 })
            {
                Wave = w;
                StartWave();
                yield return new WaitForSecondsRealtime(3.5f);
                yield return Snap($"06_wave{w}");
            }
            // divers: send three and catch them mid-swoop
            player.PowerTime[(int)PowerKind.Shield] = 10f;
            for (int i = 0; i < 3; i++) { formation.TryDive(true); yield return new WaitForSecondsRealtime(0.35f); }
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Snap("06_dive_a");
            yield return new WaitForSecondsRealtime(0.7f);
            yield return Snap("06_dive_b");
            Debug.Log($"ORBITAL_DIVERS swooping={formation.Divers}");
            yield return new WaitForSecondsRealtime(3.5f);
            Debug.Log($"ORBITAL_DIVERS_AFTER swooping={formation.Divers} alive={formation.Alive}");
            // panic fire, then a forced regroup
            bool panicked = TryPanic();
            yield return new WaitForSecondsRealtime(0.22f);
            yield return Snap("06_panic_a");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Snap("06_panic_b");
            Debug.Log($"ORBITAL_PANIC fired={panicked} charges={PanicCharges} alive={formation.Alive}");
            for (int shape = 0; shape < 4; shape++)
            {
                formation.ForceRegroup(shape);
                yield return new WaitForSecondsRealtime(0.8f);
                if (shape == 0) yield return Snap("06_regroup_mid");
                yield return new WaitForSecondsRealtime(2.2f);
                yield return Snap($"06_regroup{shape}");
            }
            Debug.Log($"ORBITAL_REGROUP regroups={formation.Regroups} regrouping={formation.Regrouping} alive={formation.Alive}");
            // a death
            player.PowerTime[(int)PowerKind.Shield] = 0f;
            player.Invulnerable = 0f;
            PlayerHit();
            yield return new WaitForSecondsRealtime(0.25f);
            yield return Snap("07_death_a");
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Snap("07_death_b");
            yield return new WaitForSecondsRealtime(3.5f);
            // boss
            Wave = 5;
            StartWave();
            yield return new WaitForSecondsRealtime(2.0f);
            yield return Snap("08_boss_arrive");
            yield return new WaitForSecondsRealtime(3.5f);
            yield return Snap("09_boss_fight");
            boss.Health = 1f;
            boss.TryHit(new Vector3(boss.transform.position.x, boss.transform.position.y, 0), boss.transform.position + Vector3.forward * 3f, 1000f, out _);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Snap("10_boss_dying");
            while (boss.Dying) yield return null;   // the slow-mo death runs ~5s of real time
            yield return new WaitForSecondsRealtime(0.15f);
            yield return Snap("10_boss_death");
            yield return new WaitForSecondsRealtime(2f);
            yield return Snap("11_after");
            Debug.Log($"ORBITAL_BOSS_CLEARED wave={Wave} state={State}");
            Lives = 0;
            GameOver("TEST RUN COMPLETE");
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Snap("12_gameover");
            Debug.Log($"ORBITAL_HISCORE entering={EnteringInitials} rank={PendingRank} score={Score}");
            foreach (char c in "QA!") { Initials[InitialsCursor] = c; if (InitialsCursor < 2) InitialsCursor++; yield return new WaitForSecondsRealtime(0.2f); }
            yield return Snap("12_initials");
            SubmitInitials();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Snap("13_table");
            Debug.Log($"ORBITAL_HISCORE saved row={HighScoreRow} top={HighScores.Entries[0].name}:{HighScores.Entries[0].score}");
            Debug.Log($"ORBITAL_AUTOSHOT_DONE score={Score}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
