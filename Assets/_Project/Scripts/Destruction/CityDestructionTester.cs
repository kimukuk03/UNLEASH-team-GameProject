using System.Collections;
using System.Collections.Generic;
using CowMeat.City;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace CowMeat.Destruction
{
    /// <summary>
    /// Test harness for the large city (put on the camera).
    /// RMB look, WASD/QE move (Shift fast) | LMB explosion | F beam | C collapse the destructible building nearest to the aim
    /// | 1..5 collapse that many destructible buildings at once | B benchmark 1/3/5 simultaneous collapses (reloads the scene between runs) | R reset
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CityDestructionTester : MonoBehaviour
    {
        [Header("References")]
        public List<DestructibleStructure> structures = new List<DestructibleStructure>();
        public CityDamageTracker tracker;

        [Header("Attacks")]
        public float explosionRadius = 8f;
        public float explosionDamage = 350f;
        public float explosionForce = 12f;
        public float beamRadius = 4f;
        public float beamDamage = 600f;
        public float beamPenetration = 4f;
        public float beamLength = 900f;

        [Header("Camera")]
        public float moveSpeed = 60f;
        public float lookSpeed = 3f;

        [Header("Benchmark")]
        public float benchSeconds = 10f;

        private static readonly int[] BenchCounts = { 1, 3, 5 };
        private static int benchStage = -1;
        private static readonly List<string> benchResults = new List<string>();

        private Camera cam;
        private LineRenderer line;
        private float lineUntil, yaw, pitch;
        private string lastAction = "-";
        private bool busy;
        private float fpsAccum, fpsTime, fpsValue, worstMs, worstWindow;
        private ProfilerRecorder physicsRecorder;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            Application.runInBackground = true;
            var e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
            line = GetComponent<LineRenderer>();
            if (!line) line = gameObject.AddComponent<LineRenderer>();
            line.enabled = false;
            line.positionCount = 2;
            line.widthMultiplier = 2.5f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = new Color(0.4f, 0.8f, 1f, 0.9f);
            structures.RemoveAll(s => !s);
        }

        private void OnEnable() => physicsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate");
        private void OnDisable() => physicsRecorder.Dispose();

        /// <summary>
        /// "-perfLegacy": turns the performance optimizations off at runtime (A/B comparison in the same build):
        /// no GPU instancing, no fracture/resolve time-slicing, no FX distance LOD / cloud cap, old timestep cap.
        /// </summary>
        private static void ApplyLegacyPerfMode()
        {
            var dm = DebrisManager.Instance;
            dm.maxFracturesPerFrame = 1000;
            dm.maxStructureResolvesPerFrame = 1000;
            dm.fxLodFarScale = 1f;
            dm.maxCloudsPerCell = 0;
            Time.maximumDeltaTime = 1f / 3f;
            var seen = new HashSet<Material>();
            foreach (var r in FindObjectsOfType<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m && seen.Add(m)) m.enableInstancing = false;
            foreach (var ps in dm.GetComponentsInChildren<ParticleSystemRenderer>())
                if (ps.name == "DustCloudFX") ps.maxParticleSize = 3f;
        }

        private static bool LegacyPerf => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-perfLegacy") >= 0;

        private void Start()
        {
            if (LegacyPerf) ApplyLegacyPerfMode();
            if (benchStage < 0 && AutoBench && !autoStarted)
            {
                autoStarted = true;
                benchResults.Clear();
                benchStage = 0;
            }
            if (benchStage >= 0) StartCoroutine(RunBenchStage());
        }

        private void Update()
        {
            TrackFps();
            if (line.enabled && Time.time > lineUntil) line.enabled = false;
            if (busy) return;
            FlyCamera();

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Input.GetMouseButtonDown(0) && Physics.Raycast(ray, out var hit, 3000f, ~0, QueryTriggerInteraction.Ignore))
            {
                int n = DestructionUtility.Explode(hit.point, explosionRadius, explosionDamage, explosionForce);
                DebrisManager.Instance.EmitDust(hit.point, 2f, 8);
                lastAction = $"Explosion: {n} pieces destroyed";
            }
            if (Input.GetKeyDown(KeyCode.F))
            {
                var r = DestructionUtility.FireBeam(ray.origin, ray.direction, beamLength, beamRadius, beamDamage, beamPenetration);
                line.SetPosition(0, ray.origin + ray.direction * 2f);
                line.SetPosition(1, r.end);
                line.enabled = true;
                lineUntil = Time.time + 0.35f;
                lastAction = $"Beam: {r.piecesDestroyed} pieces destroyed, {r.targetsHit} other targets hit";
            }
            if (Input.GetKeyDown(KeyCode.C)) CollapseAimed(ray);
            for (int k = 1; k <= 5; k++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + k)) Collapse(k);
            if (Input.GetKeyDown(KeyCode.B)) StartBenchmark();
            if (Input.GetKeyDown(KeyCode.R)) Reload();
        }

        // ------------------------------------------------------------------ actions

        private void CollapseAimed(Ray ray)
        {
            DestructibleStructure best = null;
            float bestD = float.MaxValue;
            foreach (var s in structures)
            {
                if (!s || s.CountState(PieceState.Intact) == 0) continue;
                var p = s.transform.position;
                float d = Vector3.Cross(ray.direction, p - ray.origin).sqrMagnitude; // distance to the aim ray
                if (d < bestD) { bestD = d; best = s; }
            }
            if (!best) { lastAction = "No standing destructible building"; return; }
            int n = DestructionUtility.BlastBase(best);
            lastAction = $"Collapse {best.name}: {n} base pieces destroyed";
        }

        public int Collapse(int count)
        {
            int done = 0, destroyed = 0;
            foreach (var s in structures)
            {
                if (done >= count) break;
                if (!s || s.CountState(PieceState.Intact) == 0) continue;
                destroyed += DestructionUtility.BlastBase(s);
                done++;
            }
            lastAction = $"Collapse x{done}: {destroyed} base pieces destroyed";
            return done;
        }

        private void Reload()
        {
            var scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.buildIndex);
#endif
        }

        // ------------------------------------------------------------------ benchmark

        /// <summary>Runs 1, 3 and 5 simultaneous collapses (scene reloaded between runs), ~40 s total.</summary>
        public void StartBenchmark()
        {
            if (busy) return;
            benchResults.Clear();
            benchStage = 0;
            StartCoroutine(RunBenchStage());
        }

        private static bool AutoBench => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-cityBench") >= 0;
        private static bool autoStarted;

        private IEnumerator RunBenchStage()
        {
            busy = true;
            // VSync would cap the frame at 16.7 ms and hide the real cost.
            int vsync = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            int count = BenchCounts[benchStage];
            yield return new WaitForSecondsRealtime(1f);
            var idle = new Sample();
            yield return Measure(idle, 1.5f, null);
            Collapse(count);
            var run = new Sample();
            using (var rec = new BenchRecorder())
            {
                yield return Measure(run, benchSeconds, rec);
                var dm = DebrisManager.Instance;
                int ok = 0;
                foreach (var s in structures) { if (s) { s.ValidateDamage(out bool good); if (good) ok++; } }
                string result = $"[CityBench] {count} building(s): avg {run.avg:0.0} ms ({1000f / Mathf.Max(0.01f, run.avg):0} fps), p95 {run.p95:0.0}, " +
                                $"max {run.max:0.0} ms | idle avg {idle.avg:0.0} ms | " +
                                $"peak bodies {dm.PeakActiveBodies}, peak blocks {dm.PeakActiveBlocks}, fractures {dm.FractureCount}, " +
                                $"shredded {dm.ShreddedCount}, rubble {dm.RubbleCount}, peak dust {run.peakDust}, peak clouds {run.peakClouds}, " +
                                $"peak chunks {run.peakChunks} | damage checks OK {ok}/{structures.Count} | city damage {(tracker ? tracker.DamageRatio * 100f : 0f):0.0}%" +
                                $"\n   breakdown (avg/max): {rec.Summary()}" +
                                $"\n   worst frames:{rec.WorstFrames(4)}";
                Debug.Log(result);
                benchResults.Add(result);
            }
            QualitySettings.vSyncCount = vsync;

            benchStage++;
            if (benchStage < BenchCounts.Length)
            {
                Reload();
                yield break;
            }
            benchStage = -1;
            string all = "[CityBench] done\n" + string.Join("\n", benchResults);
            Debug.Log(all);
            WriteBenchFile(all);
            busy = false;
            if (AutoBench && !Application.isEditor) Application.Quit();
        }

        private static void WriteBenchFile(string text)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-benchOut");
            if (i < 0 || i + 1 >= args.Length) return;
            try { System.IO.File.WriteAllText(args[i + 1], text); }
            catch (System.Exception e) { Debug.LogWarning($"[CityBench] could not write {args[i + 1]}: {e.Message}"); }
        }

        private class Sample
        {
            public float avg, p95, max;
            public int peakDust, peakClouds, peakChunks;
        }

        private IEnumerator Measure(Sample s, float seconds, BenchRecorder rec)
        {
            var dm = DebrisManager.Instance;
            dm.ResetStats();
            var frames = new List<float>();
            int lastFract = dm.FractureCount, lastBlocks = dm.BlocksCreated, lastBodies = dm.BodiesSpawned, lastShred = dm.ShreddedCount;
            float t0 = Time.realtimeSinceStartup;
            yield return null;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                float ms = Time.unscaledDeltaTime * 1000f;
                frames.Add(ms);
                if (rec != null)
                {
                    // Events of the PREVIOUS frame are what this frame's timing reflects (recorders report last frame).
                    string evt = "";
                    int df = dm.FractureCount - lastFract, db = dm.BlocksCreated - lastBlocks, dbo = dm.BodiesSpawned - lastBodies, ds = dm.ShreddedCount - lastShred;
                    if (df > 0) evt += $"fract+{df} ";
                    if (db > 0) evt += $"blocks+{db} ";
                    if (dbo > 0) evt += $"bodies+{dbo} ";
                    if (ds > 0) evt += $"shred+{ds} ";
                    evt += $"act {dm.ActiveBodies}b/{dm.ActiveBlocks}k clouds {dm.CloudParticles} chunks {dm.ChunkParticles}";
                    rec.Sample(ms, evt);
                    lastFract = dm.FractureCount; lastBlocks = dm.BlocksCreated; lastBodies = dm.BodiesSpawned; lastShred = dm.ShreddedCount;
                }
                s.peakDust = Mathf.Max(s.peakDust, dm.DustParticles);
                s.peakClouds = Mathf.Max(s.peakClouds, dm.CloudParticles);
                s.peakChunks = Mathf.Max(s.peakChunks, dm.ChunkParticles);
                yield return null;
            }
            frames.Sort();
            foreach (var f in frames) s.avg += f;
            s.avg /= Mathf.Max(1, frames.Count);
            if (frames.Count > 0)
            {
                s.p95 = frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.95f))];
                s.max = frames[frames.Count - 1];
            }
        }

        // ------------------------------------------------------------------ camera & HUD

        private void FlyCamera()
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * lookSpeed;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSpeed, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            var move = new Vector3(
                (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f);
            transform.position += transform.TransformDirection(move) * speed * Time.unscaledDeltaTime;
        }

        private void TrackFps()
        {
            float dt = Time.unscaledDeltaTime;
            fpsAccum += 1f; fpsTime += dt;
            worstWindow = Mathf.Max(worstWindow, dt * 1000f);
            if (fpsTime >= 0.5f)
            {
                fpsValue = fpsAccum / fpsTime;
                worstMs = worstWindow;
                fpsAccum = 0f; fpsTime = 0f; worstWindow = 0f;
            }
        }

        private void OnGUI()
        {
            var dm = DebrisManager.Instance;
            GUILayout.BeginArea(new Rect(10, 10, 720, 300), GUI.skin.box);
            GUILayout.Label("City destruction test | RMB look, WASD/QE move (Shift) | LMB explosion | F beam | C collapse aimed building\n" +
                            "1-5 collapse N buildings | B benchmark 1/3/5 (reloads scene) | R reset");
            GUILayout.Label($"FPS {fpsValue:0} | worst {worstMs:0.0} ms" + (physicsRecorder.Valid ? $" | physics {physicsRecorder.LastValue / 1e6f:0.00} ms" : ""));
            GUILayout.Label($"Bodies {dm.ActiveBodies}/{dm.maxActiveBodies} | blocks {dm.ActiveBlocks} | fragments {dm.ActiveFragments} | rubble {dm.RubbleCount} | " +
                            $"dust {dm.DustParticles} clouds {dm.CloudParticles} chunks {dm.ChunkParticles}");
            int standing = 0;
            foreach (var s in structures) if (s && s.CountState(PieceState.Intact) > 0) standing++;
            GUILayout.Label($"Destructible buildings standing {standing}/{structures.Count}" +
                            (tracker ? $" | city damage {tracker.DamageRatio * 100f:0.0}% | collapsed {tracker.CollapsedCount}" : ""));
            GUILayout.Label($"Last: {lastAction}");
            if (benchStage >= 0) GUILayout.Label($"BENCHMARK {benchStage + 1}/{BenchCounts.Length} RUNNING...");
            foreach (var l in benchResults) GUILayout.Label(l);
            GUILayout.EndArea();
        }
    }
}
