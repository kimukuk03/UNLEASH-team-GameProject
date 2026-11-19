using System.Collections.Generic;
using System.IO;
using CowMeat.Destruction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CowMeat.City.EditorTools
{
    /// <summary>
    /// Generates the expanded 600 x 600 city into its own scene (CityMapLarge). CityMap and its generator are untouched;
    /// palette/props/lighting helpers are shared with CityGenerator.
    /// Layout (per axis, mirrored):  avenue |0..38| (76 wide)  block 38..140  street 140..176 (36 wide)  block 176..262  ring 262..300.
    /// 16 blocks, the 4 inner corner lots stay open plazas => central arena ~170 m across for the 50 m robot / 60 m kaiju.
    /// A few buildings around the arena are physics-destructible (DestructibleStructure); the rest stay section-based.
    /// </summary>
    public static class CityGeneratorLarge
    {
        public const string ScenePath = "Assets/_Project/Scenes/CityMapLarge.unity";
        private const string RootName = "CityRoot";
        private const int Seed = 2611;

        private const float MapHalf = 300f;
        private const float RingInner = 262f;
        private const float AvenueHalf = 38f;
        private const float StreetCenter = 158f;
        private const float StreetHalf = 18f;
        private const float SidewalkHeight = 0.25f;
        private const float SidewalkWidth = 6f;
        private const float LineY = 0.02f;
        private const float MinHeight = 90f, MaxHeight = 250f;
        private const int DestructibleCount = 5;
        private const float DestructibleMaxHeight = 160f;

        private static readonly Vector2[] Bands = { new Vector2(AvenueHalf, StreetCenter - StreetHalf), new Vector2(StreetCenter + StreetHalf, RingInner) };

        private struct Placed
        {
            public BuildingSpec spec;
            public int style;
            public string block;
        }

        [MenuItem("Tools/City/4. Create Large City Scene (600x600)", priority = 20)]
        public static void CreateSceneAndGenerate()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (active.isDirty)
                {
                    Debug.LogWarning($"[CityGeneratorLarge] '{active.name}' has unsaved changes. Save it first, then run again.");
                    return;
                }
                if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                else
                {
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
            }
            Build();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log($"[CityGeneratorLarge] Saved {ScenePath}");
        }

        private static void Build()
        {
            int buildingLayer = CityGenerator.EnsureLayer("Building");
            int groundLayer = CityGenerator.EnsureLayer("Ground");
            CityGenerator.EnsureLayer("Debris");
            var pal = CityGenerator.BuildPalette();

            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == RootName || go.name == "DebrisManager") Undo.DestroyObjectImmediate(go);

            var rng = new System.Random(Seed);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Generate Large City");
            var env = CityGenerator.Child(root.transform, "Environment");
            var roads = CityGenerator.Child(env, "Roads");
            var markings = CityGenerator.Child(roads, "Markings");
            var sidewalks = CityGenerator.Child(env, "Sidewalks");
            var plazas = CityGenerator.Child(env, "Plazas");
            var buildingsRoot = CityGenerator.Child(root.transform, "Buildings");
            var destructRoot = CityGenerator.Child(root.transform, "DestructibleBuildings");
            var props = CityGenerator.Child(root.transform, "Props");
            var backdrop = CityGenerator.Child(root.transform, "Backdrop");
            var bounds = CityGenerator.Child(root.transform, "Bounds");
            var markers = CityGenerator.Child(root.transform, "Markers");

            var ground = PrimitiveBuilder.Box("Ground_Asphalt", roads, new Vector3(0f, -0.5f, 0f), new Vector3(1200f, 1f, 1200f), pal.asphalt, true);
            ground.layer = groundLayer;

            BuildMarkings(markings, pal);

            // ---- blocks & buildings
            var placed = new List<Placed>();
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            for (int bx = 0; bx < Bands.Length; bx++)
            for (int bz = 0; bz < Bands.Length; bz++)
            {
                float x0 = sx * Bands[bx].x, x1 = sx * Bands[bx].y, z0 = sz * Bands[bz].x, z1 = sz * Bands[bz].y;
                var rect = Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(z0, z1), Mathf.Max(x0, x1), Mathf.Max(z0, z1));
                string name = $"{(sz > 0 ? "N" : "S")}{(sx > 0 ? "E" : "W")}_{bx}{bz}";
                // Inner blocks leave the lot touching the central intersection open (arena).
                Vector2Int? plaza = bx == 0 && bz == 0 ? new Vector2Int(sx > 0 ? 0 : 1, sz > 0 ? 0 : 1) : (Vector2Int?)null;
                BuildBlock(name, rect, plaza, sidewalks, plazas, pal, rng, placed, groundLayer);
            }

            // ---- pick destructible buildings: nearest to the arena, one per block
            var order = new List<int>();
            for (int i = 0; i < placed.Count; i++) order.Add(i);
            order.Sort((a, b) => Flat(placed[a].spec.position).CompareTo(Flat(placed[b].spec.position)));
            var chosen = new HashSet<int>();
            var usedBlocks = new HashSet<string>();
            foreach (int i in order)
            {
                if (chosen.Count >= DestructibleCount) break;
                var sp = placed[i].spec;
                if (Mathf.Min(sp.footprint.x, sp.footprint.y) < 22f || !usedBlocks.Add(placed[i].block)) continue;
                chosen.Add(i);
            }

            var interior = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Destruction/Interior_Concrete.mat");
            var columnMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Destruction/Structure_Column.mat");
            var structures = new List<DestructibleStructure>();
            int pieceTotal = 0;
            for (int i = 0; i < placed.Count; i++)
            {
                var p = placed[i];
                if (!chosen.Contains(i))
                {
                    BuildingFactory.Create(p.spec, pal.buildingSets[p.style], buildingsRoot, buildingLayer);
                    continue;
                }
                var spec = p.spec;
                spec.id = spec.id + "_DESTRUCT";
                var s = DestructibleTowerBuilder.Settings.Default;
                s.width = Mathf.Round(spec.footprint.x);
                s.depth = Mathf.Round(spec.footprint.y);
                s.levels = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(spec.height, DestructibleMaxHeight) / s.levelHeight), 9, 16);
                s.panelsPerFace = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(s.width, s.depth) / 11f), 2, 4);
                spec.maxHealth = Mathf.Max(100f, Mathf.Round(s.width * s.depth * s.levels * s.levelHeight / 1000f) * 10f);
                var facade = pal.facades[p.style];
                var st = DestructibleTowerBuilder.CreateBuilding(spec, s, destructRoot, facade, columnMat ? columnMat : facade,
                                                                  interior ? interior : facade, buildingLayer);
                structures.Add(st);
                pieceTotal += st.Pieces.Count;
            }

            BuildStreetLights(CityGenerator.Child(props, "StreetLights"), pal);
            int cars = BuildCars(CityGenerator.Child(props, "Cars"), pal, rng);
            int backdropCount = BuildBackdrop(backdrop, pal, rng);
            BuildBounds(bounds);
            CityGenerator.AddMarker(markers, "CombatZone", Vector3.zero, 0f, MapMarker.MarkerType.CombatZone, 110f);
            CityGenerator.AddMarker(markers, "PlayerSpawn", new Vector3(0f, 0f, -150f), 0f, MapMarker.MarkerType.PlayerSpawn, 10f);
            CityGenerator.AddMarker(markers, "MonsterSpawn", new Vector3(0f, 0f, 220f), 180f, MapMarker.MarkerType.MonsterSpawn, 25f);

            CityGenerator.SetStatic(env.gameObject);
            CityGenerator.SetStatic(backdrop.gameObject);

            var tracker = root.AddComponent<CityDamageTracker>();
            tracker.Collect();

            // Debris manager (shared FX assets with the destruction test scene)
            var dmGo = new GameObject("DebrisManager");
            Undo.RegisterCreatedObjectUndo(dmGo, "Debris Manager");
            var dm = dmGo.AddComponent<DebrisManager>();
            dm.dustMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/FX/FX_Dust.mat");
            dm.chipMaterial = interior;

            CityGenerator.SetupLightingAndCamera();
            RenderSettings.fogDensity = 0.0012f;
            var cam = Camera.main ? Camera.main : Object.FindObjectOfType<Camera>();
            cam.transform.position = new Vector3(0f, 150f, -300f);
            cam.transform.LookAt(new Vector3(0f, 20f, 20f));
            cam.farClipPlane = 3000f;
            var tester = cam.GetComponent<CityDestructionTester>();
            if (!tester) tester = cam.gameObject.AddComponent<CityDestructionTester>();
            tester.structures = structures;
            tester.tracker = tracker;

            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CityGeneratorLarge] 600x600 city: {placed.Count} buildings ({structures.Count} physics-destructible, {pieceTotal} pieces), " +
                      $"{cars} cars, {backdropCount} backdrop towers.");
        }

        private static float Flat(Vector3 p) => new Vector2(p.x, p.z).magnitude;

        // ---------------------------------------------------------------- blocks

        private static void BuildBlock(string name, Rect r, Vector2Int? plazaLot, Transform sidewalks, Transform plazas,
                                       CityGenerator.Palette pal, System.Random rng, List<Placed> placed, int groundLayer)
        {
            var slab = PrimitiveBuilder.Box($"Sidewalk_{name}", sidewalks, new Vector3(r.center.x, SidewalkHeight * 0.5f, r.center.y),
                                            new Vector3(r.width, SidewalkHeight, r.height), pal.sidewalk, true);
            slab.layer = groundLayer;

            var inner = new Rect(r.xMin + SidewalkWidth, r.yMin + SidewalkWidth, r.width - SidewalkWidth * 2f, r.height - SidewalkWidth * 2f);
            const int cols = 2, rows = 2;
            float lotW = inner.width / cols, lotD = inner.height / rows;
            for (int c = 0; c < cols; c++)
            for (int row = 0; row < rows; row++)
            {
                var lot = new Rect(inner.xMin + c * lotW, inner.yMin + row * lotD, lotW, lotD);
                if (plazaLot.HasValue && plazaLot.Value == new Vector2Int(c, row))
                {
                    PrimitiveBuilder.Box($"Plaza_{name}", plazas, new Vector3(lot.center.x, SidewalkHeight + 0.01f, lot.center.y),
                                         new Vector3(lot.width + 6f, 0.02f, lot.height + 6f), pal.plaza);
                    continue;
                }
                PlaceInLot(name, lot, rng, placed, pal.facades.Length);
            }
        }

        private static void PlaceInLot(string block, Rect lot, System.Random rng, List<Placed> placed, int styleCount)
        {
            const float gap = 3f;
            var avail = new Rect(lot.xMin + gap, lot.yMin + gap, lot.width - gap * 2f, lot.height - gap * 2f);
            var parcels = new List<Rect> { avail };
            if (avail.width >= 40f && avail.height >= 40f && rng.NextDouble() < 0.35)
            {
                parcels.Clear();
                const float split = 4f;
                float w = (avail.width - split) * 0.5f;
                parcels.Add(new Rect(avail.xMin, avail.yMin, w, avail.height));
                parcels.Add(new Rect(avail.xMin + w + split, avail.yMin, w, avail.height));
            }

            foreach (var p in parcels)
            {
                float fx = CityGenerator.Snap(Mathf.Min(p.width * CityGenerator.Range(rng, 0.78f, 0.96f), 46f));
                float fz = CityGenerator.Snap(Mathf.Min(p.height * CityGenerator.Range(rng, 0.78f, 0.96f), 46f));
                float px = CityGenerator.Snap(p.xMin + fx * 0.5f + (p.width - fx) * (float)rng.NextDouble());
                float pz = CityGenerator.Snap(p.yMin + fz * 0.5f + (p.height - fz) * (float)rng.NextDouble());

                // Dense high-rise core: tallest around the arena, still >= 90 m at the edge.
                float t = Mathf.InverseLerp(380f, 70f, new Vector2(px, pz).magnitude);
                float h = Mathf.Lerp(105f, 235f, t) * CityGenerator.Range(rng, 0.8f, 1.12f);
                h = Mathf.Clamp(Mathf.Round(h / 5f) * 5f, MinHeight, MaxHeight);

                var spec = new BuildingSpec
                {
                    id = $"LBLD_{placed.Count + 1:000}",
                    position = new Vector3(px, SidewalkHeight, pz),
                    footprint = new Vector2(fx, fz),
                    height = h,
                    archetype = CityGenerator.PickArchetype(rng, fx, fz),
                    seed = rng.Next(),
                    maxHealth = Mathf.Max(100f, Mathf.Round(fx * fz * h / 1000f) * 10f),
                };
                placed.Add(new Placed { spec = spec, style = rng.Next(styleCount), block = block });
            }
        }


        // ---------------------------------------------------------------- roads

        private static readonly float[][] Segments =
        {
            new[] { -RingInner, -(StreetCenter + StreetHalf) }, new[] { -(StreetCenter - StreetHalf), -AvenueHalf },
            new[] { AvenueHalf, StreetCenter - StreetHalf }, new[] { StreetCenter + StreetHalf, RingInner },
        };

        private static void BuildMarkings(Transform parent, CityGenerator.Palette pal)
        {
            foreach (bool alongZ in new[] { true, false })
            {
                string axis = alongZ ? "NS" : "EW";
                foreach (var seg in Segments)
                {
                    // Main avenue (x=0 / z=0), 3 lanes per direction
                    Strip(parent, $"{axis}_CenterA", pal.lineYellow, alongZ, -0.4f, seg[0], seg[1], 0.35f);
                    Strip(parent, $"{axis}_CenterB", pal.lineYellow, alongZ, 0.4f, seg[0], seg[1], 0.35f);
                    Strip(parent, $"{axis}_EdgeA", pal.lineWhite, alongZ, -AvenueHalf + 2f, seg[0], seg[1], 0.35f);
                    Strip(parent, $"{axis}_EdgeB", pal.lineWhite, alongZ, AvenueHalf - 2f, seg[0], seg[1], 0.35f);
                    foreach (float lane in new[] { -24.5f, -12.5f, 12.5f, 24.5f }) Dashed(parent, $"{axis}_Lane", pal.lineWhite, alongZ, lane, seg[0], seg[1]);
                    // Secondary streets
                    foreach (float c in new[] { -StreetCenter, StreetCenter })
                    {
                        Strip(parent, $"{axis}_Street_Center", pal.lineYellow, alongZ, c, seg[0], seg[1], 0.35f);
                        Dashed(parent, $"{axis}_Street_Lane", pal.lineWhite, alongZ, c - 8.5f, seg[0], seg[1]);
                        Dashed(parent, $"{axis}_Street_Lane", pal.lineWhite, alongZ, c + 8.5f, seg[0], seg[1]);
                    }
                }
                // Ring road
                float ringC = (RingInner + MapHalf) * 0.5f;
                Dashed(parent, $"{axis}_Ring", pal.lineWhite, alongZ, ringC, -MapHalf, MapHalf);
                Dashed(parent, $"{axis}_Ring", pal.lineWhite, alongZ, -ringC, -MapHalf, MapHalf);
                // Crosswalks at the central intersection
                foreach (float a in new[] { AvenueHalf + 5f, -AvenueHalf - 5f })
                    for (int i = 0; i < 22; i++)
                        Strip(parent, $"{axis}_Crosswalk", pal.lineWhite, alongZ, -34.5f + i * 3.3f, a - 4f, a + 4f, 1.7f);
            }
        }

        private static void Strip(Transform p, string name, Material m, bool alongZ, float cross, float a0, float a1, float width)
        {
            float mid = (a0 + a1) * 0.5f, len = Mathf.Abs(a1 - a0);
            var pos = alongZ ? new Vector3(cross, LineY, mid) : new Vector3(mid, LineY, cross);
            var size = alongZ ? new Vector3(width, 0.02f, len) : new Vector3(len, 0.02f, width);
            PrimitiveBuilder.Box(name, p, pos, size, m);
        }

        private static void Dashed(Transform p, string name, Material m, bool alongZ, float cross, float a0, float a1, float dash = 6f, float gap = 9f)
        {
            for (float a = Mathf.Min(a0, a1) + gap * 0.5f; a + dash <= Mathf.Max(a0, a1); a += dash + gap)
                Strip(p, name, m, alongZ, cross, a, a + dash, 0.4f);
        }

        private static bool InIntersection(float a) => Mathf.Abs(a) < AvenueHalf + 6f || Mathf.Abs(Mathf.Abs(a) - StreetCenter) < StreetHalf + 6f || Mathf.Abs(a) > RingInner - 6f;

        // ---------------------------------------------------------------- props

        private static void BuildStreetLights(Transform parent, CityGenerator.Palette pal)
        {
            int n = 0;
            foreach (bool alongZ in new[] { true, false })
            for (float a = -RingInner + 15f; a <= RingInner - 15f; a += 32f)
            {
                if (InIntersection(a)) continue;
                foreach (float side in new[] { 1f, -1f })
                {
                    float cross = side * (AvenueHalf + 2.5f);
                    var pos = alongZ ? new Vector3(cross, SidewalkHeight, a) : new Vector3(a, SidewalkHeight, cross);
                    var toRoad = alongZ ? new Vector3(-side, 0f, 0f) : new Vector3(0f, 0f, -side);
                    CityGenerator.CreateStreetLight(parent, $"StreetLight_{n++:000}", pos, toRoad, pal);
                }
            }
        }

        private static int BuildCars(Transform parent, CityGenerator.Palette pal, System.Random rng)
        {
            int n = 0;
            foreach (bool alongZ in new[] { true, false })
            foreach (float cross in new[] { -31f, -18.5f, 18.5f, 31f })
            {
                float a = -RingInner + CityGenerator.Range(rng, 5f, 25f);
                while (a < RingInner - 10f)
                {
                    if (!InIntersection(a) && rng.NextDouble() < 0.45)
                    {
                        var pos = alongZ ? new Vector3(cross, 0f, a) : new Vector3(a, 0f, cross);
                        float yaw = (alongZ ? 0f : 90f) + (cross > 0f ? 180f : 0f);
                        CityGenerator.CreateCar(parent, $"Car_{n++:000}", pos, yaw, pal.cars[rng.Next(pal.cars.Length)], pal);
                    }
                    a += CityGenerator.Range(rng, 16f, 34f);
                }
            }
            return n;
        }

        private static int BuildBackdrop(Transform parent, CityGenerator.Palette pal, System.Random rng)
        {
            int n = 0;
            const float step = 40f;
            for (float x = -540f; x <= 540f; x += step)
            for (float z = -540f; z <= 540f; z += step)
            {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < 330f || rng.NextDouble() < 0.25) continue;
                float fx = CityGenerator.Snap(CityGenerator.Range(rng, 22f, 34f)), fz = CityGenerator.Snap(CityGenerator.Range(rng, 22f, 34f));
                float h = Mathf.Round(CityGenerator.Range(rng, 80f, 280f) / 5f) * 5f;
                var pos = new Vector3(x + CityGenerator.Range(rng, -6f, 6f), h * 0.5f, z + CityGenerator.Range(rng, -6f, 6f));
                if (Mathf.Abs(pos.x) - fx * 0.5f < MapHalf + 8f && Mathf.Abs(pos.z) - fz * 0.5f < MapHalf + 8f) continue;
                PrimitiveBuilder.Box($"Backdrop_{n++:000}", parent, pos, new Vector3(fx, h, fz), pal.facades[rng.Next(pal.facades.Length)]);
            }
            return n;
        }

        private static void BuildBounds(Transform parent)
        {
            const float edge = MapHalf + 3f, h = 400f, thick = 2f, len = MapHalf * 2f + 10f;
            CityGenerator.AddWall(parent, "Bound_N", new Vector3(0f, h * 0.5f, edge), new Vector3(len, h, thick));
            CityGenerator.AddWall(parent, "Bound_S", new Vector3(0f, h * 0.5f, -edge), new Vector3(len, h, thick));
            CityGenerator.AddWall(parent, "Bound_E", new Vector3(edge, h * 0.5f, 0f), new Vector3(thick, h, len));
            CityGenerator.AddWall(parent, "Bound_W", new Vector3(-edge, h * 0.5f, 0f), new Vector3(thick, h, len));
        }
    }
}
