using System.Collections.Generic;
using CowMeat.City;
using UnityEngine;

namespace CowMeat.Destruction
{
    /// <summary>
    /// Owns everything that moves after destruction.
    ///  - Collapse blocks: unsupported parts fall as big compound rigidbodies and fracture progressively
    ///    (big block -> 2-3 smaller blocks -> single pieces) on hard impacts.
    ///  - Debris bodies: single detached pieces (budgeted), frozen into static rubble when settled.
    ///  - Fragments: small pooled rigid chunks (beam ejecta), Rigidbody removed when settled.
    ///  - Visual-only FX: dust puffs, large dust clouds, concrete chunk particles and chips (no physics bodies).
    /// Anything over a physics budget turns into visual chunks + dust instead of silently vanishing.
    /// </summary>
    public class DebrisManager : MonoBehaviour
    {
        private static DebrisManager instance;

        public static DebrisManager Instance
        {
            get
            {
                if (instance) return instance;
                instance = FindObjectOfType<DebrisManager>();
                if (!instance) instance = new GameObject("DebrisManager").AddComponent<DebrisManager>();
                return instance;
            }
        }

        [Header("Budget - single debris pieces (rigidbodies)")]
        [Tooltip("Max simultaneously simulated single-piece rigidbodies. Overflow becomes visual chunk particles + dust.")]
        public int maxActiveBodies = 100;
        [Tooltip("Max settled rubble pieces kept as static colliders (no Rigidbody). Oldest are hidden.")]
        public int maxRubble = 1500;
        public float maxBodyLifetime = 10f;
        [Tooltip("Mass per cubic metre (scaled down to keep the solver stable).")]
        public float density = 0.15f;
        [Tooltip("When the body budget is over 70% used, pieces smaller than this volume (m^3) become visual chunks instead.")]
        public float minPhysicalVolume = 25f;

        [Header("Collapse blocks (big rigid chunks)")]
        public int maxActiveBlocks = 30;
        [Tooltip("Unsupported groups bigger than this start falling as several stacked chunks instead of one.")]
        public int maxInitialBlockPieces = 110;
        [Tooltip("Fracture never creates blocks smaller than this; smaller remainders fall as single pieces.")]
        public int minBlockSize = 6;
        [Tooltip("Velocity change (m/s) of one impact needed to fracture a falling block. Light debris never counts.")]
        public float blockShatterDeltaV = 6f;
        [Tooltip("A new block cannot fracture during this time (lets stacked chunks separate first).")]
        public float blockGraceTime = 0.35f;
        [Tooltip("Pieces within this radius of the impact point break off the block (scaled up by impact strength).")]
        public float impactBreakRadius = 8f;
        [Tooltip("How many times a block can split into smaller blocks before it falls apart into single pieces.")]
        public int maxFractureGenerations = 3;
        [Tooltip("The intact rest of a fractured block is split into about this many blocks.")]
        public int fractureSplitCount = 2;
        [Tooltip("Initial toppling spin (rad/s) of a collapsing upper part.")]
        public float topplingSpin = 0.22f;
        public float blockMaxLifetime = 25f;

        [Header("Fragments (pooled small rigid chunks, e.g. beam ejecta)")]
        public int maxFragmentBodies = 40;
        public int maxFragmentObjects = 160;
        public int fragmentsPerBeamPiece = 3;
        public Vector2 fragmentSize = new Vector2(1.2f, 3.2f);

        [Header("Impact damage")]
        public float impactMinSpeed = 6f;
        public float impactDamagePerMomentum = 1.5f;
        public float blockCrushDamagePerMomentum = 0.05f;
        public float blockCrushRadius = 10f;

        [Header("FX amounts (0 = off, 1 = default)")]
        [Range(0f, 3f)] public float dustAmount = 1f;
        [Range(0f, 3f)] public float cloudAmount = 1f;
        [Range(0f, 3f)] public float chunkAmount = 1f;
        public int maxDustParticles = 4000;
        public int maxCloudParticles = 700;
        public int maxChunkParticles = 1500;
        [Tooltip("Hard cap of FX particles emitted per frame (protects against spikes in multi-building collapses).")]
        public int maxFxPerFrame = 600;
        public Vector2 cloudLifetime = new Vector2(6f, 10f);
        public Vector2 cloudSize = new Vector2(18f, 42f);
        [Tooltip("Debris hitting something faster than this emits impact dust.")]
        public float debrisDustSpeed = 7f;
        [Tooltip("Debris hitting something faster than this also sheds small chunks.")]
        public float debrisBreakSpeed = 13f;
        public int maxImpactFxPerFrame = 6;

        [Header("Performance")]
        [Tooltip("Block fractures executed per frame. Extra fractures wait a frame or two (spreads physics/GC spikes).")]
        public int maxFracturesPerFrame = 2;
        [Tooltip("Structural resolutions (level integrity + connectivity) per frame across all buildings.")]
        public int maxStructureResolvesPerFrame = 2;
        [Tooltip("FX within this camera distance are emitted at full amount.")]
        public float fxLodNear = 180f;
        [Tooltip("At this distance and beyond, FX counts are scaled by fxLodFarScale (clouds get bigger to keep coverage).")]
        public float fxLodFar = 600f;
        [Range(0.05f, 1f)] public float fxLodFarScale = 0.35f;
        [Tooltip("Cloud density cap: max overlapping live clouds per horizontal cell. Beyond that a cloud layer adds overdraw but no visible density.")]
        public int maxCloudsPerCell = 24;
        public float cloudCellSize = 40f;
        [Tooltip("Max on-screen size of one cloud particle (fraction of the viewport). Limits fill cost when the camera is inside the dust.")]
        public float cloudMaxScreenSize = 1f;
        [Tooltip("Logs block lifecycle (freeze / fracture, max impact) for tuning.")]
        public bool debugLogBlocks;

        [Header("FX materials")]
        public Material dustMaterial;
        public Material chipMaterial;
        public string debrisLayerName = "Debris";

        private class Fragment
        {
            public GameObject go;
            public Rigidbody rb;
            public float spawnTime;
        }

        private readonly List<DebrisBody> active = new List<DebrisBody>();
        private readonly Queue<GameObject> rubble = new Queue<GameObject>();
        private readonly List<Fragment> fragActive = new List<Fragment>();
        private readonly Queue<Fragment> fragSettled = new Queue<Fragment>();
        private readonly Stack<Fragment> fragFree = new Stack<Fragment>();
        private int fragTotal;
        private ParticleSystem dust, cloud, chunks, chips;
        private MaterialPropertyBlock mpb;
        private Transform root, fragRoot;
        private int debrisLayer;
        private int fxFrame = -1, fxThisFrame, impactFxThisFrame;
        private readonly Queue<CollapseBlock> fractureQueue = new Queue<CollapseBlock>();
        private readonly Dictionary<Vector2Int, Vector2> cloudCells = new Dictionary<Vector2Int, Vector2>(); // (load, time)
        private Transform camTransform;
        private int resolveFrame = -1, resolvesThisFrame;

        public int PendingFractures => fractureQueue.Count;
        public int CloudsCapped { get; private set; }

        /// <summary>Per-frame budget for structural resolution (DestructibleStructure asks before resolving).</summary>
        internal bool TryReserveResolve()
        {
            if (resolveFrame != Time.frameCount) { resolveFrame = Time.frameCount; resolvesThisFrame = 0; }
            if (resolvesThisFrame >= maxStructureResolvesPerFrame) return false;
            resolvesThisFrame++;
            return true;
        }

        internal void EnqueueFracture(CollapseBlock block) => fractureQueue.Enqueue(block);

        public int ActiveBodies => active.Count;
        public int PeakActiveBodies { get; private set; }
        public int RubbleCount => rubble.Count;
        public int PulverizedCount { get; private set; }
        public int ShreddedCount { get; private set; }
        public int ActiveBlocks { get; private set; }
        public int PeakActiveBlocks { get; private set; }
        public int FractureCount { get; private set; }
        public int BlocksCreated { get; private set; }
        public int BodiesSpawned { get; private set; }
        public int ImpactCount { get; private set; }
        public int ActiveFragments => fragActive.Count;
        public int DustParticles => (dust ? dust.particleCount : 0);
        public int CloudParticles => cloud ? cloud.particleCount : 0;
        public int ChunkParticles => (chunks ? chunks.particleCount : 0) + (chips ? chips.particleCount : 0);

        private Transform Root => root ? root : (root = new GameObject("DebrisRoot").transform);

        private void Awake()
        {
            if (instance && instance != this) { Destroy(gameObject); return; }
            instance = this;
            debrisLayer = LayerMask.NameToLayer(debrisLayerName);
            if (debrisLayer < 0) debrisLayer = 0;
            mpb = new MaterialPropertyBlock();
            CreateFx();
        }

        public void ResetStats()
        {
            PeakActiveBodies = active.Count;
            PeakActiveBlocks = ActiveBlocks;
        }

        // ================================================================== single pieces

        /// <summary>A single piece breaks off (already reported as lost by its structure).</summary>
        public void SpawnDebris(DestructiblePiece p, Vector3 velocity)
        {
            if (!p) return;
            Launch(p, velocity);
            var b = BoundsOf(p);
            EmitDust(b.center, b.extents.magnitude * 0.5f, 6);
            EmitChips(b.center, b.extents, 6);
        }

        /// <summary>The piece is crushed into dust (beam core, overkill, crushed level). Leaves a real hole.</summary>
        public void Pulverize(DestructiblePiece p)
        {
            p.SetState(PieceState.Pulverized);
            var b = BoundsOf(p);
            float volume = VolumeOf(p.transform);
            EmitDust(b.center, b.extents.magnitude * 0.7f, Mathf.Clamp((int)(volume / 5f), 8, 36));
            if (volume > 40f) EmitCloud(b.center, b.extents.magnitude * 0.5f, 1 + (int)(volume / 150f), 2.5f);
            EmitChips(b.center, b.extents, 12);
            EmitChunks(b, Vector3.zero, 4f, Mathf.Clamp((int)(volume / 25f), 2, 8));
            p.gameObject.SetActive(false); // removes renderer + collider => real hole
            PulverizedCount++;
        }

        /// <summary>Physics if the budget allows it, otherwise visual chunks (never just disappears).</summary>
        private void Launch(DestructiblePiece p, Vector3 velocity)
        {
            float volume = VolumeOf(p.transform);
            bool tight = active.Count >= maxActiveBodies * 0.7f;
            if (active.Count >= maxActiveBodies || (tight && volume < minPhysicalVolume))
            {
                Shred(p, velocity);
                return;
            }
            PrepareMoving(p);
            p.transform.SetParent(Root, true);
            AddBody(p, velocity);
        }

        /// <summary>Replaces a piece by concrete chunk particles + dust that keep its motion (no rigidbody).</summary>
        private void Shred(DestructiblePiece p, Vector3 velocity)
        {
            p.SetState(PieceState.Pulverized);
            var b = BoundsOf(p);
            float volume = VolumeOf(p.transform);
            EmitChunks(b, velocity, 3f, Mathf.Clamp(Mathf.RoundToInt(volume / 10f), 3, 14));
            EmitDust(b.center, b.extents.magnitude * 0.5f, Mathf.Clamp((int)(volume / 10f), 3, 12));
            EmitChips(b.center, b.extents, 6);
            p.gameObject.SetActive(false);
            ShreddedCount++;
        }

        private static bool HasLanded(DebrisBody b)
        {
            var pos = b.transform.position;
            if (b.SpawnPosition.y - pos.y > 1f) return true;
            float baseY = b.Piece && b.Piece.Structure ? b.Piece.Structure.transform.position.y : 0f;
            return pos.y - baseY < 3f;
        }

        /// <summary>
        /// Before a block falls, everything resting inside its volume must be able to move with it:
        /// frozen rubble (static) pieces become bodies again and sleeping bodies are woken.
        /// </summary>
        private void ReleaseSupportsInside(Bounds b, List<DestructiblePiece> blockPieces)
        {
            b.Expand(1f);
            int n = Physics.OverlapBoxNonAlloc(b.center, b.extents, ReleaseBuffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = ReleaseBuffer[i];
                var rb = c.attachedRigidbody;
                if (rb) { if (!rb.isKinematic) rb.WakeUp(); continue; }
                var p = c.GetComponent<DestructiblePiece>();
                if (!p || p.IsIntact || p.State != PieceState.Detached || blockPieces.Contains(p)) continue;
                if (p.GetComponent<DebrisBody>()) continue;
                AddBody(p, Vector3.zero); // was frozen as static rubble: dynamic again
            }
        }

        private static readonly Collider[] ReleaseBuffer = new Collider[256];

        private void AddBody(DestructiblePiece p, Vector3 velocity)
        {
            var rb = p.gameObject.GetComponent<Rigidbody>();
            if (!rb) rb = p.gameObject.AddComponent<Rigidbody>();
            rb.mass = MassOf(p.transform);
            rb.velocity = velocity;
            rb.angularVelocity = Random.insideUnitSphere * 1.2f;
            rb.maxDepenetrationVelocity = 3f;
            var body = p.gameObject.AddComponent<DebrisBody>();
            body.Init(this, p, rb, VolumeOf(p.transform));
            active.Add(body);
            BodiesSpawned++;
            if (active.Count > PeakActiveBodies) PeakActiveBodies = active.Count;
        }

        private void PrepareMoving(DestructiblePiece p)
        {
            if (p.preparedForMotion) return; // keep the ORIGINAL matrix (facade windows stay glued to the piece)
            p.preparedForMotion = true;
            var r = p.Renderer;
            if (r)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetMatrix("_OrigObjectToWorld", p.transform.localToWorldMatrix);
                mpb.SetFloat("_UseOrigSpace", 1f);
                r.SetPropertyBlock(mpb);
            }
            p.gameObject.layer = debrisLayer;
        }

        private float MassOf(Transform t) => Mathf.Clamp(VolumeOf(t) * density, 2f, 500f);

        private static float VolumeOf(Transform t)
        {
            var s = t.lossyScale;
            return Mathf.Abs(s.x * s.y * s.z);
        }

        private static Bounds BoundsOf(DestructiblePiece p)
        {
            var c = p.Collider;
            if (c && c.enabled && c.gameObject.activeInHierarchy) return c.bounds;
            return p.Renderer ? p.Renderer.bounds : new Bounds(p.transform.position, Vector3.one);
        }

        // ================================================================== collapse blocks

        /// <summary>
        /// A disconnected group falls. Large groups become several stacked blocks (split along their longest axis,
        /// i.e. by floors for an upright tower) that share one toppling direction, so they tilt and separate while falling.
        /// </summary>
        public void SpawnBlock(List<DestructiblePiece> pieces)
        {
            if (pieces == null || pieces.Count == 0) return;
            var all = BoundsOf(pieces);
            ReleaseSupportsInside(all, pieces);
            EmitCollapseCloud(new Vector3(all.center.x, all.min.y, all.center.z), all.extents);

            var parts = new List<List<DestructiblePiece>>();
            SplitInto(pieces, Mathf.Max(minBlockSize, maxInitialBlockPieces), parts);

            var h = Random.insideUnitCircle.normalized;
            var tilt = new Vector3(h.x, 0f, h.y) * (topplingSpin * Random.Range(0.6f, 1f));
            foreach (var part in parts)
            {
                if (part.Count >= minBlockSize && ActiveBlocks < maxActiveBlocks)
                    CreateBlock(part, Vector3.zero, tilt + Random.insideUnitSphere * 0.04f, 0);
                else
                    foreach (var p in part) Launch(p, Vector3.zero);
            }
        }

        private void CreateBlock(List<DestructiblePiece> pieces, Vector3 velocity, Vector3 angularVelocity, int generation)
        {
            var go = new GameObject($"CollapseBlock_g{generation}_{pieces.Count}");
            go.transform.SetParent(Root, false);
            Vector3 center = Vector3.zero;
            foreach (var p in pieces) center += p.transform.position;
            go.transform.position = center / pieces.Count;
            go.layer = debrisLayer;

            float mass = 0f;
            foreach (var p in pieces)
            {
                PrepareMoving(p);
                p.transform.SetParent(go.transform, true);
                mass += MassOf(p.transform);
            }

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.velocity = velocity;
            rb.angularVelocity = angularVelocity;
            rb.maxDepenetrationVelocity = 2f; // split chunks may overlap slightly at slab/column corners
            go.AddComponent<CollapseBlock>().Init(this, pieces, rb, generation);
            ActiveBlocks++;
            BlocksCreated++;
            if (ActiveBlocks > PeakActiveBlocks) PeakActiveBlocks = ActiveBlocks;
        }

        /// <summary>
        /// Hard impact: crush what was hit, break off the pieces around the impact point, split the intact rest
        /// into a few smaller blocks (next generation). After maxFractureGenerations the block falls apart completely.
        /// </summary>
        internal void FractureBlock(CollapseBlock block, List<DestructiblePiece> pieces, Rigidbody rb, int generation,
                                    Vector3 contact, float deltaV, List<Vector3> crushPoints, float relSpeed)
        {
            ActiveBlocks--;
            FractureCount++;
            if (crushPoints != null && crushPoints.Count > 0) BlockImpact(crushPoints, relSpeed, rb);

            var alive = new List<DestructiblePiece>(pieces.Count);
            foreach (var p in pieces) if (p && p.gameObject.activeSelf && p.transform.parent == block.transform) alive.Add(p);
            if (alive.Count == 0) return;

            float strength = Mathf.Clamp(deltaV / Mathf.Max(0.1f, blockShatterDeltaV), 1f, 2.5f);
            float r = impactBreakRadius * strength;
            bool final = generation >= maxFractureGenerations || alive.Count < minBlockSize * 2;

            var keep = new List<DestructiblePiece>();
            var broken = new List<DestructiblePiece>();
            foreach (var p in alive)
            {
                if (final || p.Collider.bounds.SqrDistance(contact) < r * r) broken.Add(p);
                else keep.Add(p);
            }
            if (keep.Count < minBlockSize) { broken.AddRange(keep); keep.Clear(); }

            // Capture velocities before anything is re-parented.
            var brokenVel = new Vector3[broken.Count];
            for (int i = 0; i < broken.Count; i++)
            {
                var pos = broken[i].transform.position;
                var outward = pos - contact;
                outward = outward.sqrMagnitude > 1e-3f ? outward.normalized : Random.onUnitSphere;
                brokenVel[i] = rb.GetPointVelocity(pos) * 0.5f + outward * Random.Range(2f, 6f) * strength + Vector3.up * Random.Range(0.5f, 3f);
            }

            var parts = new List<List<DestructiblePiece>>();
            if (keep.Count > 0)
                SplitInto(keep, Mathf.Max(minBlockSize, Mathf.CeilToInt(keep.Count / (float)Mathf.Max(1, fractureSplitCount))), parts);
            var partVel = new Vector3[parts.Count];
            for (int i = 0; i < parts.Count; i++) partVel[i] = rb.GetPointVelocity(BoundsOf(parts[i]).center);
            var spin = rb.angularVelocity;

            for (int i = 0; i < broken.Count; i++)
            {
                broken[i].transform.SetParent(Root, true);
                Launch(broken[i], brokenVel[i]);
            }
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Count >= minBlockSize && ActiveBlocks < maxActiveBlocks)
                {
                    CreateBlock(part, partVel[i], spin * 0.8f + Random.insideUnitSphere * topplingSpin, generation + 1);
                }
                else
                {
                    foreach (var p in part)
                    {
                        p.transform.SetParent(Root, true);
                        Launch(p, partVel[i] + Random.insideUnitSphere * 2f);
                    }
                }
            }

            float size = Mathf.Sqrt(alive.Count) * 3f;
            EmitCloud(contact, Mathf.Clamp(size, 6f, 30f), Mathf.Clamp(alive.Count / 6, 4, 18), 7f);
            EmitDust(contact, Mathf.Clamp(size * 0.5f, 3f, 15f), Mathf.Clamp(alive.Count / 2, 10, 50));
        }

        /// <summary>A block came to rest: it stays as one static rubble chunk (rigidbody removed).</summary>
        internal void FreezeBlock(CollapseBlock block, List<DestructiblePiece> pieces, Rigidbody rb)
        {
            ActiveBlocks--;
            if (rb) Destroy(rb);
            Destroy(block);
            foreach (var p in pieces)
                if (p && p.transform.parent == block.transform) rubble.Enqueue(p.gameObject);
            TrimRubble();
        }

        internal void DiscardBlock(CollapseBlock block)
        {
            ActiveBlocks--;
            block.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ grouping helpers

        private static Bounds BoundsOf(List<DestructiblePiece> pieces)
        {
            var b = new Bounds(pieces[0].transform.position, Vector3.zero);
            foreach (var p in pieces) if (p) b.Encapsulate(BoundsOf(p));
            return b;
        }

        /// <summary>Splits pieces into connected groups of at most maxSize, cutting along the longest axis at the median.</summary>
        private static void SplitInto(List<DestructiblePiece> pieces, int maxSize, List<List<DestructiblePiece>> output)
        {
            foreach (var comp in Components(pieces))
            {
                if (comp.Count <= maxSize) { output.Add(comp); continue; }

                var b = new Bounds(comp[0].transform.position, Vector3.zero);
                foreach (var p in comp) b.Encapsulate(p.transform.position);
                int axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.y >= b.size.z ? 1 : 2);
                comp.Sort((a, c) => a.transform.position[axis].CompareTo(c.transform.position[axis]));
                float cut = comp[comp.Count / 2].transform.position[axis];

                var lo = new List<DestructiblePiece>();
                var hi = new List<DestructiblePiece>();
                foreach (var p in comp) (p.transform.position[axis] < cut - 0.01f ? lo : hi).Add(p);
                if (lo.Count == 0 || hi.Count == 0) { output.Add(comp); continue; }
                SplitInto(lo, maxSize, output);
                SplitInto(hi, maxSize, output);
            }
        }

        private static List<List<DestructiblePiece>> Components(List<DestructiblePiece> pieces)
        {
            var set = new HashSet<DestructiblePiece>(pieces);
            var seen = new HashSet<DestructiblePiece>();
            var result = new List<List<DestructiblePiece>>();
            var queue = new Queue<DestructiblePiece>();
            foreach (var start in pieces)
            {
                if (!start || !seen.Add(start)) continue;
                var comp = new List<DestructiblePiece>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var q = queue.Dequeue();
                    comp.Add(q);
                    var all = q.Structure ? q.Structure.Pieces : null;
                    if (all == null) continue;
                    foreach (int nb in q.Neighbors)
                    {
                        if (nb < 0 || nb >= all.Count) continue;
                        var n = all[nb];
                        if (n && set.Contains(n) && seen.Add(n)) queue.Enqueue(n);
                    }
                }
                result.Add(comp);
            }
            return result;
        }

        // ================================================================== fragments (pooled)

        /// <summary>Beam hit: rigid fragments and chunks are blown out along the beam, plus dust at the exit.</summary>
        public void EmitBeamEjecta(Bounds b, Vector3 dir, float power)
        {
            dir.Normalize();
            var exit = b.center + dir * b.extents.magnitude * 0.4f;
            int n = Mathf.RoundToInt(fragmentsPerBeamPiece * chunkAmount);
            for (int i = 0; i < n; i++)
            {
                var pos = exit + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.6f);
                var vel = dir * Random.Range(10f, 26f) * Mathf.Clamp(power, 0.4f, 1f) + Random.insideUnitSphere * 5f + Vector3.up * Random.Range(1f, 5f);
                SpawnFragment(pos, vel);
            }
            EmitChunks(b, dir * 14f * Mathf.Clamp(power, 0.4f, 1f), 7f, 8);
            EmitDust(exit, b.extents.magnitude * 0.5f, 10);
            EmitCloud(exit, 4f, 2, 3f);
        }

        private void SpawnFragment(Vector3 pos, Vector3 velocity)
        {
            if (fragActive.Count >= maxFragmentBodies) return;
            Fragment f = null;
            if (fragFree.Count > 0) f = fragFree.Pop();
            else if (fragTotal < maxFragmentObjects) f = CreateFragment();
            else if (fragSettled.Count > 0) f = fragSettled.Dequeue(); // recycle the oldest settled one
            if (f == null) return;

            var t = f.go.transform;
            t.position = pos;
            t.rotation = Random.rotation;
            t.localScale = new Vector3(Random.Range(fragmentSize.x, fragmentSize.y), Random.Range(fragmentSize.x, fragmentSize.y) * 0.7f,
                                       Random.Range(fragmentSize.x, fragmentSize.y));
            f.go.SetActive(true);
            if (!f.rb) f.rb = f.go.GetComponent<Rigidbody>();
            if (!f.rb) f.rb = f.go.AddComponent<Rigidbody>();
            f.rb.mass = Mathf.Clamp(VolumeOf(t) * density, 0.5f, 20f);
            f.rb.velocity = velocity;
            f.rb.angularVelocity = Random.insideUnitSphere * 5f;
            f.rb.maxDepenetrationVelocity = 3f;
            f.spawnTime = Time.time;
            fragActive.Add(f);
        }

        private Fragment CreateFragment()
        {
            if (!fragRoot) fragRoot = new GameObject("Fragments").transform;
            var go = PrimitiveBuilder.Box("Fragment", fragRoot, Vector3.zero, Vector3.one, chipMaterial, true);
            go.layer = debrisLayer;
            go.SetActive(false);
            fragTotal++;
            return new Fragment { go = go };
        }

        // ================================================================== lifecycle

        private void Update()
        {
            for (int n = 0; n < maxFracturesPerFrame && fractureQueue.Count > 0; n++)
            {
                var block = fractureQueue.Dequeue();
                if (block) block.ExecuteFracture();
                else n--; // destroyed meanwhile: does not use the budget
            }

            float now = Time.time;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var b = active[i];
                if (!b) { active.RemoveAt(i); continue; }
                if (b.transform.position.y < -30f) { b.gameObject.SetActive(false); active.RemoveAt(i); continue; }

                float age = now - b.SpawnTime;
                bool settled = age > 1f && (b.Body.IsSleeping() || b.Body.velocity.sqrMagnitude < 0.1f);
                bool expired = (age > maxBodyLifetime && b.Body.velocity.sqrMagnitude < 1f) || age > maxBodyLifetime * 2f;
                // A piece still wedged where it broke off must NOT become a static collider: it would hold up the floors
                // above forever. It stays a (sleeping, nearly free) body until it has actually fallen or reached the ground.
                if ((settled || expired) && !HasLanded(b)) continue;
                if (settled || expired)
                {
                    Freeze(b);
                    active.RemoveAt(i);
                }
            }

            for (int i = fragActive.Count - 1; i >= 0; i--)
            {
                var f = fragActive[i];
                float age = now - f.spawnTime;
                if (!f.rb || f.go.transform.position.y < -30f)
                {
                    if (f.rb) Destroy(f.rb);
                    f.rb = null;
                    f.go.SetActive(false);
                    fragFree.Push(f);
                    fragActive.RemoveAt(i);
                    continue;
                }
                if ((age > 1f && (f.rb.IsSleeping() || f.rb.velocity.sqrMagnitude < 0.1f)) || age > maxBodyLifetime)
                {
                    Destroy(f.rb); // settled fragment = static collider only
                    f.rb = null;
                    fragSettled.Enqueue(f);
                    fragActive.RemoveAt(i);
                }
            }
        }

        /// <summary>Removes physics from settled debris; it stays as a static rubble collider.</summary>
        private void Freeze(DebrisBody b)
        {
            var go = b.gameObject;
            Destroy(b.Body);
            Destroy(b);
            rubble.Enqueue(go);
            TrimRubble();
        }

        private void TrimRubble()
        {
            while (rubble.Count > maxRubble)
            {
                var old = rubble.Dequeue();
                if (old) old.SetActive(false);
            }
        }

        // ================================================================== impact damage & impact FX

        internal void OnDebrisCollision(Collision c, DebrisBody body)
        {
            TryImpact(c, body.Body, body.SpawnTime, ref body.lastHit);

            float now = Time.time;
            float v = c.relativeVelocity.magnitude;
            if (v < debrisDustSpeed || now - body.lastFx < 0.5f || now - body.SpawnTime < 0.15f) return;
            BeginFxFrame();
            if (impactFxThisFrame >= maxImpactFxPerFrame) return;
            impactFxThisFrame++;
            body.lastFx = now;

            var point = c.contactCount > 0 ? c.GetContact(0).point : body.transform.position;
            float vol = body.Volume;
            EmitDust(point, Mathf.Clamp(Mathf.Sqrt(vol) * 0.35f, 1.5f, 6f), Mathf.Clamp((int)(vol / 12f), 3, 10));
            if (vol > 50f) EmitCloud(point, 3f, vol > 100f ? 2 : 1, 3f);
            if (v > debrisBreakSpeed)
            {
                var b = BoundsOf(body.Piece);
                EmitChunks(new Bounds(point, b.size * 0.5f), Vector3.up * 3f, 5f, Mathf.Clamp((int)(vol / 30f), 2, 5));
                EmitChips(point, b.extents * 0.5f, 8);
            }
        }

        internal bool TryImpact(Collision c, Rigidbody rb, float spawnTime, ref float lastHit)
        {
            float now = Time.time;
            if (now - spawnTime < 0.25f || now - lastHit < 0.5f) return false;
            float v = c.relativeVelocity.magnitude;
            if (v < impactMinSpeed) return false;
            var target = c.collider.GetComponentInParent<IDamageable>();
            if (target == null) return false;
            if (target is DestructiblePiece dp && !dp.IsIntact) return false;

            lastHit = now;
            var point = c.contactCount > 0 ? c.GetContact(0).point : c.collider.bounds.center;
            target.ApplyDamage(rb.mass * v * impactDamagePerMomentum, point);
            ImpactCount++;
            return true;
        }

        /// <summary>Crush around up to 4 well-separated contact points (pancake effect / chain damage to what was hit).</summary>
        internal void BlockImpact(List<Vector3> points, float relSpeed, Rigidbody rb)
        {
            float damage = Mathf.Clamp(rb.mass * relSpeed * blockCrushDamagePerMomentum, 200f, 3000f);
            foreach (var p in points) DestructionUtility.Explode(p, blockCrushRadius, damage, 4f);
            ImpactCount++;
        }

        /// <summary>Picks up to 4 contact points at least 8 m apart (copied out: Collision objects are reused by Unity).</summary>
        internal static void CollectCrushPoints(Collision c, List<Vector3> result)
        {
            result.Clear();
            int count = Mathf.Min(c.contactCount, 16);
            for (int i = 0; i < count && result.Count < 4; i++)
            {
                var p = c.GetContact(i).point;
                bool near = false;
                foreach (var u in result) if ((u - p).sqrMagnitude < 64f) { near = true; break; }
                if (!near) result.Add(p);
            }
        }

        // ================================================================== FX

        private void BeginFxFrame()
        {
            if (fxFrame == Time.frameCount) return;
            fxFrame = Time.frameCount;
            fxThisFrame = 0;
            impactFxThisFrame = 0;
        }

        /// <summary>Returns how many of the requested particles may still be emitted this frame.</summary>
        private int Take(int requested, float amount) => Take(requested, amount, 1f);

        private int Take(int requested, float amount, float lod)
        {
            BeginFxFrame();
            float exact = requested * amount * lod;
            int n = Mathf.FloorToInt(exact);
            if (Random.value < exact - n) n++; // stochastic rounding keeps small requests alive at distance
            n = Mathf.Min(n, maxFxPerFrame - fxThisFrame);
            if (n <= 0) return 0;
            fxThisFrame += n;
            return n;
        }

        private void CreateFx()
        {
            if (!dustMaterial) dustMaterial = CreateRuntimeDustMaterial();
            if (!chipMaterial) chipMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.44f, 0.42f) };

            // Small/medium puffs
            dust = CreateSystem("DustFX", dustMaterial, false);
            var main = dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(5f, 12f);
            main.startColor = new Color(0.62f, 0.58f, 0.52f, 0.55f);
            main.gravityModifier = -0.01f;
            main.maxParticles = maxDustParticles;
            SetFade(dust, 0.8f, 0.5f);
            SetGrowth(dust, 0.6f, 1.9f);
            SetDrag(dust, 0.08f);

            // Big rolling clouds (collapse / big impacts). Dense at first, then spread and thin out.
            cloud = CreateSystem("DustCloudFX", dustMaterial, false);
            var cm0 = cloud.main;
            cm0.startLifetime = new ParticleSystem.MinMaxCurve(cloudLifetime.x, cloudLifetime.y);
            cm0.startSize = new ParticleSystem.MinMaxCurve(cloudSize.x, cloudSize.y);
            cm0.startColor = new ParticleSystem.MinMaxGradient(new Color(0.58f, 0.54f, 0.48f, 0.75f), new Color(0.7f, 0.66f, 0.6f, 0.75f));
            cm0.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            cm0.gravityModifier = -0.004f;
            cm0.maxParticles = maxCloudParticles;
            var col = cloud.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.06f), new GradientAlphaKey(0.6f, 0.3f),
                              new GradientAlphaKey(0.22f, 0.65f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            SetGrowth(cloud, 0.45f, 1.7f);
            SetDrag(cloud, 0.12f);
            var rot = cloud.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var cr = cloud.GetComponent<ParticleSystemRenderer>();
            cr.sortMode = ParticleSystemSortMode.Distance;
            cr.maxParticleSize = cloudMaxScreenSize; // one cloud may still cover the whole screen (view blocking), never more

            // Concrete chunks (visual-only debris, mesh particles with cheap world collision)
            chunks = CreateSystem("ChunkFX", chipMaterial, true);
            var km = chunks.main;
            km.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            km.startSize3D = true;
            km.startSizeX = new ParticleSystem.MinMaxCurve(1f, 3.5f);
            km.startSizeY = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
            km.startSizeZ = new ParticleSystem.MinMaxCurve(1f, 3.5f);
            km.startRotation3D = true;
            km.gravityModifier = 1f;
            km.maxParticles = maxChunkParticles;
            var kc = chunks.collision;
            kc.enabled = true;
            kc.type = ParticleSystemCollisionType.World;
            kc.quality = ParticleSystemCollisionQuality.Low;
            kc.bounce = 0.15f;
            kc.dampen = 0.6f;
            kc.collidesWith = ~(1 << debrisLayer);
            var ksize = chunks.sizeOverLifetime; // shrink away at the very end instead of popping
            ksize.enabled = true;
            ksize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f)));
            var kr = chunks.GetComponent<ParticleSystemRenderer>();
            kr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            kr.receiveShadows = true;

            // Small chips
            chips = CreateSystem("ChipFX", chipMaterial, true);
            var pm = chips.main;
            pm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
            pm.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f);
            pm.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            pm.startRotation3D = true;
            pm.gravityModifier = 1f;
            pm.maxParticles = 2000;
            var pc = chips.collision;
            pc.enabled = true;
            pc.type = ParticleSystemCollisionType.World;
            pc.quality = ParticleSystemCollisionQuality.Low;
            pc.bounce = 0.2f;
            pc.dampen = 0.5f;
            pc.collidesWith = ~(1 << debrisLayer);
        }

        private static void SetFade(ParticleSystem ps, float a0, float aMid)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(aMid, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        private static void SetGrowth(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, from, 0f, 2f), new Keyframe(1f, to, 0.3f, 0f)));
        }

        private static void SetDrag(ParticleSystem ps, float dampen)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.6f;
            limit.dampen = dampen;
        }

        private ParticleSystem CreateSystem(string name, Material material, bool meshParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            if (meshParticles)
            {
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = PrimitiveBuilder.CubeMesh;
            }
            ps.Play();
            return ps;
        }

        public void EmitDust(Vector3 position, float spread, int count)
        {
            if (!dust) return;
            count = Take(count, dustAmount, Lod(position));
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int i = 0; i < count; i++)
            {
                ep.position = position + Random.insideUnitSphere * spread;
                dust.Emit(ep, 1);
            }
        }

        /// <summary>Large slow clouds rolling outward (mostly horizontally) from a point.</summary>
        public void EmitCloud(Vector3 position, float spread, int count, float outwardSpeed)
        {
            if (!cloud) return;
            // Distance LOD: fewer but bigger clouds far away (same silhouette, less overdraw).
            float lod = Lod(position);
            int wanted = count;
            count = Take(count, cloudAmount, lod);
            // Density cap: in an already saturated area extra layers are invisible but cost fill-rate.
            // Emit fewer and make them bigger instead.
            count = ReserveCloudCell(position, count, out float capScale);
            float sizeScale = Mathf.Clamp(1f / Mathf.Sqrt(Mathf.Max(lod, 0.05f)), 1f, 1.7f) * capScale;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.startSize = Random.Range(cloudSize.x, cloudSize.y) * sizeScale;
                ep.startLifetime = Random.Range(cloudLifetime.x, cloudLifetime.y);
                var d = Random.insideUnitCircle;
                var off = new Vector3(d.x, Random.Range(-0.2f, 0.4f), d.y) * spread;
                var dir = new Vector3(d.x, 0f, d.y);
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
                ep.position = position + off;
                ep.velocity = dir * outwardSpeed * Random.Range(0.4f, 1f) + Vector3.up * Random.Range(0.3f, 2f);
                cloud.Emit(ep, 1);
            }
        }

        /// <summary>1 near the camera, fxLodFarScale far away.</summary>
        private float Lod(Vector3 position)
        {
            if (!camTransform)
            {
                var cam = Camera.main;
                if (!cam) return 1f;
                camTransform = cam.transform;
            }
            float d = Vector3.Distance(camTransform.position, position);
            if (d <= fxLodNear) return 1f;
            return Mathf.Lerp(1f, fxLodFarScale, Mathf.InverseLerp(fxLodNear, fxLodFar, d));
        }

        /// <summary>
        /// Tracks an exponentially decaying "live clouds" estimate per horizontal cell. Returns how many of the
        /// requested clouds may be emitted and a size multiplier that compensates coverage for the skipped ones.
        /// </summary>
        private int ReserveCloudCell(Vector3 position, int count, out float sizeScale)
        {
            sizeScale = 1f;
            if (count <= 0 || maxCloudsPerCell <= 0) return count;
            var key = new Vector2Int(Mathf.FloorToInt(position.x / cloudCellSize), Mathf.FloorToInt(position.z / cloudCellSize));
            float now = Time.time, tau = Mathf.Max(1f, (cloudLifetime.x + cloudLifetime.y) * 0.5f);
            float load = 0f;
            if (cloudCells.TryGetValue(key, out var cell)) load = cell.x * Mathf.Exp(-(now - cell.y) / tau);
            int allowed = Mathf.Clamp(Mathf.CeilToInt(maxCloudsPerCell - load), 0, count);
            if (allowed < count)
            {
                CloudsCapped += count - allowed;
                // Keep ONE bigger cloud alive per request so a fresh impact is still visible inside an old cloud.
                if (allowed == 0) allowed = Random.value < 0.35f ? 1 : 0;
                sizeScale = 1.35f;
            }
            cloudCells[key] = new Vector2(load + allowed, now);
            return allowed;
        }

        /// <summary>The moment a building gives way: a dense ring of clouds around its footprint at the given height and on the ground.</summary>
        public void EmitCollapseCloud(Vector3 baseCenter, Vector3 halfExtents)
        {
            float perimeter = 4f * (halfExtents.x + halfExtents.z);
            int ring = Mathf.Clamp(Mathf.RoundToInt(perimeter / 7f), 8, 36);
            for (int i = 0; i < ring; i++)
            {
                float a = i / (float)ring * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var edge = new Vector3(dir.x * halfExtents.x, 0f, dir.z * halfExtents.z) * 1.05f;
                EmitCloud(baseCenter + edge, 4f, 1, 9f);
                float groundY = Mathf.Max(0f, baseCenter.y);
                if (baseCenter.y > 3f) EmitCloud(new Vector3(baseCenter.x + edge.x, 2f, baseCenter.z + edge.z), 4f, 1, 12f);
                else if (groundY < 3f) EmitCloud(baseCenter + edge * 1.2f + Vector3.up * 2f, 6f, 1, 13f);
            }
            EmitDust(baseCenter, Mathf.Max(halfExtents.x, halfExtents.z), Mathf.Clamp(ring * 2, 20, 70));
        }

        public void EmitChips(Vector3 position, Vector3 extents, int count)
        {
            if (!chips) return;
            count = Take(count, chunkAmount, Lod(position));
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int i = 0; i < count; i++)
            {
                ep.position = position + Vector3.Scale(Random.insideUnitSphere, extents);
                chips.Emit(ep, 1);
            }
        }

        /// <summary>Visual-only concrete chunks filling the given bounds, moving with the given velocity.</summary>
        public void EmitChunks(Bounds b, Vector3 velocity, float scatter, int count)
        {
            if (!chunks) return;
            count = Take(count, chunkAmount, Lod(b.center));
            var ep = new ParticleSystem.EmitParams();
            float maxDim = Mathf.Max(1.2f, Mathf.Min(4f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * 0.3f));
            for (int i = 0; i < count; i++)
            {
                ep.position = b.center + Vector3.Scale(Random.insideUnitSphere, b.extents);
                ep.velocity = velocity + Random.insideUnitSphere * scatter;
                ep.startSize3D = new Vector3(Random.Range(0.8f, maxDim), Random.Range(0.5f, maxDim * 0.6f), Random.Range(0.8f, maxDim));
                ep.rotation3D = Random.insideUnitSphere * 180f;
                ep.angularVelocity3D = Random.insideUnitSphere * 200f;
                chunks.Emit(ep, 1);
            }
        }

        private static Material CreateRuntimeDustMaterial()
        {
            var m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
            m.mainTexture = CreateSoftTexture(64);
            m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return m;
        }

        /// <summary>Soft round puff texture (also used by the editor builder to create a texture asset).</summary>
        public static Texture2D CreateSoftTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "DustSoft" };
            var rng = new System.Random(7);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (0.75f + 0.25f * (float)rng.NextDouble());
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return tex;
        }
    }
}
