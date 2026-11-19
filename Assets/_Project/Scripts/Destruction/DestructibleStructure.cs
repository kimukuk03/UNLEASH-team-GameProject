using System;
using System.Collections.Generic;
using CowMeat.City;
using UnityEngine;

namespace CowMeat.Destruction
{
    /// <summary>
    /// Piece-based destructible building with simple structural rules:
    ///  1) Level integrity — if the intact load-bearing ratio (walls + columns) of a level drops below
    ///     supportThreshold, that level is crushed (pulverized).
    ///  2) Connectivity — intact pieces no longer connected to the ground through intact neighbours fall:
    ///     big groups as one rigid CollapseBlock, small groups as individual debris.
    /// Every piece that leaves the structure reports its max health as damage to the Building (→ CityDamageTracker).
    /// </summary>
    [DisallowMultipleComponent]
    public class DestructibleStructure : MonoBehaviour
    {
        [SerializeField] private DestructiblePiece[] pieces = Array.Empty<DestructiblePiece>();
        [SerializeField] private int levelCount = 1;
        [Tooltip("A level collapses when the ratio of intact load-bearing pieces drops below this.")]
        [SerializeField, Range(0f, 1f)] private float supportThreshold = 0.45f;
        [Tooltip("Damage beyond health x this factor turns the piece into dust instead of a falling chunk.")]
        [SerializeField] private float pulverizeOverkill = 2.5f;
        [Tooltip("Unsupported groups with at least this many pieces fall as one rigid block.")]
        [SerializeField] private int minBlockSize = 6;
        [SerializeField] private Building building;

        private int[] bearingTotal;
        private bool dirty;
        private float damageScale = 1f;

        /// <summary>Total building damage reported by this structure (for duplicate-count validation).</summary>
        public float ReportedLoss { get; private set; }

        public event Action<DestructibleStructure> Changed;

        public IReadOnlyList<DestructiblePiece> Pieces => pieces;
        public Building Building => building;
        public int LevelCount => levelCount;

        public int CountState(PieceState s)
        {
            int n = 0;
            foreach (var p in pieces) if (p && p.State == s) n++;
            return n;
        }

        public void Setup(DestructiblePiece[] builtPieces, int levels, Building owner)
        {
            pieces = builtPieces ?? Array.Empty<DestructiblePiece>();
            levelCount = Mathf.Max(1, levels);
            building = owner;
        }

        private void Awake()
        {
            bearingTotal = new int[levelCount];
            foreach (var p in pieces)
                if (p && p.IsBearing && p.Level >= 0 && p.Level < levelCount) bearingTotal[p.Level]++;

            // Building HP may differ from the sum of piece HP (city buildings use a volume-based HP);
            // losses are scaled so destroying every piece removes exactly the building's max health.
            float total = 0f;
            foreach (var p in pieces) if (p) total += p.MaxHealth;
            damageScale = building && total > 0f ? building.MaxHealth / total : 1f;
        }

        /// <summary>Damages one piece. Returns true if the piece was destroyed (detached or pulverized).</summary>
        public bool DamagePiece(DestructiblePiece piece, float amount, Vector3 velocity, bool pulverize)
        {
            if (!Application.isPlaying || !piece || !piece.IsIntact || piece.Structure != this || amount <= 0f) return false;
            float remaining = piece.TakeDamage(amount);
            if (remaining > 0f) return false;

            bool dust = pulverize || -remaining >= piece.MaxHealth * (pulverizeOverkill - 1f);
            if (dust) Pulverize(piece);
            else Detach(piece, velocity);
            return true;
        }

        private void Detach(DestructiblePiece p, Vector3 velocity)
        {
            p.SetState(PieceState.Detached);
            ReportLoss(p);
            DebrisManager.Instance.SpawnDebris(p, velocity);
            dirty = true;
        }

        private void Pulverize(DestructiblePiece p)
        {
            p.SetState(PieceState.Pulverized);
            ReportLoss(p);
            DebrisManager.Instance.Pulverize(p);
            dirty = true;
        }

        private void ReportLoss(DestructiblePiece p)
        {
            float amount = p.MaxHealth * damageScale;
            ReportedLoss += amount;
            if (building) building.ApplyDamage(amount, (BuildingSection)null);
        }

        // Structural checks are batched once per frame, after all hits of that frame.
        private void LateUpdate()
        {
            if (!dirty) return;
            // Budgeted across all buildings: simultaneous collapses resolve on consecutive frames instead of one spike.
            if (!DebrisManager.Instance.TryReserveResolve()) return;
            dirty = false;
            Resolve();
            Changed?.Invoke(this);
        }

        private void Resolve()
        {
            CheckLevelIntegrity();
            CheckConnectivity();
        }

        private void CheckLevelIntegrity()
        {
            for (int level = 0; level < levelCount; level++)
            {
                if (bearingTotal[level] == 0) continue;
                int intactBearing = 0;
                bool anyAbove = false;
                foreach (var p in pieces)
                {
                    if (!p || !p.IsIntact) continue;
                    if (p.Level == level && p.IsBearing) intactBearing++;
                    else if (p.Level > level) anyAbove = true;
                }
                if (!anyAbove) return; // nothing left to hold up

                if ((float)intactBearing / bearingTotal[level] < supportThreshold)
                {
                    // The level gives way: its remaining pieces are crushed into dust.
                    bool hasBounds = false;
                    var lb = new Bounds();
                    foreach (var p in pieces)
                    {
                        if (!p || !p.IsIntact || p.Level != level) continue;
                        if (!hasBounds) { lb = p.Collider.bounds; hasBounds = true; }
                        else lb.Encapsulate(p.Collider.bounds);
                        Pulverize(p);
                    }
                    if (hasBounds) DebrisManager.Instance.EmitCollapseCloud(new Vector3(lb.center.x, lb.min.y, lb.center.z), lb.extents);
                    return;
                }
            }
        }

        private void CheckConnectivity()
        {
            int n = pieces.Length;
            var visited = new bool[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (pieces[i] && pieces[i].IsIntact && pieces[i].Grounded)
                {
                    visited[i] = true;
                    queue.Enqueue(i);
                }
            }
            Flood(queue, visited, null);

            for (int i = 0; i < n; i++)
            {
                if (visited[i] || !pieces[i] || !pieces[i].IsIntact) continue;
                var group = new List<DestructiblePiece>();
                visited[i] = true;
                queue.Enqueue(i);
                Flood(queue, visited, group);

                if (group.Count >= minBlockSize)
                {
                    foreach (var p in group)
                    {
                        p.SetState(PieceState.Detached);
                        ReportLoss(p);
                    }
                    DebrisManager.Instance.SpawnBlock(group);
                }
                else
                {
                    foreach (var p in group) Detach(p, Vector3.zero);
                }
            }
        }

        // ------------------------------------------------------------------ validation

        /// <summary>
        /// Expected loss = scaled max HP of every non-intact piece. It must equal ReportedLoss and the building's lost HP
        /// when nothing is counted twice.
        /// </summary>
        public string ValidateDamage(out bool ok)
        {
            float expected = 0f;
            foreach (var p in pieces) if (p && !p.IsIntact) expected += p.MaxHealth * damageScale;
            float buildingLoss = building ? building.MaxHealth - building.CurrentHealth : ReportedLoss;
            float tol = Mathf.Max(0.5f, expected * 0.001f);
            ok = Mathf.Abs(expected - ReportedLoss) <= tol && Mathf.Abs(buildingLoss - ReportedLoss) <= tol;
            return $"expected {expected:0} / reported {ReportedLoss:0} / building lost {buildingLoss:0}";
        }

        /// <summary>Counts visible pieces whose collider bounds differ from their renderer bounds (should be 0).</summary>
        public int CountColliderMismatches(float tolerance = 0.05f)
        {
            int n = 0;
            foreach (var p in pieces)
            {
                if (!p || !p.gameObject.activeInHierarchy || !p.Collider || !p.Renderer) continue;
                Bounds a = p.Collider.bounds, b = p.Renderer.bounds;
                if ((a.center - b.center).sqrMagnitude > tolerance * tolerance || (a.size - b.size).sqrMagnitude > tolerance * tolerance) n++;
            }
            return n;
        }

        private void Flood(Queue<int> queue, bool[] visited, List<DestructiblePiece> collect)
        {
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                collect?.Add(pieces[i]);
                foreach (int nb in pieces[i].Neighbors)
                {
                    if (nb < 0 || nb >= pieces.Length || visited[nb]) continue;
                    var p = pieces[nb];
                    if (!p || !p.IsIntact) continue;
                    visited[nb] = true;
                    queue.Enqueue(nb);
                }
            }
        }
    }
}
