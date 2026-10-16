using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital
{
    public enum PowerKind { Spread = 0, Rapid = 1, Pierce = 2, Shield = 3, Bomb = 4 }

    /// <summary>The player's fighter: flies in the X/Y plane at Z = 0 and fires down +Z.</summary>
    public class Player : MonoBehaviour
    {
        public static Player I { get; private set; }

        public const float MinX = -13f, MaxX = 13f, MinY = -4.6f, MaxY = 7f;
        public const float HitRadius = 0.55f;

        public bool AutoPilot;
        public bool Alive { get; private set; } = true;
        public float Invulnerable;
        public float Bank { get; private set; }
        public readonly float[] PowerTime = new float[5];
        public bool ShieldUp => PowerTime[(int)PowerKind.Shield] > 0f;

        Vector3 vel;
        float cooldown;
        Transform model;
        MeshRenderer hull;
        MaterialPropertyBlock block;
        GameObject shieldBubble;
        MaterialPropertyBlock shieldBlock;
        Transform reticle;
        MeshRenderer reticleRenderer;
        MaterialPropertyBlock reticleBlock;
        LineRenderer sight;
        ParticleSystem exhaust;
        Transform[] flames = new Transform[2];
        float flash;
        float hitShieldFlash;

        void Awake()
        {
            I = this;
            var lib = OrbitalLibrary.I;
            model = new GameObject("Ship").transform;
            model.SetParent(transform, false);
            model.localScale = Vector3.one * 0.8f;
            model.gameObject.AddComponent<MeshFilter>().sharedMesh = lib.playerMesh;
            hull = model.gameObject.AddComponent<MeshRenderer>();
            hull.sharedMaterial = lib.playerHull;
            block = new MaterialPropertyBlock();

            // engine flames
            for (int i = 0; i < 2; i++)
            {
                var f = new GameObject("Flame").transform;
                f.SetParent(model, false);
                f.localPosition = new Vector3(i == 0 ? -0.42f : 0.42f, -0.02f, -1.62f);
                f.gameObject.AddComponent<MeshFilter>().sharedMesh = lib.sphere;
                var mr = f.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = lib.engine;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                flames[i] = f;
            }
            var light = new GameObject("EngineLight").AddComponent<Light>();
            light.transform.SetParent(model, false);
            light.transform.localPosition = new Vector3(0, 0, -2.2f);
            light.type = LightType.Point; light.color = new Color(0.4f, 0.7f, 1f); light.range = 5f; light.intensity = 6f; light.shadows = LightShadows.None;

            // exhaust particles
            var ex = new GameObject("Exhaust");
            ex.transform.SetParent(model, false);
            ex.transform.localPosition = new Vector3(0, 0, -1.9f);
            exhaust = ex.AddComponent<ParticleSystem>();
            exhaust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = exhaust.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startColor = new Color(0.5f, 0.8f, 1f);
            main.maxParticles = 300;
            var em = exhaust.emission; em.rateOverTime = 90;
            var shape = exhaust.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 6f; shape.radius = 0.25f;
            shape.rotation = new Vector3(0, 180, 0);
            var col = exhaust.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.2f, 0.4f, 1f), 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sol = exhaust.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
            var pr = ex.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = lib.ember;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            exhaust.Play();

            // wingtip trails
            for (int s = -1; s <= 1; s += 2)
            {
                var tipGo = new GameObject("Wingtip");
                tipGo.transform.SetParent(model, false);
                tipGo.transform.localPosition = new Vector3(1.55f * s, -0.05f, -0.75f);
                var tr = tipGo.AddComponent<TrailRenderer>();
                tr.sharedMaterial = lib.streak;
                tr.time = 0.35f;
                tr.widthMultiplier = 0.06f;
                tr.startColor = new Color(0.6f, 0.85f, 1f, 0.8f);
                tr.endColor = new Color(0.6f, 0.85f, 1f, 0f);
                tr.shadowCastingMode = ShadowCastingMode.Off;
            }

            // shield bubble
            shieldBubble = new GameObject("Shield");
            shieldBubble.transform.SetParent(transform, false);
            shieldBubble.transform.localScale = new Vector3(3.6f, 1.8f, 3.6f);
            shieldBubble.AddComponent<MeshFilter>().sharedMesh = lib.sphere;
            var sr = shieldBubble.AddComponent<MeshRenderer>();
            sr.sharedMaterial = lib.shield;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            shieldBlock = new MaterialPropertyBlock();
            shieldBubble.SetActive(false);

            // targeting reticle projected onto the formation, plus a faint sight line
            reticle = new GameObject("Reticle").transform;
            reticle.gameObject.AddComponent<MeshFilter>().sharedMesh = lib.quad;
            reticleRenderer = reticle.gameObject.AddComponent<MeshRenderer>();
            reticleRenderer.sharedMaterial = lib.reticle;
            reticleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            reticleBlock = new MaterialPropertyBlock();
            sight = gameObject.AddComponent<LineRenderer>();
            sight.sharedMaterial = lib.streak;
            sight.positionCount = 2;
            sight.widthMultiplier = 0.025f;
            sight.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void ResetForGame()
        {
            for (int i = 0; i < PowerTime.Length; i++) PowerTime[i] = 0f;
            Respawn();
        }

        public void Respawn()
        {
            Alive = true;
            gameObject.SetActive(true);
            transform.position = new Vector3(0, -1f, 0);
            vel = Vector3.zero;
            Invulnerable = 2.5f;
            model.gameObject.SetActive(true);
            exhaust.Play();
        }

        public void Die()
        {
            Alive = false;
            model.gameObject.SetActive(false);
            shieldBubble.SetActive(false);
            reticle.gameObject.SetActive(false);
            sight.enabled = false;
            exhaust.Stop();
            Fx.I.Explosion(transform.position, 2.2f, new Color(1f, 0.6f, 0.25f));
            Fx.I.Cubes(transform.position, new Color(0.85f, 0.88f, 0.95f, 0.2f), 70, 0.14f, 7f, 2.5f);
            Fx.I.Cubes(transform.position, new Color(0.3f, 0.7f, 1f, 1f), 30, 0.1f, 9f, 2f);
        }

        public void ShieldHit()
        {
            hitShieldFlash = 1f;
            SoundBank.I.Play(Sfx.ShieldHit, 0.8f);
        }

        public void HitFlash() => flash = 1f;

        void Update()
        {
            if (!Alive) return;
            var game = Game.I;
            float dt = Time.deltaTime;

            Vector2 input;
            bool fire;
            if (AutoPilot) AutoPilotInput(out input, out fire);
            else
            {
                input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                if (input.sqrMagnitude > 1f) input.Normalize();
                fire = Input.GetKey(KeyCode.Space) || Input.GetButton("Fire1") || Input.GetKey(KeyCode.Return);
            }
            if (!game.ControlsEnabled) { input = Vector2.zero; fire = false; }
            else if (!AutoPilot && (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift) || Input.GetKeyDown(KeyCode.B)
                     || Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetKeyDown(KeyCode.JoystickButton3)))
                game.TryPanic();

            // inertial movement
            Vector3 want = new Vector3(input.x, input.y, 0) * 13f;
            vel = Vector3.MoveTowards(vel, want, dt * (input.sqrMagnitude > 0.01f ? 60f : 38f));
            Vector3 p = transform.position + vel * dt;
            if (p.x < MinX || p.x > MaxX) vel.x = 0;
            if (p.y < MinY || p.y > MaxY) vel.y = 0;
            p.x = Mathf.Clamp(p.x, MinX, MaxX);
            p.y = Mathf.Clamp(p.y, MinY, MaxY);
            p.z = 0;
            transform.position = p;

            Bank = Mathf.Lerp(Bank, -vel.x * 3.2f, 1f - Mathf.Exp(-dt * 10f));
            float pitch = Mathf.Lerp(0, -vel.y * 2f, 0.8f);
            model.localRotation = Quaternion.Euler(pitch, vel.x * 0.6f, Bank);

            // weapons
            for (int i = 0; i < PowerTime.Length; i++) PowerTime[i] = Mathf.Max(0f, PowerTime[i] - dt);
            cooldown -= dt;
            if (fire && cooldown <= 0f && game.ControlsEnabled)
            {
                bool rapid = PowerTime[(int)PowerKind.Rapid] > 0f;
                bool spread = PowerTime[(int)PowerKind.Spread] > 0f;
                bool pierce = PowerTime[(int)PowerKind.Pierce] > 0f;
                // holding fire is always automatic; the Rapid power-up makes it a bullet hose
                int maxShots = rapid ? 40 : spread ? 28 : 18;
                if (game.PlayerShotCount < maxShots)
                {
                    cooldown = rapid ? 0.055f : 0.12f;
                    Vector3 muzzle = transform.position + new Vector3(0, 0.05f, 1.6f);
                    game.FirePlayer(muzzle + Vector3.left * 0.35f, Vector3.forward, pierce);
                    game.FirePlayer(muzzle + Vector3.right * 0.35f, Vector3.forward, pierce);
                    if (spread)
                    {
                        game.FirePlayer(muzzle, Quaternion.Euler(0, -7f, 0) * Vector3.forward, pierce);
                        game.FirePlayer(muzzle, Quaternion.Euler(0, 7f, 0) * Vector3.forward, pierce);
                    }
                    SoundBank.I.Play(pierce ? Sfx.LaserHeavy : Sfx.Laser, rapid ? 0.35f : 0.5f, rapid ? 1.2f : 1f);
                    Fx.I.Sparks(muzzle, new Color(1f, 0.85f, 0.3f), 4, 3f, Vector3.forward, 0.4f);
                }
            }

            // flames flicker with thrust
            float thrust = 1f + vel.magnitude * 0.03f;
            for (int i = 0; i < 2; i++)
            {
                float fl = Random.Range(0.85f, 1.15f) * thrust;
                flames[i].localScale = new Vector3(0.32f, 0.32f, 1.1f * fl);
                flames[i].localPosition = new Vector3(flames[i].localPosition.x, -0.02f, -1.62f - 0.25f * fl);
            }

            // invulnerability blink + hit flash
            if (Invulnerable > 0f) Invulnerable -= dt;
            bool blinkOff = Invulnerable > 0f && Mathf.Repeat(Time.time * 10f, 1f) < 0.35f;
            hull.enabled = !blinkOff;
            flash = Mathf.MoveTowards(flash, 0f, dt * 5f);
            block.SetFloat("_Flash", flash);
            hull.SetPropertyBlock(block);

            // shield bubble
            shieldBubble.SetActive(ShieldUp);
            if (ShieldUp)
            {
                hitShieldFlash = Mathf.MoveTowards(hitShieldFlash, 0f, dt * 3f);
                float ending = PowerTime[(int)PowerKind.Shield] < 2f && Mathf.Repeat(Time.time * 6f, 1f) < 0.5f ? 0.3f : 1f;
                shieldBlock.SetColor("_Color", new Color(0.25f, 0.6f, 1.6f) * (0.7f + 2.5f * hitShieldFlash) * ending);
                shieldBubble.GetComponent<MeshRenderer>().SetPropertyBlock(shieldBlock);
                shieldBubble.transform.localRotation = Quaternion.Euler(0, Time.time * 40f, 0);
            }

            UpdateReticle();
        }

        void UpdateReticle()
        {
            var f = Formation.I;
            bool show = f && f.Active && f.Alive > 0;
            float z = show ? f.FrontZ : 40f;
            if (Boss.I && Boss.I.Alive) { show = true; z = Boss.I.transform.position.z - 2f; }
            reticle.gameObject.SetActive(show);
            sight.enabled = show;
            if (!show) return;
            Vector3 aim = new Vector3(transform.position.x, transform.position.y + 0.05f, z);
            reticle.position = aim;
            var cam = CameraRig.I.transform;
            reticle.rotation = Quaternion.LookRotation(aim - cam.position) * Quaternion.Euler(90, 0, 0);
            float onTarget = Game.I.WouldHit(aim) ? 1f : 0f;
            reticle.localScale = Vector3.one * (1.4f + 0.25f * Mathf.Sin(Time.time * 8f) * onTarget);
            reticleBlock.SetColor("_Color", onTarget > 0 ? new Color(2.5f, 0.6f, 0.3f) : new Color(0.4f, 0.9f, 1.6f) * 0.6f);
            reticleRenderer.SetPropertyBlock(reticleBlock);
            sight.SetPosition(0, transform.position + new Vector3(0, 0.05f, 1.6f));
            sight.SetPosition(1, aim);
            Color sc = onTarget > 0 ? new Color(1.5f, 0.4f, 0.2f, 0.35f) : new Color(0.3f, 0.7f, 1.2f, 0.18f);
            sight.startColor = sc;
            sight.endColor = new Color(sc.r, sc.g, sc.b, 0f);
        }

        // ------------------------------------------------------------ demo autopilot

        float dodgeSign = 1f;
        float retarget;
        Vector2 targetXY;

        void AutoPilotInput(out Vector2 input, out bool fire)
        {
            var game = Game.I;
            Vector2 me = transform.position;
            retarget -= Time.deltaTime;
            if (retarget <= 0f)
            {
                retarget = 0.35f;
                targetXY = game.AutoTarget(me);
            }
            Vector2 to = targetXY - me;

            // dodge incoming fire
            Vector2 dodge = Vector2.zero;
            foreach (var s in game.EnemyShots())
            {
                if (s.pos.z < -1f || s.vel.z >= 0f) continue;
                float tHit = s.pos.z / -s.vel.z;
                if (tHit > 1.1f) continue;
                Vector2 pred = (Vector2)s.pos + (Vector2)s.vel * tHit;
                Vector2 away = me - pred;
                if (away.magnitude < 1.6f)
                {
                    if (away.sqrMagnitude < 0.01f) away = new Vector2(dodgeSign, 0);
                    dodge += away.normalized * (1.6f - away.magnitude) * 3f;
                }
            }
            if (dodge.sqrMagnitude > 0.01f)
            {
                input = Vector2.ClampMagnitude(dodge, 1f);
                if (Mathf.Abs(me.x) > 9f) dodgeSign = -Mathf.Sign(me.x);
            }
            else input = Vector2.ClampMagnitude(to * 1.5f, 1f);
            fire = Mathf.Abs(to.x) < 1.6f && Mathf.Abs(to.y) < 1.2f;
        }
    }
}
