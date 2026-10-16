using UnityEngine;

namespace Orbital
{
    /// <summary>IMGUI front end: title, HUD, banners, popups, pause and game over.</summary>
    public class GameUI : MonoBehaviour
    {
        GUIStyle title, big, mid, hud, small, button, popup, centre;
        Texture2D white, panel, buttonTex, buttonHover, shipIcon;
        float scale = 1f;

        static readonly Color Cyan = new Color(0.45f, 0.9f, 1f);
        static readonly Color Magenta = new Color(1f, 0.4f, 0.9f);
        static readonly Color Green = new Color(0.5f, 1f, 0.55f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.35f);

        Game G => Game.I;

        void Build()
        {
            if (title != null) return;
            var lib = OrbitalLibrary.I;
            white = Tex(1, 1, (x, y) => Color.white);
            panel = Tex(8, 8, (x, y) => new Color(0.02f, 0.03f, 0.08f, 0.72f));
            buttonTex = Bordered(new Color(0.05f, 0.1f, 0.2f, 0.85f), new Color(0.3f, 0.8f, 1f, 0.8f));
            buttonHover = Bordered(new Color(0.1f, 0.3f, 0.5f, 0.95f), new Color(0.6f, 1f, 1f, 1f));
            shipIcon = Tex(32, 32, (x, y) =>
            {
                float u = (x - 15.5f) / 16f, v = y / 31f;
                bool body = Mathf.Abs(u) < 0.12f + 0.1f * (1 - v) && v < 0.95f;
                bool wing = Mathf.Abs(u) < (1f - v) * 1.1f && v > 0.1f && v < 0.45f;
                return body || wing ? Cyan : new Color(0, 0, 0, 0);
            });

            title = new GUIStyle { font = lib.titleFont, fontSize = 110, alignment = TextAnchor.MiddleCenter };
            big = new GUIStyle { font = lib.titleFont, fontSize = 84, alignment = TextAnchor.MiddleCenter };
            mid = new GUIStyle { font = lib.hudFont, fontSize = 34, alignment = TextAnchor.MiddleCenter };
            hud = new GUIStyle { font = lib.hudFont, fontSize = 30, alignment = TextAnchor.UpperLeft };
            small = new GUIStyle { font = lib.hudFont, fontSize = 21, alignment = TextAnchor.MiddleCenter };
            popup = new GUIStyle { font = lib.hudFont, fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            centre = new GUIStyle(hud) { alignment = TextAnchor.UpperCenter };
            button = new GUIStyle { font = lib.hudFont, fontSize = 28, alignment = TextAnchor.MiddleCenter, border = new RectOffset(4, 4, 4, 4) };
            button.normal.background = buttonTex; button.normal.textColor = Cyan;
            button.hover.background = buttonHover; button.hover.textColor = Color.white;
            button.active.background = buttonHover; button.active.textColor = Gold;
        }

        static Texture2D Tex(int w, int h, System.Func<int, int, Color> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, f(x, y));
            t.Apply();
            return t;
        }

        static Texture2D Bordered(Color fill, Color edge)
        {
            return Tex(12, 12, (x, y) => x == 0 || y == 0 || x == 11 || y == 11 ? edge : (x == 1 || y == 1 || x == 10 || y == 10 ? Color.Lerp(fill, edge, 0.3f) : fill));
        }

        void Text(Rect r, string s, GUIStyle st, Color c, float glow = 0f)
        {
            if (glow > 0f)
            {
                var g = new Color(c.r, c.g, c.b, c.a * 0.25f * glow);
                st.normal.textColor = g;
                for (int i = 0; i < 4; i++)
                {
                    float o = 2f + i;
                    GUI.Label(new Rect(r.x + (i % 2 == 0 ? o : -o), r.y + (i < 2 ? o : -o), r.width, r.height), s, st);
                }
            }
            st.normal.textColor = new Color(0, 0, 0, c.a * 0.7f);
            GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), s, st);
            st.normal.textColor = c;
            GUI.Label(r, s, st);
        }

        void OnGUI()
        {
            if (!G) return;
            Build();
            scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            // during automated test runs nothing on screen may be clicked
            if (G.Automated && Event.current.isMouse) Event.current.Use();
            float W = Screen.width / scale, H = 1080f;

            DrawPopups();
            if (G.Demo) DrawTitle(W, H);
            else
            {
                DrawHud(W, H);
                DrawBanner(W, H);
                if (G.Paused) DrawPause(W, H);
                if (G.State == GameState.GameOver) DrawGameOver(W, H);
            }
        }

        // ------------------------------------------------------------ title

        void DrawTitle(float W, float H)
        {
            float t = Time.unscaledTime;
            // the voxel logo is in the scene; this is the supporting text
            Text(new Rect(0, 365, W, 50), "D E F E N D   T H E   E A R T H", mid, new Color(0.7f, 0.85f, 1f, 0.9f), 0.6f);

            float blink = 0.55f + 0.45f * Mathf.Sin(t * 4f);
            Text(new Rect(0, 690, W, 60), "PRESS ENTER TO START", mid, new Color(Gold.r, Gold.g, Gold.b, blink), 1f);

            float bw = 360;
            if (GUI.Button(new Rect(W * 0.5f - bw * 0.5f, 752, bw, 60), "START GAME", button)) { SoundBank.I.Play(Sfx.Select, 0.8f); G.NewGame(false); }
            if (GUI.Button(new Rect(W * 0.5f - bw * 0.5f, 822, bw, 48), "QUIT", button)) Application.Quit();

            // attract mode: the left panel alternates between the points table and the high scores
            float x0 = 70, y0 = 450;
            if (((int)(t / 7f)) % 2 == 1) DrawScoreTable(new Rect(x0 - 20, y0 - 20, 380, 400), -1);
            else
            {
                var pan = new Rect(x0 - 20, y0 - 20, 380, 250);
                GUI.DrawTexture(pan, panel);
                Text(new Rect(x0, y0, 340, 34), "SCORE ADVANCE TABLE", small, Cyan);
                Text(new Rect(x0, y0 + 46, 340, 34), "SQUID     =  30 PTS", small, Magenta);
                Text(new Rect(x0, y0 + 86, 340, 34), "CRAB      =  20 PTS", small, Cyan);
                Text(new Rect(x0, y0 + 126, 340, 34), "OCTOPUS   =  10 PTS", small, Green);
                Text(new Rect(x0, y0 + 166, 340, 34), "UFO       =  ? MYSTERY", small, new Color(1f, 0.4f, 0.6f));
            }

            var cp = new Rect(W - 450, y0 - 20, 400, 290);
            GUI.DrawTexture(cp, panel);
            Text(new Rect(cp.x + 20, y0, 360, 34), "CONTROLS", small, Cyan);
            Text(new Rect(cp.x + 20, y0 + 46, 360, 34), "MOVE      WASD / ARROWS", small, Color.white);
            Text(new Rect(cp.x + 20, y0 + 86, 360, 34), "FIRE      SPACE", small, Color.white);
            Text(new Rect(cp.x + 20, y0 + 126, 360, 34), "PAUSE     ESC / P", small, Color.white);
            Text(new Rect(cp.x + 20, y0 + 166, 360, 34), "PANIC     SHIFT / B", small, new Color(1f, 0.6f, 0.25f));
            Text(new Rect(cp.x + 20, y0 + 206, 360, 34), "GAMEPAD   SUPPORTED", small, Color.white);

            Text(new Rect(0, 895, W, 40), $"HI-SCORE  {G.HiScore:000000}", mid, Gold, 0.5f);

            GUI.Label(new Rect(W * 0.5f - 200, 950, 120, 30), "MUSIC", new GUIStyle(small) { alignment = TextAnchor.MiddleRight, normal = { textColor = Cyan } });
            SoundBank.I.MusicVolume = GUI.HorizontalSlider(new Rect(W * 0.5f - 60, 960, 260, 20), SoundBank.I.MusicVolume, 0f, 1f);

            Text(new Rect(0, H - 52, W, 40), "DEVELOPED BY HOST HOBBIT", small, new Color(0.7f, 0.8f, 1f, 0.75f));
        }

        // ------------------------------------------------------------ HUD

        void DrawHud(float W, float H)
        {
            Text(new Rect(40, 24, 400, 40), "SCORE", hud, Cyan);
            Text(new Rect(40, 58, 400, 44), $"{G.Score:000000}", hud, Color.white);
            Text(new Rect(0, 24, W, 40), "HI-SCORE", centre, Gold);
            Text(new Rect(0, 58, W, 44), $"{Mathf.Max(G.HiScore, G.Score):000000}", centre, Color.white);
            Text(new Rect(W - 260, 24, 220, 40), "WAVE", new GUIStyle(hud) { alignment = TextAnchor.UpperRight }, Cyan);
            Text(new Rect(W - 260, 58, 220, 44), G.Wave.ToString(), new GUIStyle(hud) { alignment = TextAnchor.UpperRight }, Color.white);

            if (G.Combo > 1)
            {
                float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 12f);
                var st = new GUIStyle(hud) { fontSize = Mathf.RoundToInt(30 * pulse) };
                Text(new Rect(40, 104, 400, 44), $"COMBO x{G.Combo}", st, Gold, 0.6f);
            }

            // lives
            for (int i = 0; i < Mathf.Min(G.Lives, 5); i++) GUI.DrawTexture(new Rect(40 + i * 44, H - 74, 36, 36), shipIcon);
            if (G.Lives > 5) Text(new Rect(40 + 5 * 44 + 6, H - 76, 120, 40), "x" + G.Lives, hud, Cyan);

            // panic charges: pips plus a bar filling toward the next one
            {
                Color orange = new Color(1f, 0.55f, 0.2f);
                float px = 40, py = H - 122;
                Text(new Rect(px, py - 4, 200, 36), "PANIC", hud, orange);
                for (int i = 0; i < Game.PanicMax; i++)
                {
                    var pip = new Rect(px + 130 + i * 34, py + 4, 26, 26);
                    GUI.DrawTexture(pip, panel);
                    if (i < G.PanicCharges)
                    {
                        float glow = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f + i);
                        GUI.color = new Color(orange.r, orange.g, orange.b, glow);
                        GUI.DrawTexture(new Rect(pip.x + 3, pip.y + 3, pip.width - 6, pip.height - 6), white);
                        GUI.color = Color.white;
                    }
                }
                if (G.PanicCharges < Game.PanicMax)
                {
                    var bar = new Rect(px + 130, py + 34, Game.PanicMax * 34 - 8, 6);
                    GUI.DrawTexture(bar, panel);
                    GUI.color = new Color(orange.r, orange.g, orange.b, 0.8f);
                    GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * G.PanicMeter, bar.height), white);
                    GUI.color = Color.white;
                }
            }

            // active power-ups
            var p = Player.I;
            float y = H - 80;
            for (int k = 0; k < 4; k++)
            {
                float time = p.PowerTime[k];
                if (time <= 0f) continue;
                Color c = PowerUp.Colours[k];
                float maxT = k == (int)PowerKind.Shield ? 12f : 10f;
                var r = new Rect(W - 380, y, 340, 34);
                GUI.DrawTexture(r, panel);
                GUI.color = new Color(c.r, c.g, c.b, 0.55f);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(time / maxT), r.height), white);
                GUI.color = Color.white;
                Text(r, PowerUp.Names[k], small, Color.white);
                y -= 42;
            }

            // boss health
            var boss = Boss.I;
            if (boss && boss.gameObject.activeSelf && (boss.Alive || boss.Dying))
            {
                float bw = 700;
                var r = new Rect(W * 0.5f - bw * 0.5f, 112, bw, 22);
                GUI.DrawTexture(r, panel);
                GUI.color = boss.TurretsAlive > 0 ? new Color(0.4f, 0.6f, 1f, 0.85f) : new Color(1f, 0.3f, 0.2f, 0.9f);
                GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01(boss.Health / boss.MaxHealth), r.height - 4), white);
                GUI.color = Color.white;
                Text(new Rect(0, 136, W, 30), boss.TurretsAlive > 0 ? $"MOTHERSHIP  -  DESTROY THE TURRETS ({boss.TurretsAlive})" : "MOTHERSHIP  -  CORE EXPOSED", small, Color.white);
            }
        }

        void DrawBanner(float W, float H)
        {
            float a = G.BannerAge;
            if (a > 2.6f || string.IsNullOrEmpty(G.Banner) || G.State == GameState.GameOver) return;
            float alpha = Mathf.Clamp01(a / 0.2f) * Mathf.Clamp01((2.6f - a) / 0.5f);
            float slide = (1f - Mathf.Clamp01(a / 0.35f)) * 60f;
            bool warn = G.Banner == "WARNING";
            Color c = warn ? new Color(1f, 0.25f, 0.2f, alpha * (0.6f + 0.4f * Mathf.Sin(a * 14f))) : new Color(Cyan.r, Cyan.g, Cyan.b, alpha);
            Text(new Rect(slide, 330, W, 110), G.Banner, big, c, 1f);
            Text(new Rect(-slide, 440, W, 50), G.SubBanner, mid, new Color(1, 1, 1, alpha * 0.9f), 0.4f);
        }

        void DrawPopups()
        {
            var cam = CameraRig.I ? CameraRig.I.Cam : null;
            if (!cam) return;
            foreach (var p in Fx.I.Popups)
            {
                Vector3 sp = cam.WorldToScreenPoint(p.Pos);
                if (sp.z < 0) continue;
                float a = Mathf.Clamp01((1.3f - p.Age) / 0.4f);
                float pop = 1f + Mathf.Max(0f, 0.25f - p.Age) * 2f;
                var st = new GUIStyle(popup) { fontSize = Mathf.RoundToInt(28 * p.Size * pop) };
                var r = new Rect(sp.x / scale - 150, (Screen.height - sp.y) / scale - 25, 300, 50);
                Text(r, p.Text, st, new Color(p.Colour.r, p.Colour.g, p.Colour.b, a), 0.5f);
            }
        }

        void DrawPause(float W, float H)
        {
            GUI.DrawTexture(new Rect(0, 0, W, H), panel);
            Text(new Rect(0, 320, W, 110), "PAUSED", big, Cyan, 1f);
            float bw = 380;
            if (GUI.Button(new Rect(W * 0.5f - bw * 0.5f, 500, bw, 64), "RESUME", button)) G.TogglePause();
            if (GUI.Button(new Rect(W * 0.5f - bw * 0.5f, 580, bw, 64), "QUIT TO TITLE", button)) G.ToTitle();
            GUI.Label(new Rect(W * 0.5f - 200, 680, 120, 30), "MUSIC", new GUIStyle(small) { alignment = TextAnchor.MiddleRight, normal = { textColor = Cyan } });
            SoundBank.I.MusicVolume = GUI.HorizontalSlider(new Rect(W * 0.5f - 60, 690, 260, 20), SoundBank.I.MusicVolume, 0f, 1f);
        }

        GUIStyle rowLeft, rowRight;

        /// <summary>The top-ten table; <paramref name="highlight"/> is a 0-based row to flash.</summary>
        void DrawScoreTable(Rect r, int highlight)
        {
            if (rowLeft == null)
            {
                rowLeft = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft };
                rowRight = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            }
            GUI.DrawTexture(r, panel);
            Text(new Rect(r.x, r.y + 14, r.width, 34), "HIGH SCORES", small, Cyan);
            var list = HighScores.Entries;
            float rowH = (r.height - 66) / HighScores.Size;
            for (int i = 0; i < list.Count && i < HighScores.Size; i++)
            {
                var e = list[i];
                float y = r.y + 56 + i * rowH;
                Color c = i < 3 ? Gold : Color.white;
                if (i == highlight) c = Color.Lerp(Gold, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f));
                float pad = r.width * 0.07f;
                Text(new Rect(r.x + pad, y, 60, rowH), (i + 1) + ".", rowLeft, i == highlight ? c : Cyan);
                Text(new Rect(r.x + pad + r.width * 0.16f, y, 100, rowH), e.name, rowLeft, c);
                Text(new Rect(r.x + r.width * 0.42f, y, r.width * 0.3f, rowH), e.score.ToString("000000"), rowRight, c);
                Text(new Rect(r.x + r.width * 0.74f, y, r.width * 0.2f, rowH), "W" + e.wave, rowRight, new Color(c.r, c.g, c.b, 0.7f));
            }
        }

        void DrawGameOver(float W, float H)
        {
            float a = Mathf.Clamp01(G.BannerAge / 0.6f);
            Text(new Rect(0, 110, W, 120), "GAME OVER", title, new Color(1f, 0.3f, 0.25f, a), 1f);
            Text(new Rect(0, 225, W, 50), G.GameOverReason, mid, new Color(1, 1, 1, a * 0.9f));
            Text(new Rect(0, 285, W, 60), $"SCORE  {G.Score:000000}", mid, new Color(Gold.r, Gold.g, Gold.b, a), 0.6f);
            Text(new Rect(0, 335, W, 40), $"REACHED WAVE {G.Wave}", small, new Color(Cyan.r, Cyan.g, Cyan.b, a));

            if (G.EnteringInitials)
            {
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f);
                string head = G.NewHiScore ? "NEW HIGH SCORE!" : $"YOU MADE THE TOP TEN  -  RANK #{G.PendingRank}";
                Text(new Rect(0, 410, W, 60), head, mid, new Color(Gold.r, Gold.g, Gold.b, a * pulse), 1f);
                Text(new Rect(0, 470, W, 40), "ENTER YOUR INITIALS", small, new Color(1, 1, 1, a));
                float bw = 120, gap = 26, x = W * 0.5f - (bw * 3 + gap * 2) * 0.5f;
                for (int i = 0; i < 3; i++)
                {
                    var box = new Rect(x + i * (bw + gap), 525, bw, 140);
                    GUI.DrawTexture(box, panel);
                    bool cur = i == G.InitialsCursor;
                    if (cur)
                    {
                        GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.25f + 0.2f * pulse);
                        GUI.DrawTexture(box, white);
                        GUI.color = new Color(Gold.r, Gold.g, Gold.b, pulse);
                        GUI.DrawTexture(new Rect(box.x + 14, box.yMax - 14, box.width - 28, 6), white);
                        GUI.color = Color.white;
                    }
                    Text(box, G.Initials[i].ToString(), big, cur ? Gold : Color.white, cur ? 1f : 0.4f);
                }
                Text(new Rect(0, 700, W, 36), "TYPE LETTERS   -   UP / DOWN CHANGE   -   LEFT / RIGHT MOVE", small, new Color(0.75f, 0.85f, 1f, a));
                Text(new Rect(0, 738, W, 36), "PRESS ENTER TO SAVE", small, new Color(Gold.r, Gold.g, Gold.b, a * pulse));
                return;
            }

            DrawScoreTable(new Rect(W * 0.5f - 280, 395, 560, 440), G.HighScoreRow);
            if (G.BannerAge > 2.5f)
            {
                float bw = 380;
                if (GUI.Button(new Rect(W * 0.5f - bw - 12, 870, bw, 64), "PLAY AGAIN", button)) G.NewGame(false);
                if (GUI.Button(new Rect(W * 0.5f + 12, 870, bw, 64), "TITLE SCREEN", button)) G.ToTitle();
            }
        }
    }
}
