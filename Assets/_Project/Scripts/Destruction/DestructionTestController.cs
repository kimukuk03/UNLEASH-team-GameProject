using System.Collections;
using System.Collections.Generic;
using CowMeat.City;
using Unity.Profiling;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using UnityEngine.SceneManagement;

namespace CowMeat.Destruction
{
    /// <summary>
    /// Test harness (no robot/kaiju needed). Put on the camera.
    /// RMB+mouse look, WASD/QE move, Shift fast | LMB explosion | F camera beam | H horizontal beam through tower
    /// [ ] beam height | C blast base supports | R reset | P benchmark
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class DestructionTestController : MonoBehaviour
    {
        [Header("References")]
        public DestructibleStructure tower;
        public CityDamageTracker tracker;

        [Header("Explosion (LMB)")]
        public float explosionRadius = 7f;
        public float explosionDamage = 320f;
        public float explosionForce = 12f;

        [Header("Beam (F / H)")]
        public float beamRadius = 4f;
        public float beamDamage = 600f;
        public float beamPenetration = 4f;
        public float beamLength = 600f;
        public float horizontalBeamHeight = 45f;
        public float horizontalBeamStartZ = -160f;

        [Header("Camera")]
        public float moveSpeed = 40f;
        public float lookSpeed = 3f;

        private Camera cam;
        private LineRenderer line;
        private float lineUntil;
        private float yaw, pitch;
        private string lastBeamCheck = "-";
        private string lastAction = "-";
        private bool benchmarking;
        private readonly List<string> benchLines = new List<string>();

        private float fpsAccum, fpsTime, fpsValue, worstMs, worstWindow;
        private ProfilerRecorder physicsRecorder;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            Application.runInBackground = true;
            var e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
            line = gameObject.GetComponent<LineRenderer>();
            if (!line) line = gameObject.AddComponent<LineRenderer>();
            line.enabled = false;
            line.positionCount = 2;
            line.widthMultiplier = 2.5f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = new Color(0.4f, 0.8f, 1f, 0.9f);
        }

        private void OnEnable()
        {
            physicsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate");
            if (!physicsRecorder.Valid)
            {
                physicsRecorder.Dispose();
                physicsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Processing");
            }
        }

        private void OnDisable() => physicsRecorder.Dispose();

        private void Update()
        {
            TrackFps();
            if (line.enabled && Time.time > lineUntil) line.enabled = false;
            if (benchmarking) return;

            FlyCamera();

            if (Input.GetMouseButtonDown(0) && Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                Explode(hit.point);
            if (Input.GetKeyDown(KeyCode.F))
            {
                var ray = cam.ScreenPointToRay(Input.mousePosition);
                FireBeam(ray.origin, ray.direction);
            }
            if (Input.GetKeyDown(KeyCode.H)) FireHorizontalBeam();
            if (Input.GetKeyDown(KeyCode.LeftBracket)) horizontalBeamHeight = Mathf.Max(5f, horizontalBeamHeight - 5f);
            if (Input.GetKeyDown(KeyCode.RightBracket)) horizontalBeamHeight = Mathf.Min(95f, horizontalBeamHeight + 5f);
            if (Input.GetKeyDown(KeyCode.C)) BlastBaseSupports();
            if (Input.GetKeyDown(KeyCode.P)) StartBenchmark();
            if (Input.GetKeyDown(KeyCode.R)) ResetScene();
        }

        // ------------------------------------------------------------------ attacks

        public void Explode(Vector3 point)
        {
            int n = DestructionUtility.Explode(point, explosionRadius, explosionDamage, explosionForce);
            DebrisManager.Instance.EmitDust(point, 2f, 8);
            lastAction = $"Explosion at {point:F0}: {n} pieces destroyed";
        }

        public void FireBeam(Vector3 origin, Vector3 dir)
        {
            var r = DestructionUtility.FireBeam(origin, dir, beamLength, beamRadius, beamDamage, beamPenetration);
            ShowLine(origin, r.end);
            lastAction = $"Beam: {r.piecesDestroyed} pieces pulverized, {r.targetsHit} other targets hit{(r.stopped ? " (absorbed)" : "")}";
            StartCoroutine(VerifyPathNextFrame(origin, dir));
        }

        public void FireHorizontalBeam()
        {
            var c = tower ? tower.transform.position : Vector3.zero;
            FireBeam(new Vector3(c.x, horizontalBeamHeight, horizontalBeamStartZ), Vector3.forward);
        }

        /// <summary>Destroys most of the ground-level load-bearing pieces -> upper floors should collapse.</summary>
        public void BlastBaseSupports()
        {
            var c = tower ? tower.transform.position : Vector3.zero;
            int n = 0;
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && z == 0) continue;
                n += DestructionUtility.Explode(c + new Vector3(x * 15f, 5f, z * 15f), 9f, 900f, 8f);
            }
            lastAction = $"Base blast: {n} pieces destroyed";
        }

        private IEnumerator VerifyPathNextFrame(Vector3 origin, Vector3 dir)
        {
            yield return null; // let structural resolution run (LateUpdate)
            var blocker = DestructionUtility.FirstIntactPieceOnPath(origin, dir, beamLength, beamRadius * 0.5f);
            bool clear = !blocker || blocker.Structure != tower;
            lastBeamCheck = clear
                ? "CLEAR - no intact tower collider on the beam path (hole goes through)"
                : $"BLOCKED by {blocker.name}";
        }

        private void ShowLine(Vector3 a, Vector3 b)
        {
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.enabled = true;
            lineUntil = Time.time + 0.35f;
        }

        public void ResetScene()
        {
            var scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.buildIndex);
#endif
        }

        // ------------------------------------------------------------------ benchmark

        public void StartBenchmark()
        {
            if (!benchmarking) StartCoroutine(Benchmark());
        }

        private IEnumerator Benchmark()
        {
            benchmarking = true;
            benchLines.Clear();
            int total = tower ? tower.Pieces.Count : 0;
            Log($"[Bench] pieces={total}, maxActiveBodies={DebrisManager.Instance.maxActiveBodies}, physicsRecorder={(physicsRecorder.Valid ? "on" : "n/a")}");

            var dm = DebrisManager.Instance;
            if (tower) Log($"[Check] collider/visual mismatches before: {tower.CountColliderMismatches()}");
            yield return Measure("idle", 2f);
            FireHorizontalBeam();
            int fragAfterBeam = dm.ActiveFragments, chunksAfterBeam = dm.ChunkParticles;
            yield return null;
            yield return null;
            Log($"[Check] beam: path {lastBeamCheck} | {lastAction} | ejecta fragments={fragAfterBeam} chunk particles={chunksAfterBeam} " +
                $"flying pieces={dm.ActiveBodies} dust={dm.DustParticles} clouds={dm.CloudParticles}");
            yield return Measure("beam", 2f);
            Explode((tower ? tower.transform.position : Vector3.zero) + new Vector3(-8f, 25f, -15f));
            yield return Measure("explosion", 3f);

            int fracturesBefore = dm.FractureCount;
            BlastBaseSupports();
            yield return null;
            yield return null;
            int biggestBlock = 0, blockCount = 0;
            foreach (var b in FindObjectsOfType<CollapseBlock>()) { blockCount++; biggestBlock = Mathf.Max(biggestBlock, b.PieceCount); }
            Log($"[Check] collapse start: {blockCount} falling block(s), largest {biggestBlock} pieces, " +
                $"intact left {(tower ? tower.CountState(PieceState.Intact) : 0)} | clouds={dm.CloudParticles}");
            yield return Measure("collapse", 12f);
            Log($"[Check] fractures during collapse={dm.FractureCount - fracturesBefore}, peak blocks={dm.PeakActiveBlocks}, " +
                $"shredded(visual) pieces={dm.ShreddedCount}, rubble={dm.RubbleCount}");

            if (tower)
            {
                Log($"[Bench] final: intact={tower.CountState(PieceState.Intact)} detached={tower.CountState(PieceState.Detached)} " +
                    $"pulverized={tower.CountState(PieceState.Pulverized)} | tower HP={(tower.Building ? tower.Building.HealthRatio * 100f : 0f):0}% " +
                    $"| city damage={(tracker ? tracker.DamageRatio * 100f : 0f):0.0}% | impacts={dm.ImpactCount}");
                string dmg = tower.ValidateDamage(out bool ok);
                Log($"[Check] damage {(ok ? "OK (no double count)" : "MISMATCH")}: {dmg}");
                Log($"[Check] collider/visual mismatches after: {tower.CountColliderMismatches()}");
            }
            benchmarking = false;
        }

        private IEnumerator Measure(string label, float seconds)
        {
            var dm = DebrisManager.Instance;
            dm.ResetStats();
            var frames = new List<float>();
            float physSum = 0f, physMax = 0f;
            int physSamples = 0, peakBlocks = 0;
            float t0 = Time.realtimeSinceStartup;
            yield return null;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                frames.Add(Time.unscaledDeltaTime * 1000f);
                if (physicsRecorder.Valid)
                {
                    float ms = physicsRecorder.LastValue / 1e6f;
                    physSum += ms; physMax = Mathf.Max(physMax, ms); physSamples++;
                }
                peakBlocks = Mathf.Max(peakBlocks, dm.ActiveBlocks);
                yield return null;
            }
            frames.Sort();
            float avg = 0f;
            foreach (var f in frames) avg += f;
            avg /= Mathf.Max(1, frames.Count);
            float p95 = frames.Count > 0 ? frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.95f))] : 0f;
            float max = frames.Count > 0 ? frames[frames.Count - 1] : 0f;
            string phys = physSamples > 0 ? $" | physics avg {physSum / physSamples:0.00} ms, max {physMax:0.00} ms" : "";
            Log($"[Bench] {label,-9} frames={frames.Count} avg={avg:0.0} ms ({1000f / Mathf.Max(0.01f, avg):0} fps) p95={p95:0.0} max={max:0.0}{phys} " +
                $"| peak bodies={dm.PeakActiveBodies} blocks={peakBlocks} rubble={dm.RubbleCount} dust={dm.DustParticles} " +
                $"clouds={dm.CloudParticles} chunks={dm.ChunkParticles} fragments={dm.ActiveFragments}");
        }

        private void Log(string s)
        {
            benchLines.Add(s);
            Debug.Log(s);
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
            GUILayout.BeginArea(new Rect(10, 10, 680, 560), GUI.skin.box);
            GUILayout.Label("<b>Destruction Test</b>  RMB look | WASD/QE move (Shift fast) | LMB explosion | F beam from camera\n" +
                            $"H horizontal beam (height {horizontalBeamHeight:0} m, [ ] to change) | C blast base supports | P benchmark | R reset",
                            new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"FPS {fpsValue:0} | worst frame {worstMs:0.0} ms" +
                            (physicsRecorder.Valid ? $" | physics {physicsRecorder.LastValue / 1e6f:0.00} ms" : ""));
            if (tower)
                GUILayout.Label($"Pieces intact {tower.CountState(PieceState.Intact)} / detached {tower.CountState(PieceState.Detached)} / " +
                                $"pulverized {tower.CountState(PieceState.Pulverized)}  (total {tower.Pieces.Count})");
            GUILayout.Label($"Dynamic bodies {dm.ActiveBodies} (peak {dm.PeakActiveBodies}, max {dm.maxActiveBodies}) | falling blocks {dm.ActiveBlocks} | " +
                            $"rubble {dm.RubbleCount} | impacts {dm.ImpactCount}");
            GUILayout.Label($"Dust {dm.DustParticles} | clouds {dm.CloudParticles} | chunks {dm.ChunkParticles} | fragments {dm.ActiveFragments} | " +
                            $"fractures {dm.FractureCount} | shredded {dm.ShreddedCount}");
            if (tower && tower.Building) GUILayout.Label($"Tower HP {tower.Building.HealthRatio * 100f:0}% ({tower.Building.State})");
            if (tracker) GUILayout.Label($"City damage {tracker.DamageRatio * 100f:0.0}% | collapsed buildings {tracker.CollapsedCount}");
            GUILayout.Label($"Last action: {lastAction}");
            GUILayout.Label($"Beam path check: {lastBeamCheck}");
            if (benchmarking) GUILayout.Label("BENCHMARK RUNNING...");
            foreach (var l in benchLines) GUILayout.Label(l);
            GUILayout.EndArea();
        }
    }
}
