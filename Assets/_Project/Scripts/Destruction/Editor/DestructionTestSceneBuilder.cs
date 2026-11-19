using System.IO;
using CowMeat.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CowMeat.Destruction.EditorTools
{
    /// <summary>Creates/rebuilds the destruction test scene (separate from CityMap).</summary>
    public static class DestructionTestSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/DestructionTest.unity";
        private const string RootName = "DestructionTestRoot";
        private const string FxFolder = "Assets/_Project/FX";
        private const string CityMatFolder = "Assets/_Project/Materials/City";
        private const string MatFolder = "Assets/_Project/Materials/Destruction";

        [MenuItem("Tools/Destruction/Create Or Rebuild Test Scene", priority = 0)]
        public static void CreateOrRebuild()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (active.isDirty)
                {
                    Debug.LogWarning($"[DestructionTest] '{active.name}' has unsaved changes. Save it first.");
                    return;
                }
                if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                else
                {
                    var s = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                    EditorSceneManager.SaveScene(s, ScenePath);
                }
            }

            Build();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log($"[DestructionTest] Scene ready: {ScenePath}");
        }

        private static void Build()
        {
            int buildingLayer = EnsureLayer("Building");
            int groundLayer = EnsureLayer("Ground");
            EnsureLayer("Debris");

            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == RootName || go.name == "DebrisManager") Undo.DestroyObjectImmediate(go);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Destruction Test");

            // Materials (reuse city materials when present)
            var facadeWall = LoadOrCreate($"{CityMatFolder}/Facade_Concrete.mat", "Standard", new Color(0.6f, 0.59f, 0.56f));
            var facadeTarget = LoadOrCreate($"{CityMatFolder}/Facade_TealGlass.mat", "Standard", new Color(0.16f, 0.33f, 0.35f));
            var asphalt = LoadOrCreate($"{CityMatFolder}/Road_Asphalt.mat", "Standard", new Color(0.16f, 0.16f, 0.17f));
            var roofDetail = LoadOrCreate($"{CityMatFolder}/Roof_Detail.mat", "Standard", new Color(0.36f, 0.37f, 0.39f));
            var interior = LoadOrCreate($"{MatFolder}/Interior_Concrete.mat", "Standard", new Color(0.52f, 0.5f, 0.47f));
            var column = LoadOrCreate($"{MatFolder}/Structure_Column.mat", "Standard", new Color(0.42f, 0.42f, 0.43f));
            var carMat = LoadOrCreate($"{CityMatFolder}/Car_Red.mat", "Standard", new Color(0.65f, 0.08f, 0.07f));

            // Ground
            var ground = PrimitiveBuilder.Box("Ground", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(600f, 1f, 600f), asphalt, true);
            ground.layer = groundLayer;
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.BatchingStatic);

            // Destructible tower (Building + DestructibleStructure on the same root)
            var settings = DestructibleTowerBuilder.Settings.Default;
            var towerGo = new GameObject("TEST_TOWER_Destructible");
            towerGo.transform.SetParent(root.transform, false);
            towerGo.layer = buildingLayer;
            var building = towerGo.AddComponent<Building>();
            var structure = DestructibleTowerBuilder.Build(towerGo, settings, facadeWall, column, interior, buildingLayer);
            float totalHp = 0f;
            foreach (var p in structure.Pieces) totalHp += p.MaxHealth;
            var pieces = towerGo.transform.Find("Pieces");
            building.Initialize(new BuildingSpec
            {
                id = "TEST_TOWER",
                footprint = new Vector2(settings.width, settings.depth),
                height = settings.levels * settings.levelHeight,
                archetype = BuildingArchetype.Slab,
                maxHealth = totalHp,
            }, pieces, pieces, pieces, new BuildingSection[0]);
            structure.Setup(new System.Collections.Generic.List<DestructiblePiece>(structure.Pieces).ToArray(), settings.levels, building);

            // Target building behind the tower (classic sections) — visible through holes, receives debris damage
            var targetSet = new BuildingMaterialSet { facade = facadeTarget, roofDetail = roofDetail, beacon = null };
            BuildingFactory.Create(new BuildingSpec
            {
                id = "BLD_TARGET", position = new Vector3(0f, 0f, 55f), footprint = new Vector2(44f, 24f), height = 70f,
                archetype = BuildingArchetype.Slab, seed = 7, maxHealth = 3000f,
            }, targetSet, root.transform, buildingLayer);
            // A low neighbour to the side, in range of a collapse
            BuildingFactory.Create(new BuildingSpec
            {
                id = "BLD_SIDE", position = new Vector3(45f, 0f, 0f), footprint = new Vector2(20f, 26f), height = 32f,
                archetype = BuildingArchetype.Slab, seed = 9, maxHealth = 1200f,
            }, targetSet, root.transform, buildingLayer);

            // A few dynamic cars as small physics props
            var props = new GameObject("Props").transform;
            props.SetParent(root.transform, false);
            for (int i = 0; i < 6; i++)
            {
                var car = new GameObject($"Car_{i}");
                car.transform.SetParent(props, false);
                car.transform.position = new Vector3(-24f + i * 9f, 0f, -26f);
                PrimitiveBuilder.Box("Body", car.transform, new Vector3(0f, 0.75f, 0f), new Vector3(2f, 1.1f, 4.6f), carMat);
                var bc = car.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.75f, 0f);
                bc.size = new Vector3(2f, 1.1f, 4.6f);
                car.AddComponent<Rigidbody>().mass = 1.5f;
            }

            var tracker = root.AddComponent<CityDamageTracker>();
            tracker.Collect();

            // Debris manager with FX materials as assets
            var dmGo = new GameObject("DebrisManager");
            Undo.RegisterCreatedObjectUndo(dmGo, "Debris Manager");
            var dm = dmGo.AddComponent<DebrisManager>();
            dm.dustMaterial = CreateDustMaterial();
            dm.chipMaterial = interior;

            SetupCameraAndLight(structure, tracker);
            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log($"[DestructionTest] Tower built: {structure.Pieces.Count} pieces ({settings.levels} levels x {settings.levelHeight} m), total HP {totalHp:0}.");
        }

        private static void SetupCameraAndLight(DestructibleStructure tower, CityDamageTracker tracker)
        {
            var cam = Camera.main ? Camera.main : Object.FindObjectOfType<Camera>();
            if (!cam)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.transform.position = new Vector3(-35f, 48f, -105f);
            cam.transform.LookAt(new Vector3(0f, 45f, 0f));
            cam.farClipPlane = 2000f;
            cam.fieldOfView = 55f;
            var ctrl = cam.GetComponent<DestructionTestController>();
            if (!ctrl) ctrl = cam.gameObject.AddComponent<DestructionTestController>();
            ctrl.tower = tower;
            ctrl.tracker = tracker;

            Light sun = null;
            foreach (var l in Object.FindObjectsOfType<Light>()) if (l.type == LightType.Directional) { sun = l; break; }
            if (!sun)
            {
                sun = new GameObject("Directional Light").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            sun.color = new Color(1f, 0.9f, 0.78f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.43f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.18f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0015f;
            RenderSettings.fogColor = new Color(0.68f, 0.72f, 0.78f);
        }

        private static Material CreateDustMaterial()
        {
            EnsureFolder(FxFolder);
            string texPath = $"{FxFolder}/DustSoft.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (!tex)
            {
                tex = DebrisManager.CreateSoftTexture(64);
                AssetDatabase.CreateAsset(tex, texPath);
            }
            string matPath = $"{FxFolder}/FX_Dust.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!m)
            {
                m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
                AssetDatabase.CreateAsset(m, matPath);
            }
            m.mainTexture = tex;
            m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        private static Material LoadOrCreate(string path, string shader, Color color)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            m = new Material(Shader.Find(shader)) { color = color, enableInstancing = true };
            m.SetFloat("_Glossiness", 0.2f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static int EnsureLayer(string layerName)
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
            return 0;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
