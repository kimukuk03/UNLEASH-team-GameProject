using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CowMeat.City.EditorTools
{
    /// <summary>
    /// Generates the prototype city (≈300x300) into the active scene under a single "CityRoot".
    /// Re-running deletes the previous CityRoot first, so nothing is duplicated.
    /// Layout is deterministic (fixed seed).
    /// </summary>
    public static class CityGenerator
    {
        public const string ScenePath = "Assets/_Project/Scenes/CityMap.unity";
        public const string RootName = "CityRoot";
        private const string MaterialFolder = "Assets/_Project/Materials/City";
        public const string CatalogPath = "Assets/_Project/Settings/BuildingVisualCatalog.asset";
        private const string FacadeShaderName = "CowMeat/BuildingFacade";
        private const int Seed = 1118;

        private const float MapHalf = 150f;
        private const float SidewalkHeight = 0.25f;
        private const float SidewalkWidth = 5f;
        private const float AvenueHalf = 20f;      // main avenues are 40 wide
        private const float RingInner = 134f;      // ring road 134..150
        private const float LineY = 0.02f;
        private const float LineThickness = 0.02f;

        // ---------------------------------------------------------------- layout data

        private struct BlockDef
        {
            public string name;
            public Rect rect;          // x = X, y = Z
            public int cols, rows;
            public Vector2Int[] plazaLots; // lots left open as plaza (combat pockets)

            public BlockDef(string n, float xMin, float zMin, float xMax, float zMax, int c, int r, params Vector2Int[] plaza)
            {
                name = n; rect = Rect.MinMaxRect(xMin, zMin, xMax, zMax); cols = c; rows = r; plazaLots = plaza;
            }
        }

        // 6 blocks. Main avenues on x=0 / z=0 (40 wide), secondary streets at x=±77 (14 wide) in the north,
        // ring road around the edge. Inner-corner lots near the center stay open => large central arena.
        private static readonly BlockDef[] Blocks =
        {
            new BlockDef("NW1", -134f,   20f,  -84f,  134f, 1, 3),
            new BlockDef("NW2",  -70f,   20f,  -20f,  134f, 1, 3, new Vector2Int(0, 0)),
            new BlockDef("NE1",   20f,   20f,   70f,  134f, 1, 3, new Vector2Int(0, 0)),
            new BlockDef("NE2",   84f,   20f,  134f,  134f, 1, 3),
            new BlockDef("SW",  -134f, -134f,  -20f,  -20f, 2, 2, new Vector2Int(1, 1)),
            new BlockDef("SE",    20f, -134f,  134f,  -20f, 2, 2, new Vector2Int(0, 1)),
        };

        private struct FacadeStyle
        {
            public string name;
            public Color wall, window;
            public Vector2 windowSize;
            public float bay, lit, wallSmooth, metallic;

            public FacadeStyle(string n, Color w, Color win, Vector2 size, float bayWidth, float litRatio, float smooth, float metal)
            {
                name = n; wall = w; window = win; windowSize = size; bay = bayWidth; lit = litRatio; wallSmooth = smooth; metallic = metal;
            }
        }

        private static readonly FacadeStyle[] FacadeStyles =
        {
            new FacadeStyle("GlassBlue",   new Color(0.18f, 0.24f, 0.30f), new Color(0.10f, 0.16f, 0.24f), new Vector2(0.90f, 0.82f), 2.5f, 0.15f, 0.60f, 0.60f),
            new FacadeStyle("Concrete",    new Color(0.60f, 0.59f, 0.56f), new Color(0.07f, 0.09f, 0.12f), new Vector2(0.55f, 0.50f), 3.5f, 0.25f, 0.20f, 0.30f),
            new FacadeStyle("DarkSteel",   new Color(0.11f, 0.12f, 0.14f), new Color(0.10f, 0.12f, 0.15f), new Vector2(0.80f, 0.65f), 3.0f, 0.20f, 0.50f, 0.50f),
            new FacadeStyle("Sandstone",   new Color(0.70f, 0.60f, 0.47f), new Color(0.08f, 0.08f, 0.09f), new Vector2(0.45f, 0.55f), 3.0f, 0.28f, 0.15f, 0.30f),
            new FacadeStyle("TealGlass",   new Color(0.16f, 0.33f, 0.35f), new Color(0.08f, 0.20f, 0.22f), new Vector2(0.92f, 0.86f), 2.0f, 0.12f, 0.70f, 0.70f),
            new FacadeStyle("WhiteModern", new Color(0.83f, 0.84f, 0.86f), new Color(0.06f, 0.08f, 0.11f), new Vector2(0.62f, 0.42f), 4.0f, 0.22f, 0.35f, 0.40f),
        };

        internal class Palette
        {
            public Material asphalt, sidewalk, plaza, lineWhite, lineYellow, roofDetail, beacon, lampHead, metal, carGlass;
            public Material[] facades;
            public Material[] cars;
            public BuildingMaterialSet[] buildingSets;
        }

        // ---------------------------------------------------------------- menu

        [MenuItem("Tools/City/1. Create City Scene + Generate", priority = 0)]
        public static void CreateSceneAndGenerate()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (active.isDirty)
                {
                    Debug.LogWarning($"[CityGenerator] '{active.name}' has unsaved changes. Save it first, then run again.");
                    return;
                }

                if (File.Exists(ScenePath))
                {
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                }
                else
                {
                    EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
            }

            BuildCity();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log($"[CityGenerator] Saved {ScenePath}");
        }

        [MenuItem("Tools/City/2. Regenerate City In Active Scene", priority = 1)]
        public static void RegenerateInActiveScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path != ScenePath &&
                !EditorUtility.DisplayDialog("City Generator", $"Active scene is '{active.name}', not the city scene. Generate city here anyway?", "Generate", "Cancel"))
                return;
            BuildCity();
        }

        [MenuItem("Tools/City/3. Clear City In Active Scene", priority = 2)]
        public static void ClearInActiveScene()
        {
            int removed = ClearExisting();
            Debug.Log($"[CityGenerator] Removed {removed} '{RootName}' object(s).");
        }

        // ---------------------------------------------------------------- main build

        private static void BuildCity()
        {
            int buildingLayer = EnsureLayer("Building");
            int groundLayer = EnsureLayer("Ground");
            var pal = BuildPalette();

            ClearExisting();
            var rng = new System.Random(Seed);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Generate City");

            var env = Child(root.transform, "Environment");
            var roadsRoot = Child(env, "Roads");
            var markings = Child(roadsRoot, "Markings");
            var sidewalks = Child(env, "Sidewalks");
            var plazas = Child(env, "Plazas");
            var buildingsRoot = Child(root.transform, "Buildings");
            var props = Child(root.transform, "Props");
            var backdrop = Child(root.transform, "Backdrop");
            var bounds = Child(root.transform, "Bounds");
            var markers = Child(root.transform, "Markers");

            // Ground (all asphalt; blocks are raised sidewalks on top)
            var ground = PrimitiveBuilder.Box("Ground_Asphalt", roadsRoot, new Vector3(0f, -0.5f, 0f), new Vector3(700f, 1f, 700f), pal.asphalt, true);
            ground.layer = groundLayer;

            BuildRoadMarkings(markings, pal);

            var specs = new List<(BuildingSpec spec, int style)>();
            foreach (var block in Blocks)
                BuildBlock(block, sidewalks, plazas, pal, rng, specs, groundLayer);

            // External visuals: catalog entries (by building ID) are re-applied on every generation.
            var catalog = LoadOrCreateCatalog();
            int externalCount = 0;
            foreach (var (baseSpec, style) in specs)
            {
                var profile = catalog ? catalog.Find(baseSpec.id) : null;
                var spec = profile ? AdaptSpecToProfile(baseSpec, profile) : baseSpec;
                var building = BuildingFactory.Create(spec, pal.buildingSets[style], buildingsRoot, buildingLayer);
                if (profile && building.ApplyVisual(profile)) externalCount++;
            }
            if (externalCount > 0) Debug.Log($"[CityGenerator] Applied {externalCount} external building visual(s) from catalog.");

            BuildStreetLights(Child(props, "StreetLights"), pal);
            int carCount = BuildCars(Child(props, "Cars"), pal, rng);
            int backdropCount = BuildBackdrop(backdrop, pal, rng);
            BuildBounds(bounds);
            BuildMarkers(markers);

            SetStatic(env.gameObject);
            SetStatic(backdrop.gameObject);

            var tracker = root.AddComponent<CityDamageTracker>();
            tracker.Collect();

            SetupLightingAndCamera();
            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[CityGenerator] City generated: {Blocks.Length} blocks, {specs.Count} destructible buildings, " +
                      $"{carCount} cars, {backdropCount} backdrop towers.");
        }

        private static int ClearExisting()
        {
            int count = 0;
            var scene = SceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name != RootName) continue;
                Undo.DestroyObjectImmediate(go);
                count++;
            }
            return count;
        }

        // ---------------------------------------------------------------- blocks & buildings

        private static void BuildBlock(BlockDef block, Transform sidewalks, Transform plazas, Palette pal, System.Random rng,
                                       List<(BuildingSpec, int)> specs, int groundLayer)
        {
            var r = block.rect;
            var slab = PrimitiveBuilder.Box($"Sidewalk_{block.name}", sidewalks,
                new Vector3(r.center.x, SidewalkHeight * 0.5f, r.center.y), new Vector3(r.width, SidewalkHeight, r.height), pal.sidewalk, true);
            slab.layer = groundLayer;

            var inner = new Rect(r.xMin + SidewalkWidth, r.yMin + SidewalkWidth, r.width - SidewalkWidth * 2f, r.height - SidewalkWidth * 2f);
            float lotW = inner.width / block.cols;
            float lotD = inner.height / block.rows;

            for (int c = 0; c < block.cols; c++)
            for (int row = 0; row < block.rows; row++)
            {
                var lot = new Rect(inner.xMin + c * lotW, inner.yMin + row * lotD, lotW, lotD);
                if (System.Array.IndexOf(block.plazaLots, new Vector2Int(c, row)) >= 0)
                {
                    PrimitiveBuilder.Box($"Plaza_{block.name}", plazas,
                        new Vector3(lot.center.x, SidewalkHeight + 0.01f, lot.center.y), new Vector3(lot.width + 4f, 0.02f, lot.height + 4f), pal.plaza);
                    continue;
                }
                PlaceBuildingsInLot(lot, rng, specs);
            }
        }

        private static void PlaceBuildingsInLot(Rect lot, System.Random rng, List<(BuildingSpec, int)> specs)
        {
            const float gap = 3f;
            var avail = new Rect(lot.xMin + gap, lot.yMin + gap, lot.width - gap * 2f, lot.height - gap * 2f);
            var parcels = new List<Rect> { avail };

            // Large lots sometimes get twin towers.
            if (avail.width >= 44f && avail.height >= 44f && rng.NextDouble() < 0.6)
            {
                parcels.Clear();
                const float split = 4f;
                if (rng.NextDouble() < 0.5)
                {
                    float w = (avail.width - split) * 0.5f;
                    parcels.Add(new Rect(avail.xMin, avail.yMin, w, avail.height));
                    parcels.Add(new Rect(avail.xMin + w + split, avail.yMin, w, avail.height));
                }
                else
                {
                    float d = (avail.height - split) * 0.5f;
                    parcels.Add(new Rect(avail.xMin, avail.yMin, avail.width, d));
                    parcels.Add(new Rect(avail.xMin, avail.yMin + d + split, avail.width, d));
                }
            }

            foreach (var p in parcels)
            {
                float fx = Snap(Mathf.Min(p.width * Range(rng, 0.72f, 0.95f), 46f));
                float fz = Snap(Mathf.Min(p.height * Range(rng, 0.72f, 0.95f), 46f));
                float px = Snap(p.xMin + fx * 0.5f + (p.width - fx) * (float)rng.NextDouble());
                float pz = Snap(p.yMin + fz * 0.5f + (p.height - fz) * (float)rng.NextDouble());

                // Taller towers near the center for a dramatic downtown skyline around the arena.
                float dist = new Vector2(px, pz).magnitude;
                float t = Mathf.InverseLerp(200f, 50f, dist);
                float h = Mathf.Lerp(55f, 150f, t) * Range(rng, 0.75f, 1.25f);
                h = Mathf.Clamp(Mathf.Round(h / 4f) * 4f, 40f, 180f);

                var archetype = PickArchetype(rng, fx, fz);
                var spec = new BuildingSpec
                {
                    id = $"BLD_{specs.Count + 1:000}",
                    position = new Vector3(px, SidewalkHeight, pz),
                    footprint = new Vector2(fx, fz),
                    height = h,
                    archetype = archetype,
                    seed = rng.Next(),
                    maxHealth = Mathf.Max(100f, Mathf.Round(fx * fz * h / 1000f) * 10f),
                };
                specs.Add((spec, rng.Next(FacadeStyles.Length)));
            }
        }

        /// <summary>
        /// Makes the colliders match the external asset: the asset is measured, fitted inside the original envelope
        /// (Uniform) and the spec shrinks to the fitted size. Archetype becomes Slab (stacked full-size sections).
        /// Position and ID are unchanged, so layout/IDs stay stable.
        /// </summary>
        private static BuildingSpec AdaptSpecToProfile(BuildingSpec spec, BuildingVisualProfile profile)
        {
            if (!profile.TryGetPrefabBounds(out var b) || b.size.x <= 1e-3f || b.size.y <= 1e-3f || b.size.z <= 1e-3f)
            {
                Debug.LogWarning($"[CityGenerator] Profile '{profile.name}' for {spec.id} has no measurable mesh. Using placeholder size.");
                return spec;
            }

            spec.archetype = BuildingArchetype.Slab;
            switch (profile.fitMode)
            {
                case VisualFitMode.Uniform:
                    float s = Mathf.Min(spec.footprint.x / b.size.x, Mathf.Min(spec.height / b.size.y, spec.footprint.y / b.size.z));
                    spec.footprint = new Vector2(b.size.x * s, b.size.z * s);
                    spec.height = b.size.y * s;
                    break;
                case VisualFitMode.None:
                    if (b.size.x > spec.footprint.x || b.size.z > spec.footprint.y)
                        Debug.LogWarning($"[CityGenerator] {spec.id}: native asset size exceeds its lot footprint.");
                    spec.footprint = new Vector2(b.size.x, b.size.z);
                    spec.height = b.size.y;
                    break;
                // Stretch: keep the envelope; the asset is scaled to it.
            }
            spec.maxHealth = Mathf.Max(100f, Mathf.Round(spec.footprint.x * spec.footprint.y * spec.height / 1000f) * 10f);
            return spec;
        }

        private static BuildingVisualCatalog LoadOrCreateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<BuildingVisualCatalog>(CatalogPath);
            if (catalog) return catalog;
            EnsureFolder(Path.GetDirectoryName(CatalogPath).Replace('\\', '/'));
            catalog = ScriptableObject.CreateInstance<BuildingVisualCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            return catalog;
        }

        internal static BuildingArchetype PickArchetype(System.Random rng, float fx, float fz)
        {
            if (Mathf.Min(fx, fz) < 20f)
                return rng.NextDouble() < 0.6 ? BuildingArchetype.Slab : BuildingArchetype.Spire;
            return (BuildingArchetype)rng.Next(System.Enum.GetValues(typeof(BuildingArchetype)).Length);
        }

        // ---------------------------------------------------------------- roads

        private static void BuildRoadMarkings(Transform parent, Palette pal)
        {
            const float stopA = 30f;
            float[][] segments = { new[] { stopA + 1f, RingInner }, new[] { -RingInner, -(stopA + 1f) } };

            // Main avenues: alongZ = N-S avenue at x=0, !alongZ = E-W avenue at z=0
            foreach (bool alongZ in new[] { true, false })
            {
                string axis = alongZ ? "NS" : "EW";
                foreach (var seg in segments)
                {
                    Strip(parent, $"{axis}_CenterYellowA", pal.lineYellow, alongZ, -0.4f, seg[0], seg[1], 0.3f);
                    Strip(parent, $"{axis}_CenterYellowB", pal.lineYellow, alongZ, 0.4f, seg[0], seg[1], 0.3f);
                    Strip(parent, $"{axis}_EdgeA", pal.lineWhite, alongZ, -AvenueHalf + 1.5f, seg[0], seg[1], 0.3f);
                    Strip(parent, $"{axis}_EdgeB", pal.lineWhite, alongZ, AvenueHalf - 1.5f, seg[0], seg[1], 0.3f);
                    Dashed(parent, $"{axis}_Lane", pal.lineWhite, alongZ, -9.5f, seg[0], seg[1]);
                    Dashed(parent, $"{axis}_Lane", pal.lineWhite, alongZ, 9.5f, seg[0], seg[1]);
                }

                // Crosswalks at both approaches of the central intersection
                foreach (float a in new[] { 25f, -25f })
                {
                    for (int i = 0; i < 12; i++)
                    {
                        float cross = -17.6f + i * 3.2f;
                        Strip(parent, $"{axis}_Crosswalk", pal.lineWhite, alongZ, cross, a - 3.5f, a + 3.5f, 1.6f);
                    }
                }
            }

            // Stop lines (right-hand traffic)
            Strip(parent, "StopLine_N", pal.lineWhite, false, stopA, -AvenueHalf + 1.5f, 0f, 0.6f);
            Strip(parent, "StopLine_S", pal.lineWhite, false, -stopA, 0f, AvenueHalf - 1.5f, 0.6f);
            Strip(parent, "StopLine_E", pal.lineWhite, true, stopA, 0f, AvenueHalf - 1.5f, 0.6f);
            Strip(parent, "StopLine_W", pal.lineWhite, true, -stopA, -AvenueHalf + 1.5f, 0f, 0.6f);

            // Secondary streets (north half) and ring road
            Dashed(parent, "Secondary_W", pal.lineWhite, true, -77f, AvenueHalf, RingInner);
            Dashed(parent, "Secondary_E", pal.lineWhite, true, 77f, AvenueHalf, RingInner);
            const float ringC = (RingInner + MapHalf) * 0.5f;
            Dashed(parent, "Ring_N", pal.lineWhite, false, ringC, -MapHalf, MapHalf);
            Dashed(parent, "Ring_S", pal.lineWhite, false, -ringC, -MapHalf, MapHalf);
            Dashed(parent, "Ring_E", pal.lineWhite, true, ringC, -MapHalf, MapHalf);
            Dashed(parent, "Ring_W", pal.lineWhite, true, -ringC, -MapHalf, MapHalf);
        }

        private static void Strip(Transform p, string name, Material m, bool alongZ, float cross, float a0, float a1, float width)
        {
            float mid = (a0 + a1) * 0.5f, len = Mathf.Abs(a1 - a0);
            var pos = alongZ ? new Vector3(cross, LineY, mid) : new Vector3(mid, LineY, cross);
            var size = alongZ ? new Vector3(width, LineThickness, len) : new Vector3(len, LineThickness, width);
            PrimitiveBuilder.Box(name, p, pos, size, m);
        }

        private static void Dashed(Transform p, string name, Material m, bool alongZ, float cross, float a0, float a1, float dash = 5f, float gap = 7f)
        {
            for (float a = Mathf.Min(a0, a1) + gap * 0.5f; a + dash <= Mathf.Max(a0, a1); a += dash + gap)
                Strip(p, name, m, alongZ, cross, a, a + dash, 0.35f);
        }

        // ---------------------------------------------------------------- props

        private static void BuildStreetLights(Transform parent, Palette pal)
        {
            int n = 0;
            foreach (bool alongZ in new[] { true, false })
            foreach (float dirSign in new[] { 1f, -1f })
            for (float a = 36f; a <= 128f; a += 23f)
            foreach (float side in new[] { 1f, -1f })
            {
                float cross = side * (AvenueHalf + 2.5f);
                var pos = alongZ ? new Vector3(cross, SidewalkHeight, dirSign * a) : new Vector3(dirSign * a, SidewalkHeight, cross);
                var toRoad = alongZ ? new Vector3(-side, 0f, 0f) : new Vector3(0f, 0f, -side);
                CreateStreetLight(parent, $"StreetLight_{n++:00}", pos, toRoad, pal);
            }
        }

        internal static void CreateStreetLight(Transform parent, string name, Vector3 pos, Vector3 toRoad, Palette pal)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 5f, 0f);
            col.size = new Vector3(0.5f, 10f, 0.5f);

            bool armX = Mathf.Abs(toRoad.x) > 0.5f;
            PrimitiveBuilder.Cylinder("Pole", root.transform, new Vector3(0f, 5f, 0f), 0.2f, 10f, pal.metal);
            PrimitiveBuilder.Box("Arm", root.transform, toRoad * 1.5f + Vector3.up * 10f,
                armX ? new Vector3(3f, 0.25f, 0.25f) : new Vector3(0.25f, 0.25f, 3f), pal.metal);
            PrimitiveBuilder.Box("Head", root.transform, toRoad * 3f + Vector3.up * 9.8f,
                armX ? new Vector3(1.4f, 0.25f, 0.6f) : new Vector3(0.6f, 0.25f, 1.4f), pal.lampHead);
        }

        private static int BuildCars(Transform parent, Palette pal, System.Random rng)
        {
            int n = 0;
            foreach (bool alongZ in new[] { true, false })
            foreach (float dirSign in new[] { 1f, -1f })
            foreach (float side in new[] { 1f, -1f })
            {
                float a = 40f + Range(rng, 0f, 15f);
                while (a < 125f)
                {
                    if (rng.NextDouble() < 0.6)
                    {
                        float cross = side * 16.5f;
                        var pos = alongZ ? new Vector3(cross, 0f, dirSign * a) : new Vector3(dirSign * a, 0f, cross);
                        float yaw = (alongZ ? 0f : 90f) + (rng.NextDouble() < 0.5 ? 0f : 180f);
                        CreateCar(parent, $"Car_{n++:00}", pos, yaw, pal.cars[rng.Next(pal.cars.Length)], pal);
                    }
                    a += Range(rng, 14f, 30f);
                }
            }
            return n;
        }

        internal static void CreateCar(Transform parent, string name, Vector3 pos, float yaw, Material body, Palette pal)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.size = new Vector3(2f, 2f, 4.6f);

            PrimitiveBuilder.Box("Body", root.transform, new Vector3(0f, 0.75f, 0f), new Vector3(2f, 1.1f, 4.6f), body);
            PrimitiveBuilder.Box("Cabin", root.transform, new Vector3(0f, 1.6f, -0.2f), new Vector3(1.8f, 0.8f, 2.4f), pal.carGlass);
        }

        private static int BuildBackdrop(Transform parent, Palette pal, System.Random rng)
        {
            // Non-interactive skyline outside the playable map to sell the "megacity" feel.
            int n = 0;
            const float step = 34f;
            for (float x = -272f; x <= 272f; x += step)
            for (float z = -272f; z <= 272f; z += step)
            {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < 168f) continue;
                if (rng.NextDouble() < 0.25) continue;
                float fx = Snap(Range(rng, 18f, 30f)), fz = Snap(Range(rng, 18f, 30f));
                float h = Mathf.Round(Range(rng, 40f, 200f) / 4f) * 4f;
                var pos = new Vector3(Snap(x + Range(rng, -5f, 5f)), h * 0.5f, Snap(z + Range(rng, -5f, 5f)));
                // Keep every backdrop edge outside the boundary walls (inner face at 152) — no walk-through geometry.
                if (Mathf.Abs(pos.x) - fx * 0.5f < 158f && Mathf.Abs(pos.z) - fz * 0.5f < 158f) continue;
                PrimitiveBuilder.Box($"Backdrop_{n++:000}", parent, pos, new Vector3(fx, h, fz), pal.facades[rng.Next(pal.facades.Length)]);
            }
            return n;
        }

        private static void BuildBounds(Transform parent)
        {
            const float edge = MapHalf + 3f, h = 300f, thick = 2f, len = MapHalf * 2f + 10f;
            AddWall(parent, "Bound_N", new Vector3(0f, h * 0.5f, edge), new Vector3(len, h, thick));
            AddWall(parent, "Bound_S", new Vector3(0f, h * 0.5f, -edge), new Vector3(len, h, thick));
            AddWall(parent, "Bound_E", new Vector3(edge, h * 0.5f, 0f), new Vector3(thick, h, len));
            AddWall(parent, "Bound_W", new Vector3(-edge, h * 0.5f, 0f), new Vector3(thick, h, len));
        }

        internal static void AddWall(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        private static void BuildMarkers(Transform parent)
        {
            AddMarker(parent, "CombatZone", Vector3.zero, 0f, MapMarker.MarkerType.CombatZone, 65f);
            AddMarker(parent, "PlayerSpawn", new Vector3(0f, 0f, -70f), 0f, MapMarker.MarkerType.PlayerSpawn, 8f);
            AddMarker(parent, "MonsterSpawn", new Vector3(0f, 0f, 115f), 180f, MapMarker.MarkerType.MonsterSpawn, 20f);
        }

        internal static void AddMarker(Transform parent, string name, Vector3 pos, float yaw, MapMarker.MarkerType type, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MapMarker>().Setup(type, radius);
        }

        // ---------------------------------------------------------------- lighting & camera

        internal static void SetupLightingAndCamera()
        {
            Light sun = RenderSettings.sun;
            if (!sun)
            {
                foreach (var l in Object.FindObjectsOfType<Light>())
                    if (l.type == LightType.Directional) { sun = l; break; }
            }
            if (!sun)
            {
                var go = new GameObject("Directional Light");
                Undo.RegisterCreatedObjectUndo(go, "Create Sun");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            sun.color = new Color(1f, 0.9f, 0.78f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.43f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.18f, 0.20f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0018f;
            RenderSettings.fogColor = new Color(0.68f, 0.72f, 0.78f);

            // Buildings are huge; default shadow distance (150) cuts shadows off in the overview.
            if (QualitySettings.shadowDistance < 450f) QualitySettings.shadowDistance = 450f;

            Camera cam = Camera.main;
            if (!cam) cam = Object.FindObjectOfType<Camera>();
            if (!cam)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                Undo.RegisterCreatedObjectUndo(go, "Create Camera");
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            // Overview from above the south ring road, looking north over the central arena.
            cam.transform.position = new Vector3(0f, 85f, -146f);
            cam.transform.LookAt(new Vector3(0f, 5f, 15f));
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 2000f;
            cam.fieldOfView = 55f;
        }

        // ---------------------------------------------------------------- materials

        internal static Palette BuildPalette()
        {
            EnsureFolder(MaterialFolder);
            var pal = new Palette
            {
                asphalt = Std("Road_Asphalt", new Color(0.16f, 0.16f, 0.17f), 0.25f),
                sidewalk = Std("Sidewalk", new Color(0.55f, 0.55f, 0.53f), 0.15f),
                plaza = Std("Plaza_Paving", new Color(0.64f, 0.59f, 0.52f), 0.2f),
                lineWhite = Std("Road_LineWhite", new Color(0.9f, 0.9f, 0.88f), 0.3f),
                lineYellow = Std("Road_LineYellow", new Color(0.95f, 0.75f, 0.15f), 0.3f),
                roofDetail = Std("Roof_Detail", new Color(0.36f, 0.37f, 0.39f), 0.35f, 0.3f),
                beacon = Std("Roof_Beacon", new Color(0.8f, 0.05f, 0.05f), 0.5f, 0f, new Color(3f, 0.15f, 0.1f)),
                lampHead = Std("StreetLight_Head", new Color(1f, 0.95f, 0.8f), 0.5f, 0f, new Color(1.6f, 1.35f, 0.9f)),
                metal = Std("Metal_Pole", new Color(0.25f, 0.26f, 0.28f), 0.5f, 0.7f),
                carGlass = Std("Car_Glass", new Color(0.05f, 0.07f, 0.1f), 0.9f, 0.3f),
                cars = new[]
                {
                    Std("Car_Red", new Color(0.65f, 0.08f, 0.07f), 0.7f, 0.3f),
                    Std("Car_White", new Color(0.88f, 0.88f, 0.88f), 0.7f, 0.3f),
                    Std("Car_Black", new Color(0.05f, 0.05f, 0.06f), 0.8f, 0.4f),
                    Std("Car_Blue", new Color(0.1f, 0.22f, 0.5f), 0.7f, 0.3f),
                    Std("Car_Taxi", new Color(0.95f, 0.72f, 0.1f), 0.6f, 0.2f),
                },
            };

            var shader = Shader.Find(FacadeShaderName);
            if (!shader)
            {
                Debug.LogWarning($"[CityGenerator] Shader '{FacadeShaderName}' not found, falling back to Standard.");
                shader = Shader.Find("Standard");
            }

            pal.facades = new Material[FacadeStyles.Length];
            pal.buildingSets = new BuildingMaterialSet[FacadeStyles.Length];
            for (int i = 0; i < FacadeStyles.Length; i++)
            {
                var s = FacadeStyles[i];
                var m = GetOrCreateMaterial("Facade_" + s.name, shader);
                m.color = s.wall;
                m.SetColor("_WallColor", s.wall);
                m.SetColor("_RoofColor", new Color(0.20f, 0.21f, 0.23f));
                m.SetColor("_WindowColor", s.window);
                m.SetColor("_WindowLitColor", new Color(1f, 0.82f, 0.55f));
                m.SetFloat("_BayWidth", s.bay);
                m.SetFloat("_FloorHeight", 4f);
                m.SetVector("_WindowSize", new Vector4(s.windowSize.x, s.windowSize.y, 0f, 0f));
                m.SetFloat("_LobbyHeight", 5f);
                m.SetFloat("_LitRatio", s.lit);
                m.SetFloat("_LitIntensity", 0.8f);
                m.SetFloat("_WallSmoothness", s.wallSmooth);
                m.SetFloat("_WindowSmoothness", 0.9f);
                m.SetFloat("_Metallic", s.metallic);
                EditorUtility.SetDirty(m);

                pal.facades[i] = m;
                pal.buildingSets[i] = new BuildingMaterialSet { facade = m, roofDetail = pal.roofDetail, beacon = pal.beacon };
            }
            return pal;
        }

        private static Material Std(string name, Color color, float smoothness, float metallic = 0f, Color? emission = null)
        {
            var m = GetOrCreateMaterial(name, Shader.Find("Standard"));
            m.color = color;
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material GetOrCreateMaterial(string name, Shader shader)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }
            m.enableInstancing = true; // all city geometry is the shared cube mesh -> instanced draws
            return m;
        }

        // ---------------------------------------------------------------- utils

        internal static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        internal static void SetStatic(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        internal static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return 0;
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var p = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(p.stringValue)) continue;
                p.stringValue = layerName;
                so.ApplyModifiedProperties();
                return i;
            }
            Debug.LogWarning($"[CityGenerator] No free layer slot for '{layerName}'. Using Default.");
            return 0;
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        internal static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
        internal static float Snap(float v) => Mathf.Round(v * 2f) / 2f;
    }
}
