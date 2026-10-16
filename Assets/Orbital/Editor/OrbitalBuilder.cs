using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orbital.EditorTools
{
    /// <summary>Generates every asset, the scene and the Linux player.</summary>
    public static class OrbitalBuilder
    {
        const string Root = "Assets/Orbital/";
        const string Gen = Root + "Generated/";
        public const string ScenePath = Root + "Scenes/Orbital.unity";
        const string BuildPath = "Build/Linux/OrbitalInvaders.x86_64";

        static Shader hullShader, glowShader;

        [MenuItem("Orbital/Generate Everything")]
        public static void GenerateAll()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ConfigureProject();
            Directory.CreateDirectory(Gen);
            Directory.CreateDirectory(Root + "Scenes");
            Directory.CreateDirectory(Root + "Settings");
            AssetDatabase.Refresh();
            hullShader = Shader.Find("Orbital/Hull");
            glowShader = Shader.Find("Orbital/Glow");
            if (!hullShader || !glowShader) throw new Exception("Orbital shaders not found");

            var lib = LoadOrCreate<OrbitalLibrary>(Root + "Resources/OrbitalLibrary.asset");
            var tex = MakeTextures();
            MakeMeshes(lib);
            MakeMaterials(lib, tex);
            lib.hudFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "Resources/HudFont.ttf");
            lib.titleFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "Resources/TitleFont.ttf");
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();

            MakePipeline();
            var profile = MakeVolumeProfile();
            BuildScene(lib, tex, profile);
            AssetDatabase.SaveAssets();
            Debug.Log($"ORBITAL_GENERATE_OK in {sw.Elapsed.TotalSeconds:F1}s");
        }

        [MenuItem("Orbital/Build Linux Player")]
        public static void BuildLinux()
        {
            ConfigureProject();
            Directory.CreateDirectory("Build/Linux");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
            Debug.Log($"ORBITAL_BUILD_OK size={report.summary.totalSize / (1024 * 1024)}MB");
        }

        public static void GenerateAndBuild()
        {
            GenerateAll();
            BuildLinux();
        }

        public const string WebGLPath = "Build/WebGL";

        /// <summary>Browser build (WebGL 2) using the page template in Assets/WebGLTemplates/Orbital.</summary>
        public static void BuildWebGL()
        {
            ConfigureProject();
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
            // gzip + fallback: static hosts like GitHub Pages don't send Content-Encoding for .gz files
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.template = "PROJECT:Orbital";
            PlayerSettings.runInBackground = false;
            if (Directory.Exists(WebGLPath)) Directory.Delete(WebGLPath, true);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = WebGLPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("WebGL build failed: " + report.summary.result);
            Debug.Log($"ORBITAL_WEBGL_OK size={report.summary.totalSize / (1024 * 1024)}MB");
        }

        public static void GenerateAndBuildWebGL()
        {
            GenerateAll();
            BuildWebGL();
        }

        static void ConfigureProject()
        {
            PlayerSettings.companyName = "Host Hobbit";
            PlayerSettings.productName = "Orbital Invaders";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLCore });
            try { PlayerSettings.SplashScreen.show = false; } catch { }
            var so = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var input = so.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static void SaveAsset(UnityEngine.Object obj, string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(obj, path);
        }

        // ------------------------------------------------------------ textures

        class Tex { public Texture2D Soft, SoftAlpha, Fire, Ring, Smoke, Streak, SunGlow, SunStreak, Reticle, Hex; }

        static Tex MakeTextures()
        {
            var t = new Tex();
            t.Soft = SaveTex("soft", 128, (u, v) => Pre(Mathf.Exp(-R(u, v) * R(u, v) * 9f)));
            t.SoftAlpha = SaveTex("soft_alpha", 128, (u, v) => { float d = R(u, v); return new Color(1, 1, 1, Mathf.Exp(-d * d * 7f) * Mathf.Clamp01(1 - d)); });
            t.Fire = SaveTex("fire", 128, (u, v) =>
            {
                float d = R(u, v);
                float n = Mathf.PerlinNoise(u * 6f, v * 6f) * 0.6f + Mathf.PerlinNoise(u * 13f + 5, v * 13f) * 0.4f;
                return Pre(Mathf.Clamp01(Mathf.Exp(-d * d * 6f) * (0.6f + 0.8f * n) - 0.08f));
            });
            t.Ring = SaveTex("ring", 256, (u, v) => { float d = R(u, v); return Pre(Mathf.Clamp01(Mathf.Exp(-Mathf.Pow((d - 0.82f) / 0.06f, 2)) + 0.3f * Mathf.Exp(-Mathf.Pow((d - 0.65f) / 0.15f, 2)))); });
            t.Smoke = SaveTex("smoke", 128, (u, v) =>
            {
                float d = R(u, v);
                float n = Mathf.PerlinNoise(u * 5f, v * 5f) * 0.7f + Mathf.PerlinNoise(u * 11f + 3, v * 11f) * 0.3f;
                return new Color(1, 1, 1, Mathf.Clamp01((1f - d) * 1.3f * n - 0.1f));
            });
            t.Streak = SaveTex("streak", 64, (u, v) => { float a = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.18f, 2)); return new Color(a, a, a, a); });
            t.SunGlow = SaveTex("sunglow", 256, (u, v) =>
            {
                float d = R(u, v);
                float core = Mathf.Clamp01((0.08f - d) / 0.02f);
                float halo = Mathf.Exp(-d * 9f) * 0.8f + Mathf.Exp(-d * 3.5f) * 0.25f;
                float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(Mathf.Atan2(v - 0.5f, u - 0.5f) * 6f)), 30f) * Mathf.Exp(-d * 4f) * 0.4f;
                return Pre(Mathf.Clamp01(core + halo + rays) * Mathf.Clamp01((1f - d) * 3f));
            });
            t.SunStreak = SaveTex("sunstreak", 256, (u, v) =>
            {
                float x = Mathf.Abs(u - 0.5f) * 2f, y = Mathf.Abs(v - 0.5f) * 2f;
                return Pre(Mathf.Clamp01(Mathf.Exp(-y * y * 40f) * Mathf.Pow(1f - x, 2f)));
            });
            t.Reticle = SaveTex("reticle", 128, (u, v) =>
            {
                float d = R(u, v);
                float ang = Mathf.Atan2(v - 0.5f, u - 0.5f);
                float ring = Mathf.Exp(-Mathf.Pow((d - 0.7f) / 0.04f, 2)) * (Mathf.Abs(Mathf.Sin(ang * 2f)) > 0.38f ? 1f : 0f);
                float ticks = (Mathf.Abs(u - 0.5f) < 0.015f || Mathf.Abs(v - 0.5f) < 0.015f) && d > 0.35f && d < 0.95f ? 0.8f : 0f;
                float dot = Mathf.Clamp01((0.06f - d) / 0.02f);
                return Pre(Mathf.Clamp01(ring + ticks + dot));
            });
            t.Hex = SaveTex("hex", 256, (u, v) =>
            {
                // hexagon grid lines for the shield bubble
                float x = u * 12f, y = v * 12f * 1.1547f;
                float hx = Mathf.Abs(Mathf.Repeat(x + (Mathf.Floor(y) % 2) * 0.5f, 1f) - 0.5f);
                float hy = Mathf.Abs(Mathf.Repeat(y, 1f) - 0.5f);
                float edge = Mathf.Max(hx * 1.7f, hy + hx * 0.6f);
                float line = Mathf.Clamp01((edge - 0.72f) / 0.08f);
                return Pre(0.15f + 0.85f * line);
            }, TextureWrapMode.Repeat);
            AssetDatabase.Refresh();
            return t;
        }

        static float R(float u, float v) => new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
        static Color Pre(float a) => new Color(a, a, a, a);

        static Texture2D SaveTex(string name, int size, Func<float, float, Color> f, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / size);
            t.SetPixels(px);
            t.Apply();
            string path = Gen + name + ".png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = wrap;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------ meshes

        static readonly string[][] Sprites =
        {
            // squid A / B
            new[] { "...##...", "..####..", ".######.", "##.##.##", "########", "..#..#..", ".#.##.#.", "#.#..#.#" },
            new[] { "...##...", "..####..", ".######.", "##.##.##", "########", ".#.##.#.", "#......#", ".#....#." },
            // crab A / B
            new[] { "..#.....#..", "...#...#...", "..#######..", ".##.###.##.", "###########", "#.#######.#", "#.#.....#.#", "...##.##..." },
            new[] { "..#.....#..", "#..#...#..#", "#.#######.#", "###.###.###", "###########", ".#########.", "..#.....#..", ".#.......#." },
            // octopus A / B
            new[] { "....####....", ".##########.", "############", "###..##..###", "############", "...##..##...", "..##.##.##..", "##........##" },
            new[] { "....####....", ".##########.", "############", "###..##..###", "############", "..###..###..", ".##..##..##.", "..##....##.." },
        };

        static readonly string[] UfoRows =
        {
            ".....######.....",
            "...##########...",
            "..############..",
            ".##.##.##.##.##.",
            "################",
            "..###..##..###..",
            "...#........#...",
        };

        static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
            ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
            ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
        };

        static Mesh logoMesh, earthMesh;

        static Mesh SaveMesh(Mesh m, string name)
        {
            m.name = name;
            SaveAsset(m, Gen + name + ".asset");
            return m;
        }

        static void MakeMeshes(OrbitalLibrary lib)
        {
            Color[] colours = { new Color(1f, 0.25f, 0.85f), new Color(0.25f, 0.85f, 1f), new Color(0.35f, 1f, 0.45f) };
            lib.invaderMeshes = new Mesh[6];
            for (int k = 0; k < 3; k++)
            for (int f = 0; f < 2; f++)
            {
                var front = colours[k];
                var back = colours[k] * 0.45f;
                var vox = VoxelMesher.FromRows(Sprites[k * 2 + f], 2, front, back, 1f, 0.3f);
                // a third, inner layer on the body (not the legs) gives the sprite some volume
                var rows = Sprites[k * 2 + f];
                for (int r = 1; r < 5; r++)
                for (int x = 0; x < rows[r].Length; x++)
                    if (rows[r][x] == '#') { var c = back * 0.8f; c.a = 0.15f; vox.Add(new Voxel(x, rows.Length - 1 - r, 2, c)); }
                var centre = new Vector3((rows[0].Length - 1) * 0.5f, (rows.Length - 1) * 0.5f, 0.5f);
                lib.invaderMeshes[k * 2 + f] = SaveMesh(VoxelMesher.Build(vox, Invader.VoxelSize, 0.88f, centre), $"Invader{k}{f}");
            }

            var ufoVox = VoxelMesher.FromRows(UfoRows, 3, new Color(1f, 0.2f, 0.45f), new Color(0.5f, 0.08f, 0.2f), 1f, 0.35f);
            for (int i = 0; i < ufoVox.Count; i++)
            {
                var v = ufoVox[i];
                if (v.P.y == 3 && v.P.z == 0) { v.C = new Color(1f, 0.9f, 0.3f, 1f); ufoVox[i] = v; } // window lights
            }
            lib.ufoMesh = SaveMesh(VoxelMesher.Build(ufoVox, Ufo.VoxelSize, 0.9f, VoxelMesher.Centre(ufoVox)), "Ufo");

            lib.bossMesh = SaveMesh(BossMesh(), "Boss");
            lib.bossTurretMesh = SaveMesh(TurretMesh(), "BossTurret");
            lib.playerMesh = SaveMesh(ShipMesh(), "PlayerShip");
            logoMesh = SaveMesh(LogoMesh(), "Logo");

            lib.cube = SaveMesh(VoxelMesher.Build(new List<Voxel> { new Voxel(0, 0, 0, Color.white) }, 1f, 1f, Vector3.zero), "Cube");
            lib.sphere = SaveMesh(UvSphere(28, 18, 0.5f), "Sphere");
            lib.capsule = lib.sphere;
            lib.quad = SaveMesh(Quad(), "Quad");
            earthMesh = SaveMesh(UvSphere(160, 96, 1f), "EarthSphere");
        }

        static Mesh BossMesh()
        {
            const float vs = Boss.VoxelSize;
            Func<Vector3, float> sdf = p =>
            {
                float d = Sdf.Ellipsoid(p, Vector3.zero, new Vector3(6.2f, 0.95f, 4.2f));
                d = Mathf.Min(d, Sdf.Ellipsoid(p, new Vector3(0, 0.8f, 0.4f), new Vector3(2.6f, 1.5f, 2.0f)));
                d = Mathf.Min(d, Sdf.Ellipsoid(p, new Vector3(0, -0.6f, 0), new Vector3(3.6f, 0.9f, 2.7f)));
                d = Mathf.Min(d, Sdf.Torus(p, Vector3.zero, 5.5f, 0.42f));
                return d;
            };
            var solid = new HashSet<Vector3Int>();
            int nx = 23, ny = 9, nz = 21;
            for (int x = -nx; x <= nx; x++)
            for (int y = -ny; y <= ny; y++)
            for (int z = -nz; z <= nz; z++)
                if (sdf(new Vector3(x, y, z) * vs) < 0f) solid.Add(new Vector3Int(x, y, z));
            var vox = new List<Voxel>();
            var rng = new System.Random(5);
            Vector3Int[] n6 = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
            foreach (var p in solid)
            {
                bool surface = false;
                foreach (var n in n6) if (!solid.Contains(p + n)) { surface = true; break; }
                if (!surface) continue;
                Vector3 w = (Vector3)p * vs;
                float r = new Vector2(w.x, w.z).magnitude;
                float ang = Mathf.Atan2(w.z, w.x) * Mathf.Rad2Deg;
                Color c = new Color(0.24f, 0.22f, 0.3f, 0f);
                if (rng.NextDouble() < 0.12) c = new Color(0.16f, 0.15f, 0.2f, 0f);
                if (r > 5.1f && Mathf.Abs(w.y) < 0.35f && Mathf.Repeat(ang, 15f) < 5f) c = new Color(1f, 0.35f, 0.15f, 1f);          // rim lights
                else if (w.y > 1.2f && ((p.x + p.z) & 1) == 0) c = new Color(0.2f, 1f, 0.8f, 0.55f);                                // glowing dome
                else if (w.y < -1.0f && r < 1.6f) c = new Color(1f, 0.3f, 0.9f, 1f);                                                // tractor core
                else if (Mathf.Abs(w.y) < 0.2f && r < 5.0f && r > 3.2f && Mathf.Repeat(ang + 7f, 30f) < 3f) c = new Color(0.4f, 0.6f, 1f, 0.7f);
                vox.Add(new Voxel(p.x, p.y, p.z, c));
            }
            return VoxelMesher.Build(vox, vs, 0.93f, Vector3.zero);
        }

        static Mesh TurretMesh()
        {
            string[] rows = { ".###.", "#####", "##.##", ".###." };
            var vox = VoxelMesher.FromRows(rows, 4, new Color(0.3f, 0.3f, 0.38f), new Color(0.2f, 0.2f, 0.26f), 0f, 0f);
            vox.Add(new Voxel(2, 1, -1, new Color(1f, 0.15f, 0.1f, 1f)));   // the eye
            vox.Add(new Voxel(2, 1, 0, new Color(1f, 0.15f, 0.1f, 1f)));
            return VoxelMesher.Build(vox, 0.18f, 0.92f, VoxelMesher.Centre(vox));
        }

        static Mesh LogoMesh()
        {
            var vox = new List<Voxel>();
            void Word(string w, int y0, Color a, Color b)
            {
                int width = w.Length * 6 - 1;
                for (int i = 0; i < w.Length; i++)
                {
                    var g = Font[w[i]];
                    for (int r = 0; r < 7; r++)
                    for (int x = 0; x < 5; x++)
                    {
                        if (g[r][x] != '#') continue;
                        int px = i * 6 + x - width / 2;
                        Color c = Color.Lerp(a, b, (px + width / 2f) / width);
                        for (int z = 0; z < 2; z++)
                        {
                            Color cc = z == 0 ? c : c * 0.4f;
                            cc.a = z == 0 ? 1f : 0.25f;
                            vox.Add(new Voxel(px, y0 + 6 - r, z, cc));
                        }
                    }
                }
            }
            Word("ORBITAL", 9, new Color(0.3f, 0.9f, 1f), new Color(0.5f, 0.6f, 1f));
            Word("INVADERS", 0, new Color(1f, 0.3f, 0.85f), new Color(0.4f, 1f, 0.5f));
            return VoxelMesher.Build(vox, 0.3f, 0.88f, new Vector3(0, 8f, 0.5f));
        }

        static Mesh ShipMesh()
        {
            Func<Vector3, float> sdf = p =>
            {
                Vector3 q = new Vector3(Mathf.Abs(p.x), p.y, p.z);
                float d = Sdf.Ellipsoid(p, new Vector3(0, 0, -0.1f), new Vector3(0.36f, 0.25f, 1.5f));
                d = Sdf.SMin(d, Sdf.RoundCone(p, new Vector3(0, 0, 0.9f), new Vector3(0, -0.02f, 1.8f), 0.24f, 0.035f), 0.15f);
                d = Sdf.SMin(d, Sdf.Ellipsoid(p, new Vector3(0, 0.17f, 0.35f), new Vector3(0.2f, 0.17f, 0.55f)), 0.05f);
                // swept wings
                Vector3 wq = Sdf.RotY(q - new Vector3(0.95f, -0.05f, -0.45f), 22f);
                float wing = Sdf.Box(wq, Vector3.zero, new Vector3(0.68f, 0.035f, 0.38f), 0.03f);
                wing = Mathf.Max(wing, -(q.z + 1.05f));
                d = Sdf.SMin(d, wing, 0.08f);
                // wingtip fins
                d = Mathf.Min(d, Sdf.Box(q, new Vector3(1.6f, 0.1f, -0.8f), new Vector3(0.03f, 0.2f, 0.26f), 0.02f));
                // engine nacelles with hollow nozzles
                float nac = Sdf.Capsule(q, new Vector3(0.42f, -0.03f, -1.55f), new Vector3(0.42f, -0.03f, -0.35f), 0.17f);
                nac = Mathf.Max(nac, -(q.z + 1.68f));
                nac = Sdf.Sub(nac, Sdf.Cylinder(new Vector3(q.x - 0.42f, q.z + 1.7f, q.y + 0.03f), Vector3.zero, 0.11f, 0.15f), 0.01f);
                d = Sdf.SMin(d, nac, 0.06f);
                // twin cannons
                d = Mathf.Min(d, Sdf.Capsule(q, new Vector3(0.72f, -0.07f, -0.1f), new Vector3(0.72f, -0.07f, 0.85f), 0.045f));
                return d;
            };
            Func<Vector3, Color> colour = p =>
            {
                Vector3 q = new Vector3(Mathf.Abs(p.x), p.y, p.z);
                // cockpit glass
                if (Sdf.Ellipsoid(p, new Vector3(0, 0.17f, 0.35f), new Vector3(0.2f, 0.17f, 0.55f)) > -0.02f && p.y > 0.12f && Mathf.Abs(p.z - 0.35f) < 0.5f && Mathf.Abs(p.x) < 0.21f)
                    return new Color(0.25f, 0.8f, 1f, 0.9f);
                // nozzles glow
                if (q.z < -1.6f && Mathf.Abs(q.x - 0.42f) < 0.13f) return new Color(0.5f, 0.8f, 1f, 1f);
                // wing stripes and fin tips
                if (q.x > 1.1f && q.x < 1.35f && p.y > -0.1f) return new Color(0.15f, 0.45f, 1f, 0.12f);
                if (q.x > 1.55f && p.y > 0.2f) return new Color(1f, 0.3f, 0.2f, 0.8f);
                // nose stripe
                if (p.z > 1.2f && p.z < 1.32f) return new Color(0.15f, 0.45f, 1f, 0.1f);
                // dark underside
                if (p.y < -0.12f) return new Color(0.35f, 0.37f, 0.42f, 0f);
                return new Color(0.9f, 0.92f, 0.95f, 0f);
            };
            var b = new Bounds(new Vector3(0, 0.05f, 0), new Vector3(3.4f, 0.8f, 3.8f));
            return SurfaceNets.Build(sdf, b, 0.022f, colour);
        }

        static Mesh UvSphere(int seg, int rings, float radius)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= seg; s++)
            {
                float th = r / (float)rings * Mathf.PI, ph = s / (float)seg * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                v.Add(p * radius); n.Add(p); uv.Add(new Vector2(s / (float)seg, 1f - r / (float)rings)); c.Add(Color.white);
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                int a = r * (seg + 1) + s, b = a + seg + 1;
                tri.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
            var m = new Mesh { indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh Quad()
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------ materials

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        static Material HullMat(string name, Action<Material> setup)
        {
            var m = new Material(hullShader) { name = name };
            setup(m);
            SaveAsset(m, Gen + name + ".mat");
            return m;
        }

        static Material GlowMat(string name, Texture2D tex, Color colour, bool additive = true, float fresnel = 0f, CullMode cull = CullMode.Off)
        {
            var m = new Material(glowShader) { name = name };
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", colour);
            m.SetFloat("_Fresnel", fresnel);
            m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Cull", (float)cull);
            m.renderQueue = 3000;
            SaveAsset(m, Gen + name + ".mat");
            return m;
        }

        static void MakeMaterials(OrbitalLibrary lib, Tex t)
        {
            lib.voxel = HullMat("Voxel", m =>
            {
                m.SetFloat("_UseVertexColor", 1); m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0.25f); m.SetFloat("_Smoothness", 0.65f);
                m.SetFloat("_EmissionStrength", 1.7f); m.SetFloat("_EmissionPulse", 0.5f);
                m.SetColor("_FlashColor", new Color(4f, 4f, 4f));
            });
            lib.debris = HullMat("Debris", m =>
            {
                m.SetFloat("_UseVertexColor", 1); m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0.2f); m.SetFloat("_Smoothness", 0.6f);
                m.SetFloat("_EmissionStrength", 1.8f);
            });
            mats["Logo"] = HullMat("LogoVoxel", m =>
            {
                m.SetFloat("_UseVertexColor", 1); m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0.5f); m.SetFloat("_Smoothness", 0.75f);
                m.SetFloat("_EmissionStrength", 1.1f); m.SetFloat("_EmissionPulse", 0.4f);
            });
            lib.bunker = HullMat("Bunker", m =>
            {
                m.SetFloat("_UseVertexColor", 1); m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.25f);
                m.SetFloat("_EmissionStrength", 3f); m.SetFloat("_EmissionPulse", 0.5f);
                m.SetFloat("_NoiseScale", 5f); m.SetFloat("_NoiseAmount", 0.45f);
                m.SetFloat("_Bumpiness", 1f);
            });
            lib.playerHull = HullMat("PlayerHull", m =>
            {
                m.SetFloat("_UseVertexColor", 1); m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0.75f); m.SetFloat("_Smoothness", 0.78f);
                m.SetFloat("_EmissionStrength", 4f);
                m.SetFloat("_PanelScale", 3.2f); m.SetFloat("_PanelDepth", 0.45f);
                m.SetColor("_RimColor", new Color(0.3f, 0.6f, 1.2f)); m.SetFloat("_Rim", 0.35f);
                m.SetColor("_FlashColor", new Color(3f, 1.2f, 0.6f));
            });
            lib.bossHull = HullMat("BossHull", m =>
            {
                m.SetFloat("_UseVertexColor", 1); m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0.8f); m.SetFloat("_Smoothness", 0.55f);
                m.SetFloat("_EmissionStrength", 4f); m.SetFloat("_EmissionPulse", 1f);
                m.SetColor("_FlashColor", new Color(3f, 1.5f, 0.8f));
            });
            mats["Moon"] = HullMat("Moon", m =>
            {
                m.SetColor("_BaseColor", new Color(0.62f, 0.6f, 0.58f));
                m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.1f);
                m.SetFloat("_NoiseScale", 3f); m.SetFloat("_NoiseAmount", 0.55f); m.SetFloat("_Bumpiness", 1.5f);
            });
            var earth = new Material(Shader.Find("Orbital/Earth")) { name = "Earth" };
            SaveAsset(earth, Gen + "Earth.mat");
            mats["Earth"] = earth;
            var atmo = new Material(Shader.Find("Orbital/Atmosphere")) { name = "Atmosphere" };
            SaveAsset(atmo, Gen + "Atmosphere.mat");
            mats["Atmosphere"] = atmo;
            var sky = new Material(Shader.Find("Orbital/Sky")) { name = "Sky" };
            SaveAsset(sky, Gen + "Sky.mat");
            mats["Sky"] = sky;

            // alpha-blended, not additive: additive bolts vanish against the bright planet
            lib.playerShot = GlowMat("PlayerShot", null, new Color(1f, 1f, 1f), false, 0.6f);
            lib.shotTrail = GlowMat("ShotTrail", t.Streak, new Color(1f, 1f, 1f), false);
            lib.enemyShot = GlowMat("EnemyShot", null, new Color(1f, 1f, 1f), true, 1.4f);
            lib.fire = GlowMat("Fire", t.Fire, new Color(2.6f, 1.4f, 0.6f));
            lib.ember = GlowMat("Ember", t.Soft, new Color(2.5f, 2.2f, 2f));
            lib.spark = GlowMat("Spark", t.Soft, new Color(3f, 3f, 3f));
            lib.ring = GlowMat("Ring", t.Ring, new Color(1f, 1f, 1f));
            lib.smoke = GlowMat("Smoke", t.Smoke, new Color(0.08f, 0.08f, 0.1f, 0.6f), false);
            lib.streak = GlowMat("Streak", t.Streak, new Color(1.5f, 1.5f, 1.5f));
            lib.engine = GlowMat("Engine", null, new Color(1.2f, 2.2f, 4f), true, 1.6f);
            lib.shield = GlowMat("Shield", t.Hex, new Color(0.3f, 0.6f, 1.5f), true, -1.6f);
            lib.powerup = GlowMat("PowerUpHalo", t.Soft, new Color(1f, 1f, 1f), true, -1.2f);
            lib.reticle = GlowMat("Reticle", t.Reticle, new Color(1f, 1f, 1f));
            lib.dust = GlowMat("Dust", t.Soft, new Color(1.2f, 1.2f, 1.4f));
            mats["SunGlow"] = GlowMat("SunGlow", t.SunGlow, new Color(4f, 3.6f, 3f));
            mats["SunStreak"] = GlowMat("SunStreak", t.SunStreak, new Color(1.2f, 1.3f, 1.8f));
            mats["MoonGlow"] = GlowMat("MoonGlow", t.Soft, new Color(0.06f, 0.06f, 0.07f));
        }

        // ------------------------------------------------------------ pipeline

        static void MakePipeline()
        {
            string rendererPath = Root + "Settings/OrbitalRenderer.asset";
            string urpPath = Root + "Settings/OrbitalURP.asset";
            if (AssetDatabase.LoadMainAssetAtPath(urpPath)) AssetDatabase.DeleteAsset(urpPath);
            if (AssetDatabase.LoadMainAssetAtPath(rendererPath)) AssetDatabase.DeleteAsset(rendererPath);
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, rendererPath);
            renderer.renderingMode = RenderingMode.ForwardPlus;
            EditorUtility.SetDirty(renderer);

            var urp = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(urp, urpPath);
            urp.renderScale = 1f;
            urp.msaaSampleCount = 4;
            urp.supportsHDR = true;
            urp.supportsCameraDepthTexture = true;
            urp.shadowDistance = 80f;
            urp.shadowCascadeCount = 2;
            urp.mainLightShadowmapResolution = 4096;
            var so = new SerializedObject(urp);
            SetProp(so, "m_SoftShadowsSupported", true);
            SetPropInt(so, "m_SoftShadowQuality", 2);
            SetPropInt(so, "m_ColorGradingMode", 1);
            SetPropInt(so, "m_HDRColorBufferPrecision", 1);
            SetProp(so, "m_ShadowDepthBias", 0.8f);
            SetProp(so, "m_ShadowNormalBias", 0.6f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
            QualitySettings.vSyncCount = 1;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            AssetDatabase.SaveAssets();
        }

        static void SetProp(SerializedObject so, string name, float v) { var p = so.FindProperty(name); if (p != null) p.floatValue = v; else Debug.LogWarning("Missing prop " + name); }
        static void SetProp(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else Debug.LogWarning("Missing prop " + name); }
        static void SetPropInt(SerializedObject so, string name, int v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning("Missing prop " + name); return; }
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueFlag = v; else p.intValue = v;
        }

        static VolumeProfile MakeVolumeProfile()
        {
            string path = Root + "Settings/OrbitalGrade.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(0.9f); bloom.intensity.Override(1.15f); bloom.scatter.Override(0.72f); bloom.highQualityFiltering.Override(true);
            var vig = profile.Add<Vignette>(); vig.intensity.Override(0.3f); vig.smoothness.Override(0.5f);
            var ca = profile.Add<ColorAdjustments>(); ca.postExposure.Override(0.15f); ca.contrast.Override(12f); ca.saturation.Override(10f);
            var split = profile.Add<SplitToning>(); split.shadows.Override(new Color(0.35f, 0.45f, 0.65f)); split.highlights.Override(new Color(0.75f, 0.62f, 0.5f)); split.balance.Override(-10f);
            profile.Add<ChromaticAberration>().intensity.Override(0.08f);
            profile.Add<LensDistortion>().intensity.Override(0f);
            var grain = profile.Add<FilmGrain>(); grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(0.12f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------ scene

        static GameObject Obj(string name, Mesh mesh, Material mat, Vector3 pos, float scale, Transform parent = null, bool shadows = false)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = shadows;
            return go;
        }

        static void BuildScene(OrbitalLibrary lib, Tex tex, VolumeProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.06f, 0.08f, 0.14f);
            RenderSettings.ambientEquatorColor = new Color(0.04f, 0.05f, 0.08f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.09f, 0.16f);

            // camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 3500f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            camGo.AddComponent<AudioListener>();
            var cd = camGo.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;
            cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cd.antialiasingQuality = AntialiasingQuality.High;
            camGo.AddComponent<CameraRig>().Cam = cam;
            camGo.transform.position = new Vector3(0, 2.5f, -12f);

            // sun + earthshine
            Vector3 sunPos = new Vector3(620f, 260f, 760f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.88f);
            sun.intensity = 2.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.LookRotation(-sunPos.normalized);
            var fill = new GameObject("Earthshine").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.35f, 0.55f, 1f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(-70f, 10f, 0f);
            // key light from behind the camera so everything we look at is front-lit
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.95f, 0.92f, 1f);
            key.intensity = 1.0f;
            key.shadows = LightShadows.None;
            key.transform.rotation = Quaternion.Euler(22f, -12f, 0f);

            var space = new GameObject("Space").transform;
            Obj("Sky", lib.sphere, mats["Sky"], Vector3.zero, 3000f, space);
            var earth = Obj("Earth", earthMesh, mats["Earth"], new Vector3(0, -184f, 75f), 170f, space);
            Obj("Atmosphere", earthMesh, mats["Atmosphere"], new Vector3(0, -184f, 75f), 178f, space);
            var moon = Obj("Moon", lib.sphere, mats["Moon"], new Vector3(-260f, 120f, 820f), 70f, space);
            var sunGlow = Obj("SunGlow", lib.quad, mats["SunGlow"], sunPos, 420f, space);
            var sunStreak = Obj("SunStreak", lib.quad, mats["SunStreak"], sunPos, 1f, space);
            sunStreak.transform.localScale = new Vector3(1400f, 1f, 26f);

            var logo = Obj("Logo", logoMesh, mats["Logo"], new Vector3(0, 10.4f, 12f), 1f, null, true);

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            var root = new GameObject("Orbital");
            root.AddComponent<SoundBank>();
            root.AddComponent<PostFx>().Volume = vol;
            root.AddComponent<Fx>();
            var scn = root.AddComponent<SpaceScene>();
            scn.Earth = earth.transform;
            scn.Moon = moon.transform;
            scn.Billboards = new[] { sunGlow.transform, sunStreak.transform };
            new GameObject("Formation").AddComponent<Formation>();
            new GameObject("Player").AddComponent<Player>();
            new GameObject("UFO").AddComponent<Ufo>();
            new GameObject("Mothership").AddComponent<Boss>();
            var game = root.AddComponent<Game>();
            game.Logo = logo.transform;
            root.AddComponent<GameUI>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
