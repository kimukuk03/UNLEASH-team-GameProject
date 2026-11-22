using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace Unleash.Laboratory.EditorTools
{
    /// <summary>
    /// 지하 연구소 맵(튜토리얼 + 중반 전투, 보스방 입구 직전까지) 자동 생성기.
    ///
    /// 메뉴: UNLEASH / Laboratory / Build Map + Scene
    ///  1) 연구소 타일셋 임포트 설정(16 PPU, Point, 시트 슬라이스)
    ///  2) Tile 에셋 / 문 AnimationClip + AnimatorController(+Override) 생성
    ///  3) LabMob 프리팹 Variant (TestMob + 벽 충돌용 콜라이더) 생성
    ///  4) 맵 배치 → LaboratoryMap.prefab 저장 → Laboratory_Prototype.unity 저장
    ///
    /// 다시 실행하면 맵 프리팹/씬을 새로 만든다 (기존 Combat_Test 등은 건드리지 않음).
    /// </summary>
    public static class LaboratoryMapBuilder
    {
        // ───────────────────────── 경로 ─────────────────────────
        public const string Root = "Assets/_Game/Maps/Laboratory";
        public const string GenRoot = Root + "/Generated";
        public const string TileDir = GenRoot + "/Tiles";
        public const string AnimDir = GenRoot + "/Animations";
        public const string PrefabDir = Root + "/Prefabs";
        public const string SceneDir = Root + "/Scenes";
        public const string ScenePath = SceneDir + "/Laboratory_Prototype.unity";
        public const string MapPrefabPath = PrefabDir + "/LaboratoryMap.prefab";
        public const string LabMobPath = PrefabDir + "/Enemies/LabMob.prefab";
        public const string Art = "Assets/_Game/Sprites/Research_Lab_-_Top_Down_RPG_Tileset_[16x16]/Research_Lab_-_Top_Down_RPG_Tileset_[16x16]/16";

        private const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        private const string MobControllerPrefabPath = "Assets/_Game/Prefabs/TestMob/MobController.prefab";
        private const string TestMobPrefabPath = "Assets/_Game/Prefabs/TestMob/TestMob.prefab";
        private const string LitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";
        private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        public const string WallLayerName = "Wall";
        private const int PreferredWallLayer = 8;
        private const int PPU = 16;

        private static readonly string[] WallSets = { "lobby", "lab", "contain", "clean", "server", "reactor" };
        private static readonly string[] DoorKinds = { "slide_lobby", "door_lab", "door_contain", "door_reactor", "shutter_reactor", "shutter_contain" };

        private struct AnimSheet
        {
            public string path;
            public int frameW;
            public Vector2 pivot;

            public AnimSheet(string path, int frameW, Vector2 pivot)
            {
                this.path = path;
                this.frameW = frameW;
                this.pivot = pivot;
            }
        }

        private static readonly AnimSheet[] AnimSheets =
        {
            new AnimSheet("animations/objects/contain/warning_beacon.png", 16, new Vector2(0.5f, 0.5f)),
            new AnimSheet("animations/objects/contain/control_console.png", 32, new Vector2(0.5f, 0f)),
            new AnimSheet("animations/objects/contain/slime_tank.png", 32, new Vector2(0.5f, 0f)),
            new AnimSheet("animations/objects/fx/sparks.png", 16, new Vector2(0.5f, 0.5f)),
            new AnimSheet("animations/objects/fx/dust_puff.png", 16, new Vector2(0.5f, 0.5f)),
            new AnimSheet("animations/objects/reactor/generator.png", 32, new Vector2(0.5f, 0f)),
            new AnimSheet("animations/objects/reactor/tesla_coil.png", 32, new Vector2(0.5f, 0f)),
            new AnimSheet("animations/objects/reactor/capacitor_bank.png", 32, new Vector2(0.5f, 0f)),
            new AnimSheet("animations/objects/server/server_rack_a.png", 16, new Vector2(0.5f, 0f)),
            new AnimSheet("animations/objects/server/server_rack_b.png", 16, new Vector2(0.5f, 0f)),
        };

        private static readonly string[] PropFiles =
        {
            "lobby/reception_desk", "lobby/plant_tall", "lobby/waiting_chairs", "lobby/kiosk", "lobby/logo_wall",
            "reactor/geiger_counter", "server/cable_coil", "contain/containment_crate", "reactor/toxic_barrel_leak",
            "chem/gas_cylinders", "contain/hazard_barrier", "chem/lab_bench", "decon/uv_lamp", "office/desk_computer",
            "office/filing_cabinet", "break/vending_drinks", "break/water_cooler", "server/fire_suppression",
            "bio/specimen_tank", "contain/cryo_pod_open", "contain/quarantine_bed", "server/server_rack_wide",
            "server/supercomputer", "reactor/coolant_tank", "reactor/rad_barrel", "chem/periodic_table",
            "office/whiteboard", "office/corkboard", "contain/observation_window",
        };

        // ───────────────────────── 상태 ─────────────────────────
        private static Dictionary<string, Sprite> sprites;
        private static Dictionary<string, Tile> tileCache;
        private static Material litMaterial;
        private static TMP_FontAsset font;
        private static int wallLayer;

        [MenuItem("UNLEASH/Laboratory/Build Map + Scene", priority = 0)]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[LaboratoryMapBuilder] 플레이 모드에서는 실행할 수 없습니다.");
                return;
            }

            Scene current = SceneManager.GetActiveScene();
            if (current.isDirty && current.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }
            }

            AssetDatabase.DeleteAsset(TileDir);
            EnsureFolders();
            wallLayer = EnsureWallLayer();
            ImportArt();
            LoadSprites();
            BuildTileAtlas();
            litMaterial = AssetDatabase.LoadAssetAtPath<Material>(LitMaterialPath);
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            tileCache = new Dictionary<string, Tile>();

            Dictionary<string, RuntimeAnimatorController> doorControllers = BuildDoorAnimations();
            GameObject labMob = BuildLabMobVariant();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            LabLayout layout = new LabLayout();
            GameObject map = BuildMap(layout, doorControllers, labMob);

            GameObject mapPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(map, MapPrefabPath, InteractionMode.AutomatedAction);
            SetupScene(layout);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[LaboratoryMapBuilder] 완료: " + ScenePath + " / " + AssetDatabase.GetAssetPath(mapPrefab));
        }

        // ═════════════════════════ 1. 준비 ═════════════════════════

        private static void EnsureFolders()
        {
            string[] dirs = { Root, GenRoot, TileDir, AnimDir, AnimDir + "/Doors", PrefabDir, PrefabDir + "/Enemies", SceneDir };
            foreach (string d in dirs)
            {
                if (!AssetDatabase.IsValidFolder(d))
                {
                    string parent = Path.GetDirectoryName(d).Replace("\\", "/");
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(d));
                }
            }
        }

        /// <summary>"Wall" 레이어가 없으면 비어 있는 8번(또는 그 이후 빈 칸)에 추가한다.</summary>
        public static int EnsureWallLayer()
        {
            int existing = LayerMask.NameToLayer(WallLayerName);
            if (existing >= 0)
            {
                return existing;
            }

            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = PreferredWallLayer; i < 32; i++)
            {
                SerializedProperty p = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(p.stringValue))
                {
                    p.stringValue = WallLayerName;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[LaboratoryMapBuilder] Layer " + i + " = Wall 추가");
                    return i;
                }
            }

            throw new Exception("빈 레이어 슬롯이 없습니다.");
        }

        // ═════════════════════════ 2. 아트 임포트 ═════════════════════════

        private static void ImportArt()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                ImportGrid(Art + "/tiles/tiles_floors.png", 16, 16, new Vector2(0.5f, 0.5f), (r, c) => "floor_r" + r + "_c" + c);
                ImportGrid(Art + "/tiles/tiles_wall_caps.png", 16, 16, new Vector2(0.5f, 0.5f), (r, c) => "cap_" + c);
                foreach (string set in WallSets)
                {
                    string s = set;
                    // 벽면 16x32: 피벗을 아래 칸 중앙(0.5, 0.25)에 둬서 타일 한 칸 위로 솟게 한다.
                    ImportGrid(Art + "/tiles/tiles_walls_" + set + ".png", 16, 32, new Vector2(0.5f, 0.25f), (r, c) => "wall_" + s + "_" + c);
                }

                foreach (string kind in DoorKinds)
                {
                    string k = kind;
                    ImportGrid(Art + "/animations/objects/doors/" + kind + "_open.png", 32, 32, new Vector2(0.5f, 0.5f), (r, c) => k + "_open_" + c);
                    ImportGrid(Art + "/animations/objects/doors/" + kind + "_close.png", 32, 32, new Vector2(0.5f, 0.5f), (r, c) => k + "_close_" + c);
                }

                foreach (AnimSheet a in AnimSheets)
                {
                    string baseName = "anim_" + Path.GetFileNameWithoutExtension(a.path);
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/" + a.path);
                    ImportGrid(Art + "/" + a.path, a.frameW, tex.height, a.pivot, (r, c) => baseName + "_" + c);
                }

                foreach (string prop in PropFiles)
                {
                    ImportSingle(Art + "/props/" + prop + ".png");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh();
        }

        private static void ApplyPixelSettings(TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spritePixelsPerUnit = PPU;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
        }

        private static void ImportGrid(string path, int cellW, int cellH, Vector2 pivot, Func<int, int, string> namer)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogError("[LaboratoryMapBuilder] 텍스처 없음: " + path);
                return;
            }

            int w, h;
            ti.GetSourceTextureWidthAndHeight(out w, out h);
            int cols = w / cellW;
            int rows = h / cellH;
            List<SpriteMetaData> metas = new List<SpriteMetaData>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    SpriteMetaData m = new SpriteMetaData();
                    m.name = namer(r, c);
                    m.rect = new Rect(c * cellW, h - (r + 1) * cellH, cellW, cellH);
                    m.alignment = (int)SpriteAlignment.Custom;
                    m.pivot = pivot;
                    metas.Add(m);
                }
            }

#pragma warning disable 0618
            bool same = ti.spriteImportMode == SpriteImportMode.Multiple
                        && Mathf.Approximately(ti.spritePixelsPerUnit, PPU)
                        && ti.filterMode == FilterMode.Point
                        && ti.spritesheet != null
                        && ti.spritesheet.Length == metas.Count
                        && ti.spritesheet.Length > 0
                        && ti.spritesheet[0].name == metas[0].name
                        && ti.spritesheet[0].pivot == metas[0].pivot;
            if (same)
            {
                return;
            }

            ApplyPixelSettings(ti);
            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.spritesheet = metas.ToArray();
#pragma warning restore 0618
            ti.SaveAndReimport();
        }

        private static void ImportSingle(string path)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogError("[LaboratoryMapBuilder] 텍스처 없음: " + path);
                return;
            }

            TextureImporterSettings settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            if (ti.spriteImportMode == SpriteImportMode.Single && Mathf.Approximately(ti.spritePixelsPerUnit, PPU)
                && ti.filterMode == FilterMode.Point && settings.spriteAlignment == (int)SpriteAlignment.BottomCenter)
            {
                return;
            }

            ApplyPixelSettings(ti);
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(settings);
            ti.SaveAndReimport();
        }

        private static void LoadSprites()
        {
            sprites = new Dictionary<string, Sprite>();
            List<string> paths = new List<string>();
            paths.Add(Art + "/tiles/tiles_floors.png");
            paths.Add(Art + "/tiles/tiles_wall_caps.png");
            foreach (string set in WallSets)
            {
                paths.Add(Art + "/tiles/tiles_walls_" + set + ".png");
            }

            foreach (string kind in DoorKinds)
            {
                paths.Add(Art + "/animations/objects/doors/" + kind + "_open.png");
                paths.Add(Art + "/animations/objects/doors/" + kind + "_close.png");
            }

            foreach (AnimSheet a in AnimSheets)
            {
                paths.Add(Art + "/" + a.path);
            }

            foreach (string prop in PropFiles)
            {
                paths.Add(Art + "/props/" + prop + ".png");
            }

            foreach (string p in paths)
            {
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(p))
                {
                    Sprite s = o as Sprite;
                    if (s != null)
                    {
                        sprites[s.name] = s;
                    }
                }
            }
        }

        private static Sprite S(string name)
        {
            Sprite s;
            if (!sprites.TryGetValue(name, out s))
            {
                throw new Exception("[LaboratoryMapBuilder] 스프라이트를 찾을 수 없음: " + name);
            }

            return s;
        }

        /// <summary>패딩 아틀라스(LabTileAtlas)의 타일 스프라이트.</summary>
        private static Sprite T(string name)
        {
            return S("t_" + name);
        }

        private static Sprite[] Frames(string prefix)
        {
            List<Sprite> list = new List<Sprite>();
            for (int i = 0; ; i++)
            {
                Sprite s;
                if (!sprites.TryGetValue(prefix + "_" + i, out s))
                {
                    break;
                }

                list.Add(s);
            }

            if (list.Count == 0)
            {
                throw new Exception("[LaboratoryMapBuilder] 프레임 없음: " + prefix);
            }

            return list.ToArray();
        }

        private static Tile GetTile(Sprite sprite, Tile.ColliderType collider = Tile.ColliderType.None, string assetName = null)
        {
            string key = assetName ?? sprite.name;
            Tile tile;
            if (tileCache.TryGetValue(key, out tile))
            {
                return tile;
            }

            string path = TileDir + "/" + key + ".asset";
            tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            bool isNew = tile == null;
            if (isNew)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
            }

            // 값을 먼저 채운 뒤 에셋으로 만든다 (생성 후 수정하면 저장 전에 리로드될 때 값이 사라질 수 있음)
            tile.sprite = sprite;
            tile.colliderType = collider;
            tile.color = Color.white;
            if (isNew)
            {
                AssetDatabase.CreateAsset(tile, path);
            }
            else
            {
                EditorUtility.SetDirty(tile);
            }
            tileCache[key] = tile;
            return tile;
        }

        // ═════════════════════════ 2-b. 패딩 타일 아틀라스 ═════════════════════════

        public const string AtlasPath = GenRoot + "/LabTileAtlas.png";

        private class AtlasEntry
        {
            public string name;
            public Texture2D src;
            public int sx, sy, w, h;      // 원본 텍스처 좌표 (좌하단 기준)
            public Vector2 pivot;
            public Texture2D maskSrc;     // 데칼: 이 바닥 타일과 같은 픽셀은 투명 처리
            public int mx, my;
            public int ax, ay;            // 아틀라스 내 위치 (패딩 제외)
        }

        /// <summary>
        /// 타일맵용 스프라이트를 1px 가장자리 확장(extrude)한 별도 아틀라스로 복사한다.
        /// → 카메라 이동 시 타일 경계에 옆 칸 색이 번지는 줄(심) 현상 방지.
        /// 데칼(피/점액/균열/화살표)은 원본 바닥 타일과 비교해 배경을 투명하게 만든다.
        /// 원본 에셋 PNG 는 수정하지 않고 Generated 폴더에 새 PNG 를 만든다.
        /// </summary>
        private static void BuildTileAtlas()
        {
            Func<string, Texture2D> load = rel =>
            {
                Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(Art + "/" + rel));
                return t;
            };

            List<AtlasEntry> entries = new List<AtlasEntry>();
            Texture2D floors = load("tiles/tiles_floors.png");
            Func<int, int> fy = r => floors.height - (r + 1) * 16;
            for (int r = 0; r < 6; r++)
            {
                for (int c = 0; c < 16; c++)
                {
                    entries.Add(new AtlasEntry { name = "t_floor_r" + r + "_c" + c, src = floors, sx = c * 16, sy = fy(r), w = 16, h = 16, pivot = new Vector2(0.5f, 0.5f) });
                }
            }

            Texture2D caps = load("tiles/tiles_wall_caps.png");
            for (int c = 0; c < caps.width / 16; c++)
            {
                entries.Add(new AtlasEntry { name = "t_cap_" + c, src = caps, sx = c * 16, sy = 0, w = 16, h = 16, pivot = new Vector2(0.5f, 0.5f) });
            }

            // 데칼: 이름 / (데칼 행, 열, 배경 바닥 행, 열)
            string[] decalNames = { "decal_blood", "decal_splat", "decal_slime", "decal_crack", "decal_arrow" };
            int[,] decalCells = { { 5, 7, 0, 4 }, { 5, 3, 1, 12 }, { 5, 2, 3, 12 }, { 5, 9, 3, 12 }, { 5, 1, 0, 4 } };
            for (int i = 0; i < decalNames.Length; i++)
            {
                AtlasEntry e = new AtlasEntry();
                e.name = "t_" + decalNames[i];
                e.src = floors;
                e.sx = decalCells[i, 1] * 16;
                e.sy = fy(decalCells[i, 0]);
                e.w = 16;
                e.h = 16;
                e.pivot = new Vector2(0.5f, 0.5f);
                e.maskSrc = floors;
                e.mx = decalCells[i, 3] * 16;
                e.my = fy(decalCells[i, 2]);
                entries.Add(e);
            }

            foreach (string set in WallSets)
            {
                Texture2D wt = load("tiles/tiles_walls_" + set + ".png");
                for (int c = 0; c < wt.width / 16; c++)
                {
                    entries.Add(new AtlasEntry { name = "t_wall_" + set + "_" + c, src = wt, sx = c * 16, sy = 0, w = 16, h = 32, pivot = new Vector2(0.5f, 0.25f) });
                }
            }

            // 단순 선반(shelf) 패킹, 각 칸 사방 1px 여백
            const int atlasW = 1024;
            int cx = 0, cy = 0, rowH = 0;
            foreach (AtlasEntry e in entries)
            {
                int cw = e.w + 2, ch = e.h + 2;
                if (cx + cw > atlasW)
                {
                    cx = 0;
                    cy += rowH;
                    rowH = 0;
                }

                e.ax = cx + 1;
                e.ay = cy + 1;
                cx += cw;
                rowH = Mathf.Max(rowH, ch);
            }

            int atlasH = cy + rowH;
            Texture2D atlas = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);
            atlas.SetPixels32(new Color32[atlasW * atlasH]);
            foreach (AtlasEntry e in entries)
            {
                for (int y = -1; y <= e.h; y++)
                {
                    for (int x = -1; x <= e.w; x++)
                    {
                        int px = Mathf.Clamp(x, 0, e.w - 1);
                        int py = Mathf.Clamp(y, 0, e.h - 1);
                        Color c = e.src.GetPixel(e.sx + px, e.sy + py);
                        if (e.maskSrc != null && c == e.maskSrc.GetPixel(e.mx + px, e.my + py))
                        {
                            c = Color.clear;
                        }

                        atlas.SetPixel(e.ax + x, e.ay + y, c);
                    }
                }
            }

            atlas.Apply();
            File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);

            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            ApplyPixelSettings(ti);
            ti.spriteImportMode = SpriteImportMode.Multiple;
            TextureImporterSettings settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            ti.SetTextureSettings(settings);
            List<SpriteMetaData> metas = new List<SpriteMetaData>();
            foreach (AtlasEntry e in entries)
            {
                SpriteMetaData m = new SpriteMetaData();
                m.name = e.name;
                m.rect = new Rect(e.ax, e.ay, e.w, e.h);
                m.alignment = (int)SpriteAlignment.Custom;
                m.pivot = e.pivot;
                metas.Add(m);
            }

#pragma warning disable 0618
            ti.spritesheet = metas.ToArray();
#pragma warning restore 0618
            ti.SaveAndReimport();

            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(AtlasPath))
            {
                Sprite sp = o as Sprite;
                if (sp != null)
                {
                    sprites[sp.name] = sp;
                }
            }
        }

        // ═════════════════════════ 3. 문 애니메이션 ═════════════════════════

        private static AnimationClip MakeSpriteClip(string path, Sprite[] frames, float fps, bool loop)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }

            clip.ClearCurves();
            clip.frameRate = fps;
            EditorCurveBinding binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[frames.Length + 1];
            for (int i = 0; i < frames.Length; i++)
            {
                keys[i].time = i / fps;
                keys[i].value = frames[i];
            }

            // 마지막 프레임을 한 프레임 더 유지해서 클립 길이 = frames / fps
            keys[frames.Length].time = frames.Length / fps;
            keys[frames.Length].value = frames[frames.Length - 1];
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private class DoorClips
        {
            public AnimationClip closed;
            public AnimationClip opening;
            public AnimationClip open;
            public AnimationClip closing;
        }

        private static DoorClips MakeDoorClips(string kind)
        {
            string dir = AnimDir + "/Doors";
            Sprite[] openFrames = Frames(kind + "_open");
            Sprite[] closeFrames = Frames(kind + "_close");
            DoorClips c = new DoorClips();
            c.closed = MakeSpriteClip(dir + "/" + kind + "_Closed.anim", new[] { openFrames[0] }, 12f, true);
            c.opening = MakeSpriteClip(dir + "/" + kind + "_Opening.anim", openFrames, 12f, false);
            c.open = MakeSpriteClip(dir + "/" + kind + "_Open.anim", new[] { openFrames[openFrames.Length - 1] }, 12f, true);
            c.closing = MakeSpriteClip(dir + "/" + kind + "_Closing.anim", closeFrames, 12f, false);
            return c;
        }

        /// <summary>
        /// 공통 문 AnimatorController(Closed/Opening/Open/Closing) 1개 + 문 종류별 AnimatorOverrideController.
        /// 새로운 문 종류를 추가하려면 DoorKinds 에 이름만 추가하면 된다.
        /// </summary>
        private static Dictionary<string, RuntimeAnimatorController> BuildDoorAnimations()
        {
            Dictionary<string, DoorClips> clips = new Dictionary<string, DoorClips>();
            foreach (string kind in DoorKinds)
            {
                clips[kind] = MakeDoorClips(kind);
            }

            string basePath = AnimDir + "/Doors/LabDoor_Base.controller";
            AssetDatabase.DeleteAsset(basePath);
            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(basePath);
            ctrl.AddParameter("IsOpen", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("AnimSpeed", AnimatorControllerParameterType.Float);
            AnimatorControllerParameter[] ps = ctrl.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].name == "AnimSpeed")
                {
                    ps[i].defaultFloat = 1f;
                }
            }

            ctrl.parameters = ps;

            DoorClips baseClips = clips["door_lab"];
            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
            AnimatorState closed = sm.AddState("Closed", new Vector3(250, 0, 0));
            AnimatorState opening = sm.AddState("Opening", new Vector3(500, -80, 0));
            AnimatorState open = sm.AddState("Open", new Vector3(750, 0, 0));
            AnimatorState closing = sm.AddState("Closing", new Vector3(500, 80, 0));
            closed.motion = baseClips.closed;
            opening.motion = baseClips.opening;
            open.motion = baseClips.open;
            closing.motion = baseClips.closing;
            opening.speedParameterActive = true;
            opening.speedParameter = "AnimSpeed";
            closing.speedParameterActive = true;
            closing.speedParameter = "AnimSpeed";
            sm.defaultState = closed;

            AddTransition(closed, opening, AnimatorConditionMode.If, false);
            AddTransition(opening, open, AnimatorConditionMode.If, true);
            AddTransition(opening, closing, AnimatorConditionMode.IfNot, false);
            AddTransition(open, closing, AnimatorConditionMode.IfNot, false);
            AddTransition(closing, closed, AnimatorConditionMode.IfNot, true);
            AddTransition(closing, opening, AnimatorConditionMode.If, false);
            EditorUtility.SetDirty(ctrl);

            Dictionary<string, RuntimeAnimatorController> result = new Dictionary<string, RuntimeAnimatorController>();
            foreach (string kind in DoorKinds)
            {
                string path = AnimDir + "/Doors/LabDoor_" + kind + ".overrideController";
                AssetDatabase.DeleteAsset(path);
                AnimatorOverrideController aoc = new AnimatorOverrideController(ctrl);
                aoc.name = "LabDoor_" + kind;
                DoorClips k = clips[kind];
                aoc[baseClips.closed] = k.closed;
                aoc[baseClips.opening] = k.opening;
                aoc[baseClips.open] = k.open;
                aoc[baseClips.closing] = k.closing;
                AssetDatabase.CreateAsset(aoc, path);
                result[kind] = aoc;
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        private static void AddTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, bool exitTime)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = exitTime;
            t.exitTime = 1f;
            t.duration = 0f;
            t.hasFixedDuration = true;
            t.AddCondition(mode, 0f, "IsOpen");
        }

        // ═════════════════════════ 4. 적 프리팹 Variant ═════════════════════════

        /// <summary>
        /// TestMob 원본은 그대로 두고, Variant 에 "WallBlocker"(트리거 아님) 콜라이더만 추가.
        /// → 적이 벽/닫힌 문을 통과하지 않고, 서로 겹치지도 않는다. 피격/감지 로직은 원본 그대로.
        /// </summary>
        private static GameObject BuildLabMobVariant()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(LabMobPath);
            if (existing != null && existing.transform.Find("WallBlocker") != null)
            {
                return existing;
            }

            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestMobPrefabPath);
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            inst.name = "LabMob";
            GameObject blocker = new GameObject("WallBlocker");
            blocker.layer = basePrefab.layer;
            blocker.transform.SetParent(inst.transform, false);
            CircleCollider2D c = blocker.AddComponent<CircleCollider2D>();
            c.radius = 0.32f;
            c.isTrigger = false;
            GameObject variant = PrefabUtility.SaveAsPrefabAsset(inst, LabMobPath);
            Object.DestroyImmediate(inst);
            return variant;
        }

        // ═════════════════════════ 5. 레이아웃 데이터 ═════════════════════════

        private enum Cell : byte
        {
            Wall = 0,
            Floor = 1,
            Door = 2,
        }

        private class Zone
        {
            public string name;
            public int x0, y0, x1, y1;
            public string floor;
            public string wallSet;

            public Zone(string name, int x0, int y0, int x1, int y1, string floor, string wallSet)
            {
                this.name = name;
                this.x0 = x0;
                this.y0 = y0;
                this.x1 = x1;
                this.y1 = y1;
                this.floor = floor;
                this.wallSet = wallSet;
            }

            public Vector2 Center { get { return new Vector2((x0 + x1 + 1) * 0.5f, (y0 + y1 + 1) * 0.5f); } }
            public Vector2 Size { get { return new Vector2(x1 - x0 + 1, y1 - y0 + 1); } }
        }

        private class DoorDef
        {
            public string name;
            public string kind;
            public int x;
            public int faceY;
            public bool connector;
            public int connectorZone;
            public bool auto;
            public bool locked;
            public float openDuration = 0.6f;
            public LabDoor door;

            public Vector3 Center { get { return new Vector3(x + 1f, faceY + 1f, 0f); } }
        }

        /// <summary>
        /// 맵 구역 정의 (1 칸 = 1 유닛 = 16px).
        /// 진행: Z0 로비 → Z1 복도 → Z2 첫 습격 → Z3 도주 복도 → Z4 조명실 → Z5 손전등 구간
        ///      → Z6 연구동 복도(방 매복 R1~R3) → Z7 대형 홀(조명 ON) → Z8 좁은 골목(양방향 협공)
        ///      → Z9 디펜스(보스 셔터) → Z10 보스방 전실 (종료)
        /// 남북 벽 두께 3칸(벽면 2 + 윗면 1), 문은 북쪽 벽면에만 배치(에셋이 정면 문).
        /// </summary>
        private class LabLayout
        {
            public const int OX = -8, OY = -8, W = 124, H = 164;
            public readonly Cell[,] cells = new Cell[W, H];
            public readonly int[,] zoneOf = new int[W, H];
            public readonly List<Zone> zones = new List<Zone>();
            public readonly List<DoorDef> doors = new List<DoorDef>();
            public readonly Dictionary<string, Zone> zoneByName = new Dictionary<string, Zone>();

            public LabLayout()
            {
                for (int x = 0; x < W; x++)
                {
                    for (int y = 0; y < H; y++)
                    {
                        zoneOf[x, y] = -1;
                    }
                }

                // 이름, x0, y0, x1, y1(포함), 바닥 스타일, 벽 세트
                AddZone(new Zone("Z0_Lobby", 0, 0, 17, 11, "lobby", "lobby"));
                AddZone(new Zone("Z1_Corridor", 7, 15, 11, 34, "dark", "lab"));
                AddZone(new Zone("Z2_Assault", 1, 35, 22, 48, "metal", "contain"));
                AddZone(new Zone("Z3_Retreat", 23, 38, 40, 41, "dark", "contain"));
                AddZone(new Zone("Z4_LightRoom", 41, 33, 54, 48, "concrete", "lab"));
                AddZone(new Zone("Z5_FlashRoom", 41, 52, 54, 61, "whitegrid", "lab"));
                AddZone(new Zone("Z6_LabCorridor", 42, 65, 82, 69, "plates", "clean"));
                AddZone(new Zone("R1_Lab", 48, 73, 55, 80, "teal", "clean"));
                AddZone(new Zone("R2_Quarantine", 57, 55, 64, 61, "teal", "clean"));
                AddZone(new Zone("R3_Containment", 62, 73, 67, 80, "darkplate", "contain"));
                AddZone(new Zone("Z7_Hall", 70, 73, 99, 94, "speckle", "server"));
                AddZone(new Zone("Z8_AlleyLink", 83, 98, 85, 104, "darkplate", "reactor"));
                AddZone(new Zone("Z8_Alley", 56, 105, 92, 107, "darkplate", "reactor"));
                AddZone(new Zone("Z9_Defense", 62, 111, 85, 130, "bluedot", "reactor"));
                AddZone(new Zone("Z10_BossAnte", 68, 134, 79, 141, "checker", "reactor"));

                // 문: 종류, 왼쪽 x, 벽면 아래 y, 위쪽 연결칸 여부, 자동 열림, 잠김
                AddDoor("Door_LobbyExit", "slide_lobby", 8, 12, true, "Z1_Corridor", true, false);
                AddDoor("Door_LightToFlash", "door_lab", 47, 49, true, "Z5_FlashRoom", true, false);
                AddDoor("Door_FlashToLabs", "door_lab", 47, 62, true, "Z6_LabCorridor", true, false);
                AddDoor("Door_R1", "door_lab", 51, 70, true, "R1_Lab", false, true);
                AddDoor("Door_R2", "door_lab", 60, 62, true, "Z6_LabCorridor", false, true);
                AddDoor("Door_R3", "door_contain", 65, 70, true, "R3_Containment", false, true);
                AddDoor("Door_Hall", "door_contain", 79, 70, true, "Z7_Hall", true, false);
                AddDoor("Door_HallToAlley", "door_reactor", 84, 95, true, "Z8_AlleyLink", true, false);
                DoorDef def = AddDoor("Door_AlleyToDefense", "door_reactor", 73, 108, true, "Z9_Defense", true, true);
                def.openDuration = 0.8f;
                DoorDef gl = AddDoor("Gate_Boss_L", "shutter_reactor", 72, 131, true, "Z10_BossAnte", false, true);
                DoorDef gr = AddDoor("Gate_Boss_R", "shutter_reactor", 74, 131, true, "Z10_BossAnte", false, true);
                gl.openDuration = 45f;
                gr.openDuration = 45f;
                AddDoor("Door_BossRoom", "shutter_contain", 73, 142, false, null, false, true);
            }

            public Zone Z(string name)
            {
                return zoneByName[name];
            }

            private void AddZone(Zone z)
            {
                zones.Add(z);
                zoneByName[z.name] = z;
                int idx = zones.Count - 1;
                for (int x = z.x0; x <= z.x1; x++)
                {
                    for (int y = z.y0; y <= z.y1; y++)
                    {
                        Set(x, y, Cell.Floor);
                        zoneOf[x - OX, y - OY] = idx;
                    }
                }
            }

            private DoorDef AddDoor(string name, string kind, int x, int faceY, bool connector, string connectorZone, bool auto, bool locked)
            {
                DoorDef d = new DoorDef();
                d.name = name;
                d.kind = kind;
                d.x = x;
                d.faceY = faceY;
                d.connector = connector;
                d.auto = auto;
                d.locked = locked;
                d.connectorZone = connectorZone != null ? zones.IndexOf(zoneByName[connectorZone]) : -1;
                doors.Add(d);

                for (int dx = 0; dx < 2; dx++)
                {
                    for (int dy = 0; dy < 2; dy++)
                    {
                        Set(x + dx, faceY + dy, Cell.Door);
                    }

                    if (connector && Get(x + dx, faceY + 2) == Cell.Wall)
                    {
                        Set(x + dx, faceY + 2, Cell.Floor);
                        zoneOf[x + dx - OX, faceY + 2 - OY] = d.connectorZone;
                    }
                }

                return d;
            }

            public Cell Get(int x, int y)
            {
                int ix = x - OX, iy = y - OY;
                if (ix < 0 || iy < 0 || ix >= W || iy >= H)
                {
                    return Cell.Wall;
                }

                return cells[ix, iy];
            }

            public void Set(int x, int y, Cell c)
            {
                cells[x - OX, y - OY] = c;
            }

            public Zone ZoneAt(int x, int y)
            {
                int ix = x - OX, iy = y - OY;
                if (ix < 0 || iy < 0 || ix >= W || iy >= H)
                {
                    return null;
                }

                int z = zoneOf[ix, iy];
                return z >= 0 ? zones[z] : null;
            }

            public bool IsFloor(int x, int y) { return Get(x, y) == Cell.Floor; }
            public bool IsDoor(int x, int y) { return Get(x, y) == Cell.Door; }
            public bool IsWall(int x, int y) { return Get(x, y) == Cell.Wall; }
            public bool FaceBottom(int x, int y) { return IsWall(x, y) && IsFloor(x, y - 1); }
            public bool FaceTop(int x, int y) { return IsWall(x, y) && FaceBottom(x, y - 1); }
            public bool IsFace(int x, int y) { return FaceBottom(x, y) || FaceTop(x, y); }
            public bool IsInterior(int x, int y) { return IsFloor(x, y) || IsDoor(x, y) || IsFace(x, y); }
            public bool IsTopOfFace(int x, int y) { return FaceTop(x, y) || (IsDoor(x, y) && IsDoor(x, y - 1)); }

            public bool NearWalkable(int x, int y, int dist)
            {
                for (int dx = -dist; dx <= dist; dx++)
                {
                    for (int dy = -dist; dy <= dist; dy++)
                    {
                        Cell c = Get(x + dx, y + dy);
                        if (c == Cell.Floor || c == Cell.Door)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        private static int Hash(int x, int y, int salt)
        {
            unchecked
            {
                int h = x * 73856093 ^ y * 19349663 ^ salt * 83492791;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return h & 0x7fffffff;
            }
        }

        private static string FloorSprite(string style, int x, int y)
        {
            int h = Hash(x, y, 7);
            switch (style)
            {
                case "lobby": return "floor_r0_c" + (4 + h % 4);
                case "dark": return h % 6 == 0 ? "floor_r2_c" + (4 + h % 4) : "floor_r2_c" + (h % 4);
                case "metal": return "floor_r3_c" + (h % 4);
                case "concrete": return "floor_r0_c" + (h % 4);
                case "whitegrid": return h % 9 == 0 ? "floor_r3_c5" : "floor_r3_c" + (4 + (h % 2) * 2);
                case "plates": return "floor_r1_c" + (h % 4);
                case "teal": return "floor_r0_c" + (8 + h % 4);
                case "speckle": return "floor_r1_c" + (12 + h % 4);
                case "darkplate": return "floor_r4_c" + (8 + h % 4);
                case "bluedot": return "floor_r2_c" + (8 + h % 4);
                case "grate": return "floor_r1_c" + (8 + h % 4);
                case "checker": return "floor_r2_c" + (12 + h % 4);
                default: return "floor_r0_c0";
            }
        }

        private class WallSetInfo
        {
            public int[] plain;
            public int vent;
            public int lamp;

            public WallSetInfo(int[] plain, int vent, int lamp)
            {
                this.plain = plain;
                this.vent = vent;
                this.lamp = lamp;
            }
        }

        private static readonly Dictionary<string, WallSetInfo> WallInfo = new Dictionary<string, WallSetInfo>
        {
            { "lobby", new WallSetInfo(new[] { 0, 1, 2, 3, 4, 5 }, 6, 7) },
            { "lab", new WallSetInfo(new[] { 0, 1, 2 }, 3, 4) },
            { "contain", new WallSetInfo(new[] { 0, 1, 2, 3 }, 4, 5) },
            { "clean", new WallSetInfo(new[] { 0, 1, 2, 3 }, 4, 5) },
            { "server", new WallSetInfo(new[] { 0, 1, 2, 3, 4, 5 }, 6, 7) },
            { "reactor", new WallSetInfo(new[] { 0, 1, 2 }, 3, 4) },
        };

        // ═════════════════════════ 6. 맵 생성 ═════════════════════════

        private class MapCtx
        {
            public LabLayout L;
            public Transform root, doors, props, lights, zones, encounters, enemies, markers, hints, systems;
            public Tilemap floor, decals, faces, capsA, capsB, capsC, collision;
            public Dictionary<string, RuntimeAnimatorController> doorCtrls;
            public GameObject labMob;
            public GameObject mobControllerPrefab;
            public Dictionary<string, LabDoor> doorByName = new Dictionary<string, LabDoor>();
        }

        private static GameObject BuildMap(LabLayout L, Dictionary<string, RuntimeAnimatorController> doorCtrls, GameObject labMob)
        {
            MapCtx m = new MapCtx();
            m.L = L;
            m.doorCtrls = doorCtrls;
            m.labMob = labMob;
            m.mobControllerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MobControllerPrefabPath);

            GameObject root = new GameObject("LaboratoryMap");
            m.root = root.transform;

            GameObject gridGo = Child("Grid", m.root);
            Grid grid = gridGo.AddComponent<Grid>();
            grid.cellSize = Vector3.one;
            m.floor = MakeTilemap("Floor", gridGo.transform, -100);
            m.decals = MakeTilemap("FloorDecals", gridGo.transform, -96);
            m.faces = MakeTilemap("WallFaces", gridGo.transform, -92);
            m.capsA = MakeTilemap("WallCaps", gridGo.transform, -86);
            m.capsB = MakeTilemap("WallCaps_2", gridGo.transform, -85);
            m.capsC = MakeTilemap("WallCaps_3", gridGo.transform, -84);
            m.collision = MakeCollisionTilemap(gridGo.transform);

            m.doors = Child("Doors", m.root).transform;
            m.props = Child("Props", m.root).transform;
            m.lights = Child("Lights", m.root).transform;
            m.zones = Child("LightZones", m.root).transform;
            m.encounters = Child("Encounters", m.root).transform;
            m.enemies = Child("Enemies", m.root).transform;
            m.markers = Child("Markers", m.root).transform;
            m.hints = Child("Hints", m.root).transform;
            m.systems = Child("Systems", m.root).transform;

            PaintTiles(m);
            AssetDatabase.SaveAssets();
            BuildDoors(m);

            GameObject playerStart = Child("PlayerStart", m.markers);
            playerStart.transform.position = new Vector3(9f, 3.5f, 0f);

            BuildZ0Lobby(m);
            BuildZ1Corridor(m);
            BuildZ2Assault(m);
            BuildZ3Retreat(m);
            BuildZ4LightRoom(m);
            BuildZ5FlashRoom(m);
            BuildZ6LabCorridor(m);
            BuildZ7Hall(m);
            BuildZ8Alley(m);
            BuildZ9Defense(m);
            BuildZ10BossAnte(m);

            return root;
        }

        private static GameObject Child(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject Child(string name, Transform parent, Vector2 pos)
        {
            GameObject go = Child(name, parent);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            return go;
        }

        private static Tilemap MakeTilemap(string name, Transform parent, int order)
        {
            GameObject go = Child(name, parent);
            Tilemap tm = go.AddComponent<Tilemap>();
            TilemapRenderer r = go.AddComponent<TilemapRenderer>();
            r.sortingLayerName = "Default";
            r.sortingOrder = order;
            r.mode = TilemapRenderer.Mode.Chunk;
            if (litMaterial != null)
            {
                r.sharedMaterial = litMaterial;
            }

            return tm;
        }

        private static Tilemap MakeCollisionTilemap(Transform parent)
        {
            GameObject go = Child("WallCollision", parent);
            go.layer = wallLayer;
            Tilemap tm = go.AddComponent<Tilemap>();
            TilemapCollider2D tc = go.AddComponent<TilemapCollider2D>();
            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            CompositeCollider2D cc = go.AddComponent<CompositeCollider2D>();
            cc.geometryType = CompositeCollider2D.GeometryType.Polygons;
            cc.generationType = CompositeCollider2D.GenerationType.Synchronous;
            tc.usedByComposite = true;
            return tm;
        }

        private static void PaintTiles(MapCtx m)
        {
            LabLayout L = m.L;
            Tile collisionTile = GetTile(T("floor_r2_c0"), Tile.ColliderType.Grid, "LabWallCollision");

            for (int ix = 0; ix < LabLayout.W; ix++)
            {
                for (int iy = 0; iy < LabLayout.H; iy++)
                {
                    int x = ix + LabLayout.OX;
                    int y = iy + LabLayout.OY;
                    Vector3Int p = new Vector3Int(x, y, 0);
                    Cell c = L.Get(x, y);

                    if (c == Cell.Floor)
                    {
                        Zone z = L.ZoneAt(x, y);
                        m.floor.SetTile(p, GetTile(T(FloorSprite(z != null ? z.floor : "dark", x, y))));
                        continue;
                    }

                    if (c == Cell.Door)
                    {
                        continue;
                    }

                    // 벽 칸
                    if (L.NearWalkable(x, y, 2))
                    {
                        m.collision.SetTile(p, collisionTile);
                    }

                    if (L.FaceBottom(x, y))
                    {
                        Zone below = L.ZoneAt(x, y - 1);
                        string set = below != null ? below.wallSet : "lab";
                        m.faces.SetTile(p, GetTile(T(PickWallFace(L, set, x, y))));
                        continue;
                    }

                    if (L.FaceTop(x, y))
                    {
                        continue;
                    }

                    List<int> caps = new List<int>();
                    bool right = L.IsInterior(x + 1, y);
                    bool left = L.IsInterior(x - 1, y);
                    bool up = L.IsFloor(x, y + 1) || L.IsDoor(x, y + 1);
                    if (right)
                    {
                        caps.Add(L.IsTopOfFace(x + 1, y) ? 0 : 3);
                    }

                    if (left)
                    {
                        caps.Add(L.IsTopOfFace(x - 1, y) ? 2 : 4);
                    }

                    if (up)
                    {
                        caps.Add(1);
                    }

                    if (!right && !left && !up)
                    {
                        if (L.IsFloor(x + 1, y + 1) || L.IsDoor(x + 1, y + 1))
                        {
                            caps.Add(5);
                        }
                        else if (L.IsFloor(x - 1, y + 1) || L.IsDoor(x - 1, y + 1))
                        {
                            caps.Add(6);
                        }
                    }

                    Tilemap[] layers = { m.capsA, m.capsB, m.capsC };
                    for (int i = 0; i < caps.Count && i < layers.Length; i++)
                    {
                        layers[i].SetTile(p, GetTile(T("cap_" + caps[i])));
                    }
                }
            }
        }

        private static string PickWallFace(LabLayout L, string set, int x, int y)
        {
            WallSetInfo info = WallInfo[set];
            bool nearDoor = L.IsDoor(x - 1, y) || L.IsDoor(x + 1, y);
            bool edge = !L.FaceBottom(x - 1, y) || !L.FaceBottom(x + 1, y);
            int h = Hash(x, y, 31);
            int col;
            if (!nearDoor && !edge && x % 8 == 4)
            {
                col = info.lamp;
            }
            else if (!nearDoor && !edge && h % 13 == 0)
            {
                col = info.vent;
            }
            else
            {
                col = info.plain[((x % info.plain.Length) + info.plain.Length) % info.plain.Length];
            }

            return "wall_" + set + "_" + col;
        }

        /// <summary>바닥 위 투명 데칼 (피/점액/균열/화살표) 또는 바닥 교체 타일(경고선/배수구/표식).</summary>
        private static void Decal(MapCtx m, int x, int y, string sprite)
        {
            Vector3Int p = new Vector3Int(x, y, 0);
            if (sprite.StartsWith("decal_"))
            {
                m.decals.SetTile(p, GetTile(T(sprite)));
            }
            else
            {
                m.floor.SetTile(p, GetTile(T(sprite)));
            }
        }

        private const string Blood = "decal_blood";
        private const string Splat = "decal_splat";
        private const string Slime = "decal_slime";
        private const string Crack = "decal_crack";
        private const string Arrow = "decal_arrow";
        private const string Drain = "floor_r5_c10";
        private const string HazardH = "floor_r4_c12";
        private const string Biohazard = "floor_r4_c15";
        private const string Radiation = "floor_r5_c0";

        // ───────────── 문 ─────────────

        private static void BuildDoors(MapCtx m)
        {
            foreach (DoorDef d in m.L.doors)
            {
                GameObject go = Child(d.name, m.doors, d.Center);
                go.layer = wallLayer;
                SpriteRenderer sr = AddSprite(go, Frames(d.kind + "_open")[0], "Default", -88);
                Animator anim = go.AddComponent<Animator>();
                anim.runtimeAnimatorController = m.doorCtrls[d.kind];
                BoxCollider2D col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(2f, 2f);

                LabDoor door = go.AddComponent<LabDoor>();
                SetProp(door, "locked", d.locked);
                SetProp(door, "autoOpenOnPlayer", d.auto);
                SetProp(door, "openDuration", d.openDuration);
                SetProp(door, "closeDuration", 0.6f);
                SetProp(door, "clipLength", 1f);
                SetProp(door, "blockingCollider", col);

                GameObject sensor = Child("Sensor", go.transform);
                sensor.layer = 2; // Ignore Raycast
                BoxCollider2D sc = sensor.AddComponent<BoxCollider2D>();
                sc.isTrigger = true;
                sc.size = new Vector2(3.2f, 6f);
                sensor.AddComponent<LabDoorSensor>();

                d.door = door;
                m.doorByName[d.name] = door;
            }
        }

        // ───────────── 공통 생성 헬퍼 ─────────────

        private static SpriteRenderer AddSprite(GameObject go, Sprite s, string layer, int order)
        {
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            if (litMaterial != null)
            {
                sr.sharedMaterial = litMaterial;
            }

            return sr;
        }

        /// <summary>바닥 소품. (cx = 중심 x, by = 바닥 y). collide 면 Wall 레이어 발판 콜라이더 → 총알도 막힘.</summary>
        private static GameObject Prop(MapCtx m, string sprite, float cx, float by, bool collide, string name = null)
        {
            Sprite s = S(sprite);
            GameObject go = Child(name ?? sprite, m.props, new Vector2(cx, by));
            AddSprite(go, s, "Environment", -10);
            if (collide)
            {
                go.layer = wallLayer;
                BoxCollider2D c = go.AddComponent<BoxCollider2D>();
                float w = s.rect.width / PPU;
                c.size = new Vector2(w * 0.9f, 0.8f);
                c.offset = new Vector2(0f, 0.45f);
            }

            return go;
        }

        /// <summary>벽면에 붙는 장식 (콜라이더 없음, 벽면 위에 그림).</summary>
        private static GameObject WallDecor(MapCtx m, string sprite, float cx, float by)
        {
            GameObject go = Child(sprite, m.props, new Vector2(cx, by));
            AddSprite(go, S(sprite), "Default", -89);
            return go;
        }

        private static GameObject AnimProp(MapCtx m, Transform parent, string anim, Vector2 pos, float fps, bool collide, string layer = "Environment", int order = -10)
        {
            Sprite[] frames = Frames("anim_" + anim);
            GameObject go = Child(anim, parent, pos);
            AddSprite(go, frames[0], layer, order);
            LabSpriteLoop loop = go.AddComponent<LabSpriteLoop>();
            loop.Setup(frames, fps, false);
            if (collide)
            {
                go.layer = wallLayer;
                BoxCollider2D c = go.AddComponent<BoxCollider2D>();
                float w = frames[0].rect.width / PPU;
                c.size = new Vector2(w * 0.9f, 0.8f);
                c.offset = new Vector2(0f, 0.45f);
            }

            return go;
        }

        private static Light2D AddLight(MapCtx m, Transform parent, string name, Vector2 pos, Color color, float intensity, float outer, float inner = 0.4f, LabFlickerLight.Pattern? flicker = null, float flickerSpeed = 1f, float minFactor = 0.15f)
        {
            GameObject go = Child(name, parent, pos);
            Light2D l = go.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.pointLightOuterRadius = outer;
            l.pointLightInnerRadius = inner;
            l.falloffIntensity = 0.55f;
            l.shadowsEnabled = false;
            ApplyAllSortingLayers(l);
            if (flicker.HasValue)
            {
                LabFlickerLight f = go.AddComponent<LabFlickerLight>();
                SetProp(f, "pattern", (int)flicker.Value);
                SetProp(f, "speed", flickerSpeed);
                SetProp(f, "minFactor", minFactor);
            }

            return l;
        }

        private static void ApplyAllSortingLayers(Light2D l)
        {
            SerializedObject so = new SerializedObject(l);
            SerializedProperty p = so.FindProperty("m_ApplyToSortingLayers");
            SortingLayer[] layers = SortingLayer.layers;
            p.arraySize = layers.Length;
            for (int i = 0; i < layers.Length; i++)
            {
                p.GetArrayElementAtIndex(i).intValue = layers[i].id;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static readonly Color HintWhite = new Color(0.92f, 0.95f, 1f, 0.95f);
        private static readonly Color HintRed = new Color(1f, 0.35f, 0.3f, 1f);
        private static readonly Color HintYellow = new Color(1f, 0.9f, 0.45f, 1f);

        private static LabHint Hint(MapCtx m, string name, Vector2 pos, string text, LabHint.Mode mode, float radius, Color color, bool pulse = false, float manualDuration = 0f, float size = 3.2f)
        {
            GameObject go = Child("Hint_" + name, m.hints, pos);
            TextMeshPro tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            if (font != null)
            {
                tmp.font = font;
            }

            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.color = color;
            tmp.rectTransform.sizeDelta = new Vector2(16f, 3f);
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sortingLayerName = "Player";
            mr.sortingOrder = 100;

            LabHint hint = go.AddComponent<LabHint>();
            SetProp(hint, "label", tmp);
            SetProp(hint, "mode", (int)mode);
            SetProp(hint, "showRadius", radius);
            SetProp(hint, "pulse", pulse);
            SetProp(hint, "manualDuration", manualDuration);
            return hint;
        }

        private static GameObject MobGroup(MapCtx m, string name)
        {
            GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(m.mobControllerPrefab, m.enemies);
            g.name = name;
            g.transform.position = Vector3.zero;
            return g;
        }

        private static GameObject Mob(MapCtx m, GameObject group, float x, float y, bool active = true)
        {
            GameObject mob = (GameObject)PrefabUtility.InstantiatePrefab(m.labMob, group.transform);
            mob.transform.position = new Vector3(x + 0.5f, y + 0.5f, 0f);
            mob.name = "LabMob_" + group.transform.childCount;
            mob.SetActive(active);
            return mob;
        }

        private static Transform Marker(MapCtx m, Transform parent, string name, float x, float y)
        {
            GameObject go = Child(name, parent, new Vector2(x + 0.5f, y + 0.5f));
            return go.transform;
        }

        private static BoxCollider2D TriggerBox(GameObject go, Vector2 size)
        {
            go.layer = 2;
            BoxCollider2D c = go.AddComponent<BoxCollider2D>();
            c.isTrigger = true;
            c.size = size;
            return c;
        }

        private static LabEncounter Encounter(MapCtx m, string name, Vector2 center, Vector2 size)
        {
            GameObject go = Child("Encounter_" + name, m.encounters, center);
            TriggerBox(go, size);
            return go.AddComponent<LabEncounter>();
        }

        /// <summary>조명 그룹(영역 + Light2D 목록). 적에게는 아무 영향도 주지 않는 환경 조명 데이터.</summary>
        private static LabLightZone LightZone(MapCtx m, string name, Vector2 center, Vector2 size, float radius, bool startLit, Light2D[] lights)
        {
            GameObject go = Child("LightZone_" + name, m.zones, center);
            LabLightZone z = go.AddComponent<LabLightZone>();
            SetProp(z, "size", size);
            SetProp(z, "radius", radius);
            SetProp(z, "startLit", startLit);
            SetArray(z, "lights", lights);
            return z;
        }

        // ───────────── SerializedObject 헬퍼 ─────────────

        private static void SetProp(Object target, string prop, object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(prop);
            if (p == null)
            {
                throw new Exception("[LaboratoryMapBuilder] 속성 없음: " + target.GetType().Name + "." + prop);
            }

            if (value is bool)
            {
                p.boolValue = (bool)value;
            }
            else if (value is int)
            {
                if (p.propertyType == SerializedPropertyType.Enum)
                {
                    p.enumValueIndex = (int)value;
                }
                else
                {
                    p.intValue = (int)value;
                }
            }
            else if (value is float)
            {
                p.floatValue = (float)value;
            }
            else if (value is string)
            {
                p.stringValue = (string)value;
            }
            else if (value is Vector2)
            {
                p.vector2Value = (Vector2)value;
            }
            else if (value is Color)
            {
                p.colorValue = (Color)value;
            }
            else
            {
                p.objectReferenceValue = value as Object;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string prop, Object[] values)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(prop);
            if (p == null)
            {
                throw new Exception("[LaboratoryMapBuilder] 배열 속성 없음: " + target.GetType().Name + "." + prop);
            }

            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static LabDoor D(MapCtx m, string name)
        {
            return m.doorByName[name];
        }

        // ═════════════════════════ 7. 구역별 연출 ═════════════════════════

        // Z0 로비 ─ 튜토리얼 시작. 어둡고 조용함. 조작 힌트.
        private static void BuildZ0Lobby(MapCtx m)
        {
            Transform lights = Child("Z0_Lobby", m.lights).transform;
            AddLight(m, lights, "Lamp_Flicker_W", new Vector2(4.5f, 7f), new Color(1f, 0.85f, 0.65f), 0.55f, 5f, 0.5f, LabFlickerLight.Pattern.BrokenFlicker, 0.6f, 0.2f);
            AddLight(m, lights, "Lamp_Dim_E", new Vector2(13.5f, 6f), new Color(0.75f, 0.85f, 1f), 0.35f, 4.5f, 0.5f, LabFlickerLight.Pattern.SoftBreath, 0.5f, 0.6f);
            AddLight(m, lights, "Door_Glow", new Vector2(9f, 11.5f), new Color(0.45f, 1f, 0.7f), 0.5f, 2.5f);

            Prop(m, "reception_desk", 4.5f, 7f, true);
            Prop(m, "plant_tall", 0.5f, 10f, true);
            Prop(m, "plant_tall", 17.5f, 10f, true);
            Prop(m, "kiosk", 13.5f, 10f, true);
            Prop(m, "waiting_chairs", 14.5f, 0f, true);
            WallDecor(m, "logo_wall", 3.5f, 12f);

            Hint(m, "Move", new Vector2(9f, 5f), "WASD  MOVE     SPACE  DASH", LabHint.Mode.Proximity, 6f, HintWhite);
            Hint(m, "Flashlight", new Vector2(9f, 9.6f), "F  FLASHLIGHT", LabHint.Mode.Proximity, 3.5f, HintYellow);
        }

        // Z1 복도 ─ 불 꺼진 긴 복도. 비상등만 깜빡. 흔적 연출로 긴장감.
        private static void BuildZ1Corridor(MapCtx m)
        {
            Transform lights = Child("Z1_Corridor", m.lights).transform;
            Color red = new Color(1f, 0.2f, 0.15f);
            AddLight(m, lights, "Emergency_1", new Vector2(9.5f, 19f), red, 0.6f, 3.5f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 0.4f, 0.25f);
            AddLight(m, lights, "Emergency_2", new Vector2(9.5f, 27f), red, 0.6f, 3.5f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 0.4f, 0.25f);
            AddLight(m, lights, "Broken_Tube", new Vector2(9.5f, 33f), new Color(0.8f, 0.9f, 1f), 0.7f, 3f, 0.3f, LabFlickerLight.Pattern.BrokenFlicker, 1.4f, 0f);

            Prop(m, "cable_coil", 11.5f, 22f, false);
            Prop(m, "geiger_counter", 7.5f, 28f, false, "Dropped_Geiger");
            Decal(m, 9, 24, Slime);
            Decal(m, 10, 27, Slime);
            Decal(m, 9, 30, Slime);
            Decal(m, 8, 32, Blood);
            Decal(m, 11, 33, Splat);
            Decal(m, 9, 17, Crack);

            Hint(m, "Shoot", new Vector2(9.5f, 19.5f), "MOUSE  AIM      LMB  SHOOT", LabHint.Mode.Proximity, 4f, HintWhite);
            Hint(m, "Squad", new Vector2(9.5f, 30.5f), "...BRAVO TEAM, RESPOND...\n[ SIGNAL LOST ]", LabHint.Mode.Proximity, 3.5f, HintRed);
        }

        // Z2 첫 습격 ─ 동료가 당한 현장. 진입하면 어둠 속에서 근접 적 3마리 습격. 총은 안 통함 → 동쪽으로 도주.
        private static void BuildZ2Assault(MapCtx m)
        {
            Transform lights = Child("Z2_Assault", m.lights).transform;
            AddLight(m, lights, "Dying_Light", new Vector2(11.5f, 41.5f), new Color(0.85f, 0.95f, 1f), 0.8f, 3.5f, 0.4f, LabFlickerLight.Pattern.BrokenFlicker, 1.2f, 0.05f);

            GameObject alarms = Child("Z2_Alarms", m.lights);
            Color red = new Color(1f, 0.15f, 0.1f);
            AddLight(m, alarms.transform, "Alarm_W", new Vector2(4.5f, 47f), red, 1f, 6f, 0.5f, LabFlickerLight.Pattern.AlarmPulse, 1.2f, 0.05f);
            AddLight(m, alarms.transform, "Alarm_E", new Vector2(19.5f, 47f), red, 1f, 6f, 0.5f, LabFlickerLight.Pattern.AlarmPulse, 1.2f, 0.05f);
            AnimProp(m, alarms.transform, "warning_beacon", new Vector2(4.5f, 50.5f), 14f, false, "Default", -88);
            AnimProp(m, alarms.transform, "warning_beacon", new Vector2(19.5f, 50.5f), 14f, false, "Default", -88);
            alarms.SetActive(false);

            AnimProp(m, m.props, "sparks", new Vector2(13.5f, 50.5f), 12f, false, "Default", -88);
            Prop(m, "containment_crate", 2f, 39f, true, "Crate_Overturned");
            Prop(m, "containment_crate", 21f, 42f, true);
            Prop(m, "toxic_barrel_leak", 2f, 44f, true);
            Prop(m, "gas_cylinders", 21f, 47f, true);
            Prop(m, "hazard_barrier", 15f, 36f, false, "Fallen_Barrier");
            Prop(m, "geiger_counter", 9.5f, 43f, false, "Dropped_Gear");
            WallDecor(m, "observation_window", 10.5f, 49f);

            int[,] blood = { { 8, 41 }, { 9, 42 }, { 12, 43 }, { 13, 40 }, { 15, 42 }, { 10, 45 }, { 6, 38 }, { 17, 44 } };
            for (int i = 0; i < blood.GetLength(0); i++)
            {
                Decal(m, blood[i, 0], blood[i, 1], i % 2 == 0 ? Blood : Splat);
            }

            int[,] slime = { { 5, 45 }, { 7, 46 }, { 14, 46 }, { 18, 45 }, { 20, 41 }, { 11, 38 } };
            for (int i = 0; i < slime.GetLength(0); i++)
            {
                Decal(m, slime[i, 0], slime[i, 1], Slime);
            }

            GameObject group = MobGroup(m, "Mobs_Z2_Assault");
            Mob(m, group, 4, 46, false);
            Mob(m, group, 11, 47, false);
            Mob(m, group, 18, 46, false);

            LabHint run = Hint(m, "Run", new Vector2(16.5f, 39.5f), "BULLETS DON'T STOP THEM\nRUN  >>>", LabHint.Mode.Manual, 0f, HintRed, true);

            LabEncounter e = Encounter(m, "Z2_Assault", new Vector2(12f, 38.5f), new Vector2(22f, 3f));
            SetArray(e, "enemyGroups", new Object[] { group });
            SetProp(e, "aggroRadius", 14f);
            SetProp(e, "delay", 0.4f);
            SetProp(e, "staggerInterval", 0.35f);
            SetArray(e, "activateObjects", new Object[] { alarms });
            SetArray(e, "hints", new Object[] { run });
        }

        // Z3 도주 복도 ─ 동쪽으로 달리는 구간. 화살표/점액 흔적이 길을 안내.
        private static void BuildZ3Retreat(MapCtx m)
        {
            Transform lights = Child("Z3_Retreat", m.lights).transform;
            AddLight(m, lights, "Emergency", new Vector2(31.5f, 40f), new Color(1f, 0.25f, 0.15f), 0.6f, 4f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 0.8f, 0.2f);
            Decal(m, 25, 39, Arrow);
            Decal(m, 33, 40, Arrow);
            Decal(m, 28, 41, Slime);
            Decal(m, 37, 38, Slime);
        }

        // Z4 조명실 ─ 북쪽에 살아 있는 조명 하나 (환경 조명 대비). 진입 시 적 2마리 추격 Encounter.
        private static void BuildZ4LightRoom(MapCtx m)
        {
            Transform lights = Child("Z4_LightRoom", m.lights).transform;
            AddLight(m, lights, "Surviving_Lamp", new Vector2(48f, 45.5f), new Color(1f, 0.96f, 0.82f), 1.35f, 5.5f, 2.5f, LabFlickerLight.Pattern.SoftBreath, 0.4f, 0.9f);
            AddLight(m, lights, "Dark_Corner_Red", new Vector2(44f, 35f), new Color(1f, 0.15f, 0.1f), 0.35f, 3f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 0.5f, 0.1f);

            Prop(m, "uv_lamp", 48f, 47.2f, false, "Ceiling_Lamp_Fixture");
            Prop(m, "lab_bench", 43.5f, 47f, true);
            Prop(m, "lab_bench", 52.5f, 47f, true);
            Decal(m, 44, 36, Blood);
            Decal(m, 52, 35, Slime);
            Decal(m, 42, 40, Arrow);

            GameObject group = MobGroup(m, "Mobs_Z4_LightDemo");
            Mob(m, group, 43, 34, false);
            Mob(m, group, 52, 35, false);

            LabEncounter e = Encounter(m, "Z4_LightDemo", new Vector2(42f, 40f), new Vector2(2f, 4f));
            SetArray(e, "enemyGroups", new Object[] { group });
            SetProp(e, "aggroRadius", 12f);
            SetProp(e, "delay", 1.2f);
            SetProp(e, "staggerInterval", 0.6f);
        }

        // Z5 손전등 전투 ─ 빛을 직접 써 보는 짧은 전투 (적 3).
        private static void BuildZ5FlashRoom(MapCtx m)
        {
            Transform lights = Child("Z5_FlashRoom", m.lights).transform;
            AddLight(m, lights, "Monitor_Glow_W", new Vector2(42.5f, 60f), new Color(0.4f, 0.7f, 1f), 0.45f, 2.5f);
            AddLight(m, lights, "Monitor_Glow_E", new Vector2(53.5f, 60f), new Color(0.4f, 0.7f, 1f), 0.45f, 2.5f);

            Prop(m, "desk_computer", 42f, 60f, true);
            Prop(m, "desk_computer", 54f, 60f, true);
            Prop(m, "filing_cabinet", 41.5f, 53f, true);
            WallDecor(m, "whiteboard", 44.5f, 62f);
            WallDecor(m, "corkboard", 51.5f, 62.5f);
            Decal(m, 45, 56, Slime);
            Decal(m, 50, 58, Blood);

            GameObject group = MobGroup(m, "Mobs_Z5_Flashlight");
            Mob(m, group, 44, 58, false);
            Mob(m, group, 51, 59, false);
            Mob(m, group, 48, 60, false);

            Hint(m, "UseFlashlight", new Vector2(48f, 53.6f), "F  FLASHLIGHT", LabHint.Mode.Proximity, 4f, HintYellow);

            LabEncounter e = Encounter(m, "Z5_Flashlight", new Vector2(48f, 53f), new Vector2(6f, 2f));
            SetArray(e, "enemyGroups", new Object[] { group });
            SetProp(e, "aggroRadius", 8f);
            SetProp(e, "delay", 0.8f);
            SetProp(e, "staggerInterval", 0.5f);
        }

        // Z6 연구동 복도 ─ 방 3곳에서 문이 열리며 적이 튀어나온다.
        private static void BuildZ6LabCorridor(MapCtx m)
        {
            Transform lights = Child("Z6_LabCorridor", m.lights).transform;
            Color tube = new Color(0.8f, 0.9f, 1f);
            AddLight(m, lights, "Tube_1", new Vector2(45f, 67.5f), tube, 0.6f, 4f, 0.5f, LabFlickerLight.Pattern.BrokenFlicker, 0.5f, 0.3f);
            AddLight(m, lights, "Tube_2", new Vector2(57f, 67.5f), tube, 0.5f, 4f, 0.5f, LabFlickerLight.Pattern.BrokenFlicker, 0.8f, 0.1f);
            AddLight(m, lights, "Tube_3", new Vector2(70f, 67.5f), tube, 0.55f, 4f, 0.5f, LabFlickerLight.Pattern.BrokenFlicker, 0.6f, 0.3f);
            AddLight(m, lights, "Hall_Door_Glow", new Vector2(80f, 69f), new Color(0.5f, 1f, 0.7f), 0.5f, 2.5f);
            AddLight(m, lights, "R1_Red", new Vector2(52f, 77f), new Color(1f, 0.2f, 0.15f), 0.45f, 4f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 0.6f, 0.2f);
            AddLight(m, lights, "R2_Green", new Vector2(61f, 58f), new Color(0.4f, 1f, 0.4f), 0.4f, 3.5f, 0.3f, LabFlickerLight.Pattern.SoftBreath, 0.6f, 0.4f);
            AddLight(m, lights, "R3_Purple", new Vector2(65f, 77f), new Color(0.7f, 0.3f, 1f), 0.45f, 3.5f, 0.3f, LabFlickerLight.Pattern.SoftBreath, 0.8f, 0.3f);

            // 복도 소품: 전부 북쪽 벽에 붙임 (통로 확보)
            Prop(m, "vending_drinks", 43.5f, 69f, true);
            Prop(m, "water_cooler", 57.5f, 69f, true);
            Prop(m, "fire_suppression", 72.5f, 69f, true);
            Hint(m, "Sector", new Vector2(46f, 66.5f), "SECTOR B  -  LABS   >>>", LabHint.Mode.Proximity, 5f, HintWhite);
            Decal(m, 54, 66, Slime);
            Decal(m, 63, 68, Slime);
            Decal(m, 67, 66, Blood);

            // 방 안 소품 (벽 쪽)
            Prop(m, "specimen_tank", 49f, 80f, true);
            Prop(m, "cryo_pod_open", 54.5f, 80f, true, "CryoPod_Escaped");
            Prop(m, "quarantine_bed", 63f, 60f, true);
            AnimProp(m, m.props, "slime_tank", new Vector2(58f, 60f), 8f, true);
            Prop(m, "containment_crate", 66f, 80f, true);
            Decal(m, 51, 75, Slime);
            Decal(m, 60, 56, Slime);
            Decal(m, 64, 75, Biohazard);

            // 매복 적은 문 열(2칸) 안쪽에 세로로 배치 → 직선 추적 AI 가 문틀 모서리에 걸리지 않고 바로 튀어나온다.
            GameObject r1 = MobGroup(m, "Mobs_R1_Ambush");
            Mob(m, r1, 51, 75, false);
            Mob(m, r1, 52, 77, false);
            GameObject r2 = MobGroup(m, "Mobs_R2_Ambush");
            Mob(m, r2, 60, 59, false);
            Mob(m, r2, 61, 57, false);
            GameObject r3 = MobGroup(m, "Mobs_R3_Ambush");
            Mob(m, r3, 65, 75, false);
            Mob(m, r3, 66, 77, false);
            Mob(m, r3, 65, 79, false);

            RoomAmbush(m, "R1", new Vector2(52f, 67f), D(m, "Door_R1"), r1);
            RoomAmbush(m, "R2", new Vector2(61f, 67f), D(m, "Door_R2"), r2);
            RoomAmbush(m, "R3", new Vector2(66f, 67f), D(m, "Door_R3"), r3);

            GameObject roam = MobGroup(m, "Mobs_Z6_Corridor");
            Mob(m, roam, 74, 67);
            Mob(m, roam, 77, 66);
        }

        private static void RoomAmbush(MapCtx m, string name, Vector2 center, LabDoor door, GameObject group)
        {
            LabEncounter e = Encounter(m, name + "_Ambush", center, new Vector2(3f, 5f));
            SetArray(e, "enemyGroups", new Object[] { group });
            SetArray(e, "doorsToOpen", new Object[] { door });
            SetProp(e, "aggroRadius", 16f);
            SetProp(e, "delay", 0.25f);
            SetProp(e, "staggerInterval", 0.3f);
        }

        // Z7 대형 홀 ─ 적 무리(10). 입구 콘솔로 메인 조명 ON → 시야가 밝아진 상태로 전투 (조명은 적에게 영향 없음).
        private static void BuildZ7Hall(MapCtx m)
        {
            Zone hall = m.L.Z("Z7_Hall");
            Transform lights = Child("Z7_Hall", m.lights).transform;
            Transform mainLights = Child("Main_Lights", lights).transform;
            List<Light2D> mains = new List<Light2D>();
            float[] xs = { 76f, 85f, 94f };
            float[] ys = { 78.5f, 89f };
            foreach (float ly in ys)
            {
                foreach (float lx in xs)
                {
                    mains.Add(AddLight(m, mainLights, "Main_" + lx + "_" + ly, new Vector2(lx, ly), new Color(0.92f, 0.97f, 1f), 1.15f, 8.5f, 3f));
                }
            }

            GameObject emergency = Child("Emergency_Lights", lights);
            AddLight(m, emergency.transform, "Emergency_W", new Vector2(72f, 84f), new Color(1f, 0.2f, 0.12f), 0.4f, 4f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 0.4f, 0.2f);
            AddLight(m, emergency.transform, "Emergency_E", new Vector2(97f, 84f), new Color(1f, 0.2f, 0.12f), 0.4f, 4f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 0.4f, 0.2f);
            AddLight(m, lights, "Console_Glow", new Vector2(75f, 74.5f), new Color(0.4f, 1f, 0.6f), 0.7f, 2.5f);

            LabLightZone zone = LightZone(m, "Z7_Hall", hall.Center, hall.Size + new Vector2(0f, 1f), 0f, false, mains.ToArray());

            // 엄폐물 4개만 (넓은 통로 유지) + 벽 쪽 서버랙
            Prop(m, "server_rack_wide", 79f, 80f, true, "Cover_SW");
            Prop(m, "server_rack_wide", 91f, 80f, true, "Cover_SE");
            Prop(m, "server_rack_wide", 79f, 87f, true, "Cover_NW");
            Prop(m, "server_rack_wide", 91f, 87f, true, "Cover_NE");
            AnimProp(m, m.props, "server_rack_a", new Vector2(71.5f, 94f), 8f, true);
            AnimProp(m, m.props, "server_rack_b", new Vector2(72.5f, 94f), 8f, true);
            AnimProp(m, m.props, "server_rack_a", new Vector2(97.5f, 94f), 8f, true);
            AnimProp(m, m.props, "server_rack_b", new Vector2(98.5f, 94f), 8f, true);
            Prop(m, "supercomputer", 91.5f, 94f, true);

            GameObject group = MobGroup(m, "Mobs_Z7_Hall");
            int[,] pos = { { 74, 88 }, { 76, 90 }, { 73, 92 }, { 84, 83 }, { 86, 85 }, { 83, 86 }, { 87, 82 }, { 94, 77 }, { 96, 80 }, { 95, 89 } };
            for (int i = 0; i < pos.GetLength(0); i++)
            {
                Mob(m, group, pos[i, 0], pos[i, 1]);
            }

            GameObject sw = Child("LightSwitch_MainPower", m.encounters, new Vector2(75f, 74f));
            AnimProp(m, sw.transform, "control_console", new Vector2(75f, 73.6f), 8f, true);
            CircleCollider2D trig = sw.AddComponent<CircleCollider2D>();
            trig.isTrigger = true;
            trig.radius = 1.8f;
            sw.layer = 2;
            LabHint prompt = Hint(m, "MainPower", new Vector2(75f, 76.3f), "E  MAIN LIGHTS", LabHint.Mode.Manual, 0f, HintYellow, true);
            LabHint after = Hint(m, "LightsOn", new Vector2(85f, 84f), "MAIN LIGHTS ON", LabHint.Mode.Manual, 0f, HintYellow, false, 4f, 4f);
            LabLightSwitch s = sw.AddComponent<LabLightSwitch>();
            SetProp(s, "prompt", prompt);
            SetArray(s, "zones", new Object[] { zone });
            SetArray(s, "disableOnLightsOn", new Object[] { emergency });
            SetProp(s, "afterHint", after);

            Hint(m, "HallDark", new Vector2(81.5f, 77.5f), "TOO DARK...  FIND THE POWER", LabHint.Mode.Proximity, 3f, HintWhite);
        }

        // Z8 좁은 골목 ─ 합류 지점에 들어서면 서쪽/동쪽 끝에서 동시에 몰려온다. 10초 버티면 디펜스실 문 해제.
        private static void BuildZ8Alley(MapCtx m)
        {
            Transform lights = Child("Z8_Alley", m.lights).transform;
            Color red = new Color(1f, 0.15f, 0.1f);
            AddLight(m, lights, "Strobe_W", new Vector2(62f, 106.5f), red, 0.8f, 4.5f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 1.6f, 0.05f);
            AddLight(m, lights, "Strobe_Mid", new Vector2(74f, 106.5f), red, 0.8f, 4.5f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 1.6f, 0.05f);
            AddLight(m, lights, "Strobe_E", new Vector2(86f, 106.5f), red, 0.8f, 4.5f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 1.6f, 0.05f);
            AddLight(m, lights, "Link_Tube", new Vector2(84.5f, 101f), new Color(0.8f, 0.9f, 1f), 0.5f, 3f, 0.3f, LabFlickerLight.Pattern.BrokenFlicker, 0.7f, 0.2f);

            for (int x = 57; x <= 91; x += 6)
            {
                Decal(m, x, 106, HazardH);
            }

            Transform spawnRoot = Child("Z8_SpawnPoints", m.markers).transform;
            Marker(m, spawnRoot, "SpawnPoint_West_A", 57, 106);
            Marker(m, spawnRoot, "SpawnPoint_West_B", 58, 105);
            Marker(m, spawnRoot, "SpawnPoint_East_A", 91, 106);
            Marker(m, spawnRoot, "SpawnPoint_East_B", 90, 107);

            GameObject west = MobGroup(m, "Mobs_Z8_West");
            Mob(m, west, 57, 106, false);
            Mob(m, west, 58, 105, false);
            Mob(m, west, 58, 107, false);
            Mob(m, west, 60, 106, false);
            GameObject east = MobGroup(m, "Mobs_Z8_East");
            Mob(m, east, 91, 106, false);
            Mob(m, east, 90, 105, false);
            Mob(m, east, 90, 107, false);

            LabHint warn = Hint(m, "BothSides", new Vector2(80f, 106f), "THEY'RE COMING FROM BOTH SIDES!", LabHint.Mode.Manual, 0f, HintRed, true, 5f);
            Hint(m, "ReactorAccess", new Vector2(73.9f, 105.2f), "REACTOR ACCESS", LabHint.Mode.Proximity, 4f, HintWhite, false, 0f, 2.6f);

            LabEncounter e = Encounter(m, "Z8_Alley", new Vector2(84.5f, 104f), new Vector2(3f, 2f));
            SetArray(e, "enemyGroups", new Object[] { west, east });
            SetProp(e, "aggroRadius", 40f);
            SetProp(e, "delay", 0.3f);
            SetProp(e, "staggerInterval", 0.3f);
            SetArray(e, "doorsToUnlock", new Object[] { D(m, "Door_AlleyToDefense") });
            SetProp(e, "unlockDelay", 10f);
            SetArray(e, "hints", new Object[] { warn });
        }

        // Z9 디펜스 ─ 콘솔 E → 보스 셔터 45초에 걸쳐 열림, 사방 WavePoint 에서 웨이브.
        private static void BuildZ9Defense(MapCtx m)
        {
            Zone z = m.L.Z("Z9_Defense");
            Transform lights = Child("Z9_Defense", m.lights).transform;
            AddLight(m, lights, "Ambient_Center", new Vector2(73.5f, 120f), new Color(0.5f, 0.6f, 1f), 0.35f, 8f, 1f, LabFlickerLight.Pattern.SoftBreath, 0.3f, 0.6f);
            AddLight(m, lights, "Gate_Glow", new Vector2(74f, 130f), new Color(1f, 0.55f, 0.2f), 0.6f, 4f);

            Transform alarmRoot = Child("Alarms", lights).transform;
            Color orange = new Color(1f, 0.35f, 0.1f);
            List<Light2D> alarms = new List<Light2D>();
            Vector2[] ap = { new Vector2(64f, 113f), new Vector2(84f, 113f), new Vector2(64f, 128f), new Vector2(84f, 128f) };
            foreach (Vector2 p in ap)
            {
                Light2D l = AddLight(m, alarmRoot, "Alarm", p, orange, 1.1f, 7f, 0.5f, LabFlickerLight.Pattern.AlarmPulse, 1f, 0.1f);
                l.enabled = false;
                alarms.Add(l);
            }

            AnimProp(m, m.props, "warning_beacon", new Vector2(70.5f, 132.5f), 14f, false, "Default", -88);
            AnimProp(m, m.props, "warning_beacon", new Vector2(77.5f, 132.5f), 14f, false, "Default", -88);
            AnimProp(m, m.props, "generator", new Vector2(63f, 120f), 8f, true);
            AnimProp(m, m.props, "tesla_coil", new Vector2(85f, 119f), 10f, true);
            AnimProp(m, m.props, "capacitor_bank", new Vector2(68f, 130f), 8f, true);
            AnimProp(m, m.props, "capacitor_bank", new Vector2(80f, 130f), 8f, true);

            for (int x = 72; x <= 75; x++)
            {
                Decal(m, x, 129, HazardH);
            }

            // 웨이브 포인트 (사방): 바닥 배수구로 표시 → "환풍구에서 기어 나온다"
            Transform wpRoot = Child("Z9_WavePoints", m.markers).transform;
            int[,] wp = { { 63, 115 }, { 63, 126 }, { 84, 115 }, { 84, 125 }, { 65, 129 }, { 82, 129 }, { 64, 112 }, { 83, 112 } };
            string[] wpNames = { "WavePoint_W1", "WavePoint_W2", "WavePoint_E1", "WavePoint_E2", "WavePoint_N1", "WavePoint_N2", "WavePoint_S1", "WavePoint_S2" };
            List<Object> points = new List<Object>();
            for (int i = 0; i < wp.GetLength(0); i++)
            {
                points.Add(Marker(m, wpRoot, wpNames[i], wp[i, 0], wp[i, 1]));
                Decal(m, wp[i, 0], wp[i, 1], Drain);
            }

            for (int x = 70; x <= 77; x++)
            {
                for (int y = 116; y <= 123; y++)
                {
                    m.floor.SetTile(new Vector3Int(x, y, 0), GetTile(T(FloorSprite("grate", x, y))));
                }
            }

            GameObject waveGroup = MobGroup(m, "Mobs_Z9_Waves");

            GameObject fx = Child("FX_SpawnPuff_Template", m.systems);
            SpriteRenderer fxr = AddSprite(fx, Frames("anim_dust_puff")[0], "Environment", 5);
            fxr.transform.localScale = new Vector3(2f, 2f, 1f);
            fx.AddComponent<LabSpriteLoop>().Setup(Frames("anim_dust_puff"), 16f, true);
            fx.SetActive(false);

            GameObject console = Child("DefenseConsole", m.encounters, new Vector2(74f, 113f));
            AnimProp(m, console.transform, "control_console", new Vector2(74f, 112.6f), 8f, true);
            CircleCollider2D trig = console.AddComponent<CircleCollider2D>();
            trig.isTrigger = true;
            trig.radius = 1.8f;
            console.layer = 2;
            AddLight(m, console.transform, "Console_Glow", new Vector2(74f, 113.8f), new Color(0.4f, 1f, 0.6f), 0.7f, 2.5f);

            LabHint prompt = Hint(m, "Console", new Vector2(74f, 115.3f), "E  OPEN REACTOR GATE", LabHint.Mode.Manual, 0f, HintYellow, true);
            LabHint status = Hint(m, "DefenseStatus", new Vector2(74f, 122.5f), "GATE OPENING", LabHint.Mode.Manual, 0f, HintRed, true, 0f, 3.6f);
            LabHint done = Hint(m, "GateOpen", new Vector2(74f, 127f), "GATE OPEN  -  GO NORTH", LabHint.Mode.Manual, 0f, HintYellow, false, 6f, 3.6f);
            Hint(m, "DefenseIntro", new Vector2(74f, 117.5f), "REACTOR GATE CONTROL", LabHint.Mode.Proximity, 4f, HintWhite, false, 0f, 2.6f);

            LabDefenseEvent ev = console.AddComponent<LabDefenseEvent>();
            SetProp(ev, "prompt", prompt);
            SetArray(ev, "gates", new Object[] { D(m, "Gate_Boss_L"), D(m, "Gate_Boss_R") });
            SetProp(ev, "gateOpenDuration", 45f);
            SetArray(ev, "lockBehind", new Object[] { D(m, "Door_AlleyToDefense") });
            SetArray(ev, "unlockOnComplete", new Object[] { D(m, "Door_AlleyToDefense") });
            SetProp(ev, "enemyPrefab", m.labMob);
            SetProp(ev, "waveGroup", waveGroup.transform);
            SetArray(ev, "wavePoints", points.ToArray());
            SetProp(ev, "maxAlive", 10);
            SetProp(ev, "aggroRadius", 30f);
            SetProp(ev, "spawnEffectPrefab", fx);
            SetArray(ev, "alarmLights", alarms.ToArray());
            SetProp(ev, "statusHint", status);
            SetProp(ev, "completeHint", done);

            // 웨이브 구성 (시간, 마리 수, 포인트) : W=0,1  E=2,3  N=4,5  S=6,7
            float[] times = { 1f, 7f, 13f, 20f, 27f, 34f, 40f };
            int[] counts = { 3, 3, 4, 4, 4, 4, 3 };
            int[][] idx =
            {
                new[] { 0, 2, 4 },
                new[] { 6, 7, 5 },
                new[] { 1, 3, 4, 5 },
                new int[0],
                new[] { 0, 1, 2, 3 },
                new int[0],
                new[] { 4, 5, 6 },
            };
            SerializedObject so = new SerializedObject(ev);
            SerializedProperty waves = so.FindProperty("waves");
            waves.arraySize = times.Length;
            for (int i = 0; i < times.Length; i++)
            {
                SerializedProperty w = waves.GetArrayElementAtIndex(i);
                w.FindPropertyRelative("startTime").floatValue = times[i];
                w.FindPropertyRelative("count").intValue = counts[i];
                SerializedProperty pi = w.FindPropertyRelative("pointIndices");
                pi.arraySize = idx[i].Length;
                for (int k = 0; k < idx[i].Length; k++)
                {
                    pi.GetArrayElementAtIndex(k).intValue = idx[i][k];
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Z10 보스방 전실 ─ 맵 끝. 보스방 문은 잠겨 있음(보스맵 미구현).
        private static void BuildZ10BossAnte(MapCtx m)
        {
            Transform lights = Child("Z10_BossAnte", m.lights).transform;
            AddLight(m, lights, "Ominous", new Vector2(74f, 140f), new Color(0.75f, 0.2f, 0.9f), 0.8f, 6f, 1f, LabFlickerLight.Pattern.SoftBreath, 0.5f, 0.5f);
            AddLight(m, lights, "Beacon_L", new Vector2(71.5f, 141f), new Color(1f, 0.15f, 0.1f), 0.7f, 3f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 1f, 0.1f);
            AddLight(m, lights, "Beacon_R", new Vector2(76.5f, 141f), new Color(1f, 0.15f, 0.1f), 0.7f, 3f, 0.3f, LabFlickerLight.Pattern.AlarmPulse, 1f, 0.1f);
            AnimProp(m, m.props, "warning_beacon", new Vector2(71.5f, 143.5f), 14f, false, "Default", -88);
            AnimProp(m, m.props, "warning_beacon", new Vector2(76.5f, 143.5f), 14f, false, "Default", -88);
            Prop(m, "rad_barrel", 68.5f, 141f, true);
            Prop(m, "rad_barrel", 79.5f, 141f, true);
            Prop(m, "coolant_tank", 68.5f, 134f, true);
            Prop(m, "coolant_tank", 79.5f, 134f, true);
            for (int x = 72; x <= 75; x++)
            {
                Decal(m, x, 140, HazardH);
            }

            Decal(m, 70, 137, Radiation);
            Decal(m, 77, 137, Radiation);

            GameObject endMarker = Child("BossRoomEntrance_End", m.markers, new Vector2(74f, 139f));
            TriggerBox(endMarker, new Vector2(10f, 3f));
            LabHint end = Hint(m, "End", new Vector2(74f, 137f), "BOSS AREA AHEAD\n- PROTOTYPE END -", LabHint.Mode.Manual, 0f, HintRed, true, 0f, 4f);
            LabLevelEnd le = endMarker.AddComponent<LabLevelEnd>();
            SetProp(le, "endHint", end);
        }

        // ═════════════════════════ 8. 씬 구성 ═════════════════════════

        private static void SetupScene(LabLayout L)
        {
            GameObject globalLight = new GameObject("GlobalLight");
            Light2D gl = globalLight.AddComponent<Light2D>();
            gl.lightType = Light2D.LightType.Global;
            gl.color = new Color(0.55f, 0.62f, 0.85f);
            gl.intensity = 0.2f;
            ApplyAllSortingLayers(gl);

            Vector3 start = new Vector3(9f, 3.5f, 0f);
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.transform.position = start;

            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(start.x, start.y, -10f);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.027f, 0.047f, 1f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            CameraFollow follow = camGo.AddComponent<CameraFollow>();
            follow.target = player.transform;
        }
    }
}
