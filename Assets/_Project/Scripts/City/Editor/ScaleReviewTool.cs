using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CowMeat.City.EditorTools
{
    /// <summary>
    /// Temporary size proxies (robot ~50 m, kaiju ~60 m) and a cover/beam line-of-sight report.
    /// Proxies live under a separate root "_ScaleProxies" (no colliders) and are not touched by city regeneration.
    /// </summary>
    public static class ScaleReviewTool
    {
        private const string RootName = "_ScaleProxies";
        private const string MaterialFolder = "Assets/_Project/Materials/Debug";

        public const float RobotHeight = 50f;
        public const float RobotWidth = 26f;   // shoulder span incl. arms
        public const float MonsterHeight = 60f;
        private static readonly Vector3 MonsterPos = new Vector3(0f, 0f, 45f);
        private static readonly Vector3 RobotPos = new Vector3(0f, 0f, -70f);
        private static Vector3 BeamOrigin => MonsterPos + new Vector3(0f, 54f, -14f); // mouth height

        [MenuItem("Tools/City/Scale Review/Create Scale Proxies + Report", priority = 20)]
        public static void CreateProxiesAndReport()
        {
            RemoveProxies();
            var robotMat = Mat("Proxy_Robot", new Color(0.2f, 0.75f, 1f));
            var monsterMat = Mat("Proxy_Monster", new Color(0.85f, 0.2f, 0.15f));

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Scale Proxies");

            CreateRobot(root.transform, "Robot_50m_PlayerSpawn", RobotPos, 0f, robotMat);
            CreateMonster(root.transform, "Kaiju_60m", MonsterPos, 180f, monsterMat);

            var report = new StringBuilder();
            var cover = FindCoverBuilding(out var coverPos);
            if (cover)
            {
                var yaw = Quaternion.LookRotation(Flat(MonsterPos - coverPos)).eulerAngles.y;
                CreateRobot(root.transform, $"Robot_50m_CoverTest_behind_{cover.BuildingId}", coverPos, yaw, robotMat);
                report.AppendLine(LineOfSightReport(coverPos, cover));
            }

            report.Insert(0, BuildingCoverStats());
            foreach (var line in report.ToString().Split('\n'))
                if (!string.IsNullOrWhiteSpace(line)) Debug.Log("[ScaleReview] " + line.TrimEnd());
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        [MenuItem("Tools/City/Scale Review/Remove Scale Proxies", priority = 21)]
        public static void RemoveProxies()
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == RootName) Undo.DestroyObjectImmediate(go);
        }

        // ---------------------------------------------------------------- report

        private static string BuildingCoverStats()
        {
            var buildings = Object.FindObjectsOfType<Building>();
            int tallEnough = 0, anyDir = 0, someDir = 0, none = 0;
            foreach (var b in buildings)
            {
                if (b.Height < RobotHeight + 5f) { none++; continue; }
                tallEnough++;
                float minW = Mathf.Min(b.Footprint.x, b.Footprint.y), maxW = Mathf.Max(b.Footprint.x, b.Footprint.y);
                if (minW >= RobotWidth) anyDir++;
                else if (maxW >= RobotWidth) someDir++;
                else none++;
            }
            return $"Buildings {buildings.Length}: taller than robot+5m: {tallEnough} | hides robot (width>={RobotWidth}) from any side: {anyDir} | " +
                   $"only from 2 sides (thin slab): {someDir} | no full cover: {none}\n";
        }

        /// <summary>Picks a tall building south of the kaiju near the arena and returns a hiding spot behind it.</summary>
        private static Building FindCoverBuilding(out Vector3 hidePos)
        {
            hidePos = default;
            Building best = null;
            float bestScore = float.MaxValue;
            foreach (var b in Object.FindObjectsOfType<Building>())
            {
                var p = b.transform.position;
                if (p.z > 0f || b.Height < RobotHeight + 5f) continue;
                var away = Flat(p - MonsterPos).normalized;
                var perp = new Vector3(away.z, 0f, -away.x);
                float coverWidth = Mathf.Abs(perp.x) * b.Footprint.x + Mathf.Abs(perp.z) * b.Footprint.y;
                if (coverWidth < RobotWidth) continue; // too narrow seen from the kaiju
                float extent = Mathf.Abs(away.x) * b.Footprint.x * 0.5f + Mathf.Abs(away.z) * b.Footprint.y * 0.5f;
                var pos = new Vector3(p.x, 0f, p.z) + away * (extent + 14f);
                if (Mathf.Abs(pos.x) > 140f || Mathf.Abs(pos.z) > 140f) continue; // must stay inside the map
                float score = Flat(p - MonsterPos).magnitude;
                if (score < bestScore) { bestScore = score; best = b; hidePos = pos; }
            }
            return best;
        }

        private static string LineOfSightReport(Vector3 robotPos, Building cover)
        {
            Physics.SyncTransforms();
            int mask = LayerMask.GetMask("Building");
            var sb = new StringBuilder($"Beam LOS from kaiju mouth {BeamOrigin} to robot behind {cover.BuildingId} (h={cover.Height:0}) at {robotPos}:\n");
            int blocked = 0;
            float[] heights = { 8f, 25f, 40f, RobotHeight - 2f };
            foreach (float h in heights)
            {
                var target = robotPos + Vector3.up * h;
                bool hit = Physics.Linecast(BeamOrigin, target, out var info, mask, QueryTriggerInteraction.Ignore);
                if (hit) blocked++;
                string by = hit ? info.collider.GetComponentInParent<Building>()?.BuildingId + "/" + info.collider.name : "-";
                sb.AppendLine($"  robot y={h,4:0}: {(hit ? "BLOCKED by " + by : "EXPOSED")}");
            }
            sb.Append($"  => {blocked}/{heights.Length} sample points covered");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- proxies

        private static void CreateRobot(Transform parent, string name, Vector3 pos, float yaw, Material m)
        {
            var r = Root(parent, name, pos, yaw);
            Box(r, "Leg_L", new Vector3(-5f, 11f, 0f), new Vector3(6f, 22f, 7f), m);
            Box(r, "Leg_R", new Vector3(5f, 11f, 0f), new Vector3(6f, 22f, 7f), m);
            Box(r, "Torso", new Vector3(0f, 32f, 0f), new Vector3(20f, 20f, 12f), m);
            Box(r, "Arm_L", new Vector3(-12.5f, 30f, 0f), new Vector3(5f, 20f, 6f), m);
            Box(r, "Arm_R", new Vector3(12.5f, 30f, 0f), new Vector3(5f, 20f, 6f), m);
            Box(r, "Head", new Vector3(0f, 46f, 0f), new Vector3(8f, 8f, 8f), m);
        }

        private static void CreateMonster(Transform parent, string name, Vector3 pos, float yaw, Material m)
        {
            var r = Root(parent, name, pos, yaw); // faces local +Z, tail along -Z
            Box(r, "Leg_L", new Vector3(-8f, 10f, -2f), new Vector3(10f, 20f, 12f), m);
            Box(r, "Leg_R", new Vector3(8f, 10f, -2f), new Vector3(10f, 20f, 12f), m);
            Box(r, "Body", new Vector3(0f, 34f, 0f), new Vector3(24f, 30f, 26f), m);
            Box(r, "Arm_L", new Vector3(-13f, 38f, 12f), new Vector3(5f, 12f, 5f), m);
            Box(r, "Arm_R", new Vector3(13f, 38f, 12f), new Vector3(5f, 12f, 5f), m);
            Box(r, "Head", new Vector3(0f, 54f, 8f), new Vector3(11f, 12f, 16f), m);
            float z = -13f, size = 12f, y = 20f;
            for (int i = 0; i < 5; i++)
            {
                Box(r, $"Tail_{i}", new Vector3(0f, y, z - 7f), new Vector3(size, size, 14f), m);
                z -= 14f; size -= 1.8f; y = Mathf.Max(size * 0.5f, y - 4f);
            }
        }

        private static Transform Root(Transform parent, string name, Vector3 pos, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = pos;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        private static void Box(Transform p, string n, Vector3 c, Vector3 s, Material m) => PrimitiveBuilder.Box(n, p, c, s, m);

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static Material Mat(string name, Color c)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/_Project/Materials", "Debug");
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = c;
            m.SetFloat("_Glossiness", 0.3f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
