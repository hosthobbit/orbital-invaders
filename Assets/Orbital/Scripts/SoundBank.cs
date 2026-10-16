using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbital
{
    public enum Sfx
    {
        March0, March1, March2, March3,
        Laser, LaserHeavy, EnemyShot, InvaderDie, BigBoom, PlayerDie,
        BunkerHit, PowerUp, ShieldHit, WaveStart, Warp, BossAlarm, ExtraLife, Bomb, UfoDie, Select, Dive,
    }

    /// <summary>All audio is synthesised at startup.</summary>
    public class SoundBank : MonoBehaviour
    {
        public static SoundBank I { get; private set; }
        const int Rate = 44100;

        readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource music, ufoLoop;
        readonly System.Random rng = new System.Random(11);

        public float MusicVolume = 0.5f;
        public float SfxVolume = 1f;

        void Awake()
        {
            I = this;
            for (int i = 0; i < 28; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                pool.Add(s);
            }
            float[] march = { 61.7f, 55.0f, 49.0f, 46.2f };
            for (int i = 0; i < 4; i++) { float f = march[i]; clips[Sfx.March0 + i] = Make("march" + i, 0.22f, d => MarchNote(d, f)); }
            clips[Sfx.Laser] = Make("laser", 0.28f, d => Zap(d, 1900f, 380f, 0.28f, 0.5f));
            clips[Sfx.LaserHeavy] = Make("laserHeavy", 0.45f, d => Zap(d, 1100f, 140f, 0.45f, 0.9f));
            clips[Sfx.EnemyShot] = Make("enemyShot", 0.3f, d => Zap(d, 520f, 1200f, 0.3f, 0.35f));
            clips[Sfx.InvaderDie] = Make("invaderDie", 0.5f, InvaderDie);
            clips[Sfx.BigBoom] = Make("bigBoom", 2.2f, d => Boom(d, 2.2f, 1.0f));
            clips[Sfx.PlayerDie] = Make("playerDie", 2.5f, PlayerDie);
            clips[Sfx.BunkerHit] = Make("bunkerHit", 0.25f, BunkerHit);
            clips[Sfx.PowerUp] = Make("powerUp", 0.7f, PowerUpSound);
            clips[Sfx.ShieldHit] = Make("shieldHit", 0.5f, ShieldHit);
            clips[Sfx.WaveStart] = Make("waveStart", 1.6f, WaveStart);
            clips[Sfx.Warp] = Make("warp", 1.4f, Warp);
            clips[Sfx.BossAlarm] = Make("bossAlarm", 2.4f, BossAlarm);
            clips[Sfx.ExtraLife] = Make("extraLife", 1.0f, ExtraLife);
            clips[Sfx.Bomb] = Make("bomb", 2.4f, d => Boom(d, 2.4f, 1.4f));
            clips[Sfx.UfoDie] = Make("ufoDie", 1.4f, UfoDie);
            clips[Sfx.Select] = Make("select", 0.2f, d => Zap(d, 880f, 1320f, 0.2f, 0.3f));
            clips[Sfx.Dive] = Make("dive", 1.1f, DiveWhistle);

            ufoLoop = gameObject.AddComponent<AudioSource>();
            ufoLoop.clip = Make("ufo", 1.2f, UfoSiren);
            ufoLoop.loop = true;
            ufoLoop.volume = 0f;

            music = gameObject.AddComponent<AudioSource>();
            music.clip = Make("music", 32f, Music);
            music.loop = true;
            music.volume = MusicVolume;
            music.Play();
        }

        void Update() { music.volume = MusicVolume; }

        public void Play(Sfx s, float volume = 1f, float pitch = 1f, float jitter = 0.05f)
        {
            if (!clips.TryGetValue(s, out var clip)) return;
            foreach (var src in pool)
            {
                if (src.isPlaying) continue;
                src.clip = clip;
                src.volume = volume * SfxVolume;
                src.pitch = pitch * (1f + UnityEngine.Random.Range(-jitter, jitter));
                src.Play();
                return;
            }
        }

        public void SetUfo(bool on, float pan = 0f)
        {
            if (on && !ufoLoop.isPlaying) ufoLoop.Play();
            ufoLoop.volume = Mathf.MoveTowards(ufoLoop.volume, on ? 0.45f * SfxVolume : 0f, Time.unscaledDeltaTime * 2f);
            ufoLoop.panStereo = pan;
            if (!on && ufoLoop.volume <= 0.001f && ufoLoop.isPlaying) ufoLoop.Stop();
        }

        // ------------------------------------------------------------ synthesis

        AudioClip Make(string name, float seconds, Action<float[]> synth)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            synth(d);
            for (int i = 0; i < n; i++) d[i] = (float)Math.Tanh(d[i]);
            int fade = Mathf.Min(220, n / 4);
            for (int i = 0; i < fade; i++) { d[i] *= i / (float)fade; d[n - 1 - i] *= i / (float)fade; }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        static float T(int i) => i / (float)Rate;
        static float Env(float t, float a, float d) => t < a ? t / a : Mathf.Exp(-(t - a) / d);

        struct Lp
        {
            float y;
            public float Run(float x, float cutoff) { y += (1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate)) * (x - y); return y; }
        }

        static void MarchNote(float[] d, float f)
        {
            // the classic heartbeat: a fat square-ish bass thump
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float ph = f * t;
                float sq = Mathf.Sin(2 * Mathf.PI * ph) + 0.33f * Mathf.Sin(2 * Mathf.PI * ph * 3) + 0.2f * Mathf.Sin(2 * Mathf.PI * ph * 5);
                float sub = Mathf.Sin(2 * Mathf.PI * f * 0.5f * t) * 0.6f;
                d[i] = (sq * 0.7f + sub) * Env(t, 0.004f, 0.09f) * 1.1f;
            }
        }

        void Zap(float[] d, float f0, float f1, float len, float vol)
        {
            float ph = 0;
            var lp = new Lp();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float k = t / len;
                float f = Mathf.Lerp(f0, f1, 1f - Mathf.Pow(1f - k, 2f));
                ph += f / Rate;
                float s = (Mathf.Repeat(ph, 1f) * 2f - 1f) * 0.5f + Mathf.Sin(2 * Mathf.PI * ph) * 0.6f;
                s = lp.Run(s, 5000f);
                d[i] = (s + Noise() * 0.05f) * Env(t, 0.003f, len * 0.35f) * vol * 1.4f;
            }
        }

        void InvaderDie(float[] d)
        {
            var lp = new Lp();
            float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float f = 900f * Mathf.Exp(-t * 7f) + 80f;
                ph += f / Rate;
                float crunch = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * ph)) * 0.35f;
                float n = lp.Run(Noise(), 2500f * Mathf.Exp(-t * 4f) + 200f);
                d[i] = (crunch + n * 1.2f) * Env(t, 0.002f, 0.12f);
            }
        }

        void Boom(float[] d, float len, float size)
        {
            var lp = new Lp(); var lp2 = new Lp();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float rumble = lp.Run(Noise(), Mathf.Lerp(3000f, 120f, Mathf.Clamp01(t / (len * 0.6f)))) * Env(t, 0.003f, len * 0.28f) * 2.4f;
                float sub = Mathf.Sin(2 * Mathf.PI * (40f * t - 6f * t * t)) * Env(t, 0.005f, len * 0.3f) * 1.6f;
                float crack = (Noise() - lp2.Run(Noise(), 4000f)) * Env(t, 0.001f, 0.05f) * 0.8f;
                d[i] = (rumble + sub + crack) * size;
            }
        }

        void PlayerDie(float[] d)
        {
            Boom(d, 2.5f, 1.1f);
            float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float f = 700f * Mathf.Exp(-t * 1.6f) + 40f;
                ph += f / Rate;
                d[i] += Mathf.Sin(2 * Mathf.PI * ph) * 0.4f * Env(t, 0.01f, 0.8f);
            }
        }

        void BunkerHit(float[] d)
        {
            var lp = new Lp();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                d[i] = lp.Run(Noise(), 1800f) * Env(t, 0.001f, 0.05f) * 1.4f + Mathf.Sin(2 * Mathf.PI * 120f * t) * Env(t, 0.002f, 0.04f) * 0.6f;
            }
        }

        static void PowerUpSound(float[] d)
        {
            float[] notes = { 523.3f, 659.3f, 784f, 1046.5f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                int k = Mathf.Min(3, (int)(t / 0.09f));
                float lt = t - k * 0.09f;
                float f = notes[k];
                float s = Mathf.Sin(2 * Mathf.PI * f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * f * 2 * t) * 0.2f;
                d[i] = s * Env(lt, 0.004f, k == 3 ? 0.25f : 0.06f) * 0.9f;
            }
        }

        void ShieldHit(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float s = Mathf.Sin(2 * Mathf.PI * 300f * t * (1f + 0.3f * Mathf.Sin(2 * Mathf.PI * 30f * t))) * 0.5f + Noise() * 0.15f;
                d[i] = s * Env(t, 0.002f, 0.15f);
            }
        }

        void WaveStart(float[] d)
        {
            float[] notes = { 196f, 261.6f, 329.6f, 392f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float s = 0;
                for (int k = 0; k < notes.Length; k++)
                {
                    float st = t - k * 0.12f;
                    if (st < 0) continue;
                    float f = notes[k];
                    float saw = Mathf.Repeat(f * st, 1f) * 2f - 1f;
                    s += (saw * 0.25f + Mathf.Sin(2 * Mathf.PI * f * st) * 0.3f) * Env(st, 0.01f, 0.5f);
                }
                d[i] = s * 0.7f;
            }
        }

        /// <summary>Falling, warbling whistle for an invader breaking formation.</summary>
        void DiveWhistle(float[] d)
        {
            float ph = 0;
            var lp = new Lp();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float k = t / 1.1f;
                float f = Mathf.Lerp(1500f, 260f, k) * (1f + 0.06f * Mathf.Sin(2 * Mathf.PI * 9f * t));
                ph += f / Rate;
                float s = Mathf.Sin(2 * Mathf.PI * ph) * 0.7f + (Mathf.Repeat(ph, 1f) * 2f - 1f) * 0.25f;
                s = lp.Run(s, 3500f);
                d[i] = s * Env(t, 0.02f, 0.9f) * 0.55f;
            }
        }

        void Warp(float[] d)
        {
            var lp = new Lp();
            float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float k = t / 1.4f;
                float f = Mathf.Lerp(80f, 1800f, k * k);
                ph += f / Rate;
                float s = Mathf.Sin(2 * Mathf.PI * ph) * 0.4f + lp.Run(Noise(), 300f + 3000f * k) * 0.8f;
                d[i] = s * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * (k > 0.85f ? 1.5f : 1f);
            }
        }

        static void BossAlarm(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float cyc = t % 0.6f;
                float f = cyc < 0.3f ? 440f : 330f;
                float sq = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t)) * 0.3f + Mathf.Sin(2 * Mathf.PI * f * 0.5f * t) * 0.3f;
                d[i] = sq * Env(cyc % 0.3f, 0.01f, 0.25f);
            }
        }

        static void ExtraLife(float[] d)
        {
            float[] notes = { 784f, 988f, 1175f, 1568f, 1175f, 1568f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                int k = Mathf.Min(notes.Length - 1, (int)(t / 0.1f));
                float lt = t - k * 0.1f;
                d[i] = (Mathf.Sign(Mathf.Sin(2 * Mathf.PI * notes[k] * t)) * 0.2f + Mathf.Sin(2 * Mathf.PI * notes[k] * t) * 0.3f) * Env(lt, 0.003f, 0.08f);
            }
        }

        void UfoDie(float[] d)
        {
            Boom(d, 1.4f, 0.7f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float f = 1200f + 600f * Mathf.Sin(2 * Mathf.PI * 18f * t);
                d[i] += Mathf.Sin(2 * Mathf.PI * f * t * Mathf.Exp(-t)) * 0.3f * Env(t, 0.002f, 0.4f);
            }
        }

        static void UfoSiren(float[] d)
        {
            // the classic UFO warble: a fast-modulated tone
            float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float f = 760f + 280f * Mathf.Sin(2 * Mathf.PI * 7.5f * t);
                ph += f / Rate;
                d[i] = (Mathf.Sin(2 * Mathf.PI * ph) * 0.4f + Mathf.Sign(Mathf.Sin(2 * Mathf.PI * ph)) * 0.08f);
            }
        }

        void Music(float[] d)
        {
            // slow, dark synth pad in A minor with a gentle arpeggio
            float[][] chords =
            {
                new[] { 110f, 164.8f, 220f, 261.6f },
                new[] { 87.3f, 130.8f, 174.6f, 220f },
                new[] { 98f, 146.8f, 196f, 246.9f },
                new[] { 82.4f, 123.5f, 164.8f, 207.7f },
            };
            float bar = d.Length / (float)chords.Length;
            var lp = new Lp();
            var ph = new float[8];
            float arpPh = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                int ci = (int)(i / bar);
                var ch = chords[ci % 4];
                float s = 0;
                for (int v = 0; v < 4; v++)
                for (int det = 0; det < 2; det++)
                {
                    int k = v * 2 + det;
                    ph[k] += ch[v] * (det == 0 ? 0.996f : 1.004f) / Rate;
                    s += (Mathf.Repeat(ph[k], 1f) * 2f - 1f) * 0.06f;
                }
                float swell = 0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * t / 8f);
                float pad = lp.Run(s, 500f + 400f * swell) * swell;
                // arpeggio, 8th notes at ~100bpm
                float step = 0.3f;
                int ai = (int)(t / step);
                float at = t - ai * step;
                float af = ch[ai % 4] * 4f;
                arpPh += af / Rate;
                float arp = Mathf.Sin(2 * Mathf.PI * arpPh) * Env(at, 0.005f, 0.12f) * 0.12f;
                d[i] = pad * 1.5f + arp;
            }
            int x = Rate;
            for (int i = 0; i < x; i++) { float a = i / (float)x; d[i] = d[i] * a + d[d.Length - x + i] * (1f - a); }
        }
    }
}
