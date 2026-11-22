using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unleash.Laboratory.EditorTools
{
    /// <summary>
    /// 연구소 맵 점검 도구. 메뉴: UNLEASH / Laboratory / Validate (현재 열린 씬 기준)
    ///  - Missing Script / Missing Reference 검사
    ///  - 실제 Wall 콜라이더로 시작점 → 보스방 입구까지 이동 가능 여부 (문 열림 가정 / 문 닫힘 상태 각각)
    ///  - 적 배치 수 집계
    /// </summary>
    public static class LaboratoryMapValidator
    {
        [MenuItem("UNLEASH/Laboratory/Validate", priority = 1)]
        public static void ValidateMenu()
        {
            Debug.Log(Run());
        }

        public static string Run()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[Laboratory Validate] scene = " + SceneManager.GetActiveScene().path);

            int missingScripts = 0;
            int missingRefs = 0;
            List<string> issues = new List<string>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    int ms = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (ms > 0)
                    {
                        missingScripts += ms;
                        issues.Add("Missing script: " + Path(t));
                    }

                    foreach (Component c in t.GetComponents<Component>())
                    {
                        if (c == null)
                        {
                            continue;
                        }

                        SerializedObject so = new SerializedObject(c);
                        SerializedProperty p = so.GetIterator();
                        while (p.NextVisible(true))
                        {
                            if (p.propertyType == SerializedPropertyType.ObjectReference
                                && p.objectReferenceValue == null
                                && p.objectReferenceInstanceIDValue != 0)
                            {
                                missingRefs++;
                                issues.Add("Missing reference: " + Path(t) + " / " + c.GetType().Name + "." + p.propertyPath);
                            }
                        }
                    }
                }
            }

            sb.AppendLine("Missing scripts: " + missingScripts + ", Missing references: " + missingRefs);
            foreach (string i in issues)
            {
                sb.AppendLine("  " + i);
            }

            // ── 이동 가능 경로 ──
            GameObject start = GameObject.Find("PlayerStart");
            GameObject end = GameObject.Find("BossRoomEntrance_End");
            if (start != null && end != null)
            {
                LabDoor[] doors = Object.FindObjectsOfType<LabDoor>();
                Physics2D.SyncTransforms();
                int pathOpen = PathLength(start.transform.position, end.transform.position, doors, true);
                int pathClosed = PathLength(start.transform.position, end.transform.position, doors, false);
                sb.AppendLine("Path start→boss entrance (doors open): " + (pathOpen >= 0 ? "OK, " + pathOpen + " cells" : "BLOCKED"));
                sb.AppendLine("Path start→boss entrance (doors closed, initial state): " + (pathClosed >= 0 ? pathClosed + " cells (!)" : "blocked by closed doors (expected)"));
            }
            else
            {
                sb.AppendLine("PlayerStart / BossRoomEntrance_End marker not found.");
            }

            // ── 적 배치 ──
            int total = 0;
            foreach (MobController mc in Object.FindObjectsOfType<MobController>(true))
            {
                int n = mc.GetComponentsInChildren<Monster>(true).Length;
                total += n;
                sb.AppendLine("  " + mc.name + ": " + n);
            }

            sb.AppendLine("Enemies placed: " + total + " (+ defense waves at runtime)");
            return sb.ToString();
        }

        /// <summary>
        /// 실제 Physics2D 콜라이더(Wall 레이어)로 1x1 플레이어 히트박스가 지나갈 수 있는 칸을 BFS.
        /// doorsOpen=true 면 문 콜라이더를 잠시 끄고 검사한다.
        /// </summary>
        private static int PathLength(Vector2 from, Vector2 to, LabDoor[] doors, bool doorsOpen)
        {
            int wallMask = LayerMask.GetMask(LaboratoryMapBuilder.WallLayerName);
            Dictionary<Collider2D, bool> saved = new Dictionary<Collider2D, bool>();
            if (doorsOpen)
            {
                foreach (LabDoor d in doors)
                {
                    Collider2D c = d.GetComponent<Collider2D>();
                    if (c != null)
                    {
                        saved[c] = c.enabled;
                        c.enabled = false;
                    }
                }

                Physics2D.SyncTransforms();
            }

            try
            {
                Vector2Int s = new Vector2Int(Mathf.FloorToInt(from.x), Mathf.FloorToInt(from.y));
                Vector2Int e = new Vector2Int(Mathf.FloorToInt(to.x), Mathf.FloorToInt(to.y));
                Queue<Vector2Int> q = new Queue<Vector2Int>();
                Dictionary<Vector2Int, int> dist = new Dictionary<Vector2Int, int>();
                q.Enqueue(s);
                dist[s] = 0;
                Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
                while (q.Count > 0)
                {
                    Vector2Int c = q.Dequeue();
                    if (c == e)
                    {
                        return dist[c];
                    }

                    foreach (Vector2Int d in dirs)
                    {
                        Vector2Int n = c + d;
                        if (dist.ContainsKey(n) || n.x < -10 || n.y < -10 || n.x > 130 || n.y > 170)
                        {
                            continue;
                        }

                        Vector2 center = new Vector2(n.x + 0.5f, n.y + 0.5f);
                        if (Physics2D.OverlapBox(center, new Vector2(0.92f, 0.92f), 0f, wallMask) != null)
                        {
                            continue;
                        }

                        dist[n] = dist[c] + 1;
                        q.Enqueue(n);
                    }
                }

                return -1;
            }
            finally
            {
                foreach (KeyValuePair<Collider2D, bool> kv in saved)
                {
                    kv.Key.enabled = kv.Value;
                }
            }
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }

            return p;
        }
    }
}
