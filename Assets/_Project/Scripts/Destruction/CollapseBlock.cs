using System.Collections.Generic;
using UnityEngine;

namespace CowMeat.Destruction
{
    /// <summary>
    /// A falling chunk of a building as ONE rigidbody (compound collider of its pieces).
    /// It fractures only on a HARD impact against something heavy (ground, a building, another block):
    /// the velocity change of that impact (impulse / mass) must exceed DebrisManager.blockShatterDeltaV.
    /// Light debris touching it does nothing. Fracturing breaks off the pieces near the impact and splits the
    /// rest into smaller blocks (see DebrisManager.FractureBlock). Fractures are queued and executed within a
    /// per-frame budget to spread spikes. A block at rest becomes static rubble.
    /// </summary>
    public class CollapseBlock : MonoBehaviour
    {
        private DebrisManager manager;
        private List<DestructiblePiece> pieces;
        private Rigidbody rb;
        private float spawnTime;
        private float slowSince = -1f;
        private bool done;

        // Impact data copied out of the (reused) Collision for a deferred fracture.
        private Vector3 hitPoint;
        private float hitDeltaV, hitRelSpeed;
        private readonly List<Vector3> crushPoints = new List<Vector3>(4);

        public int PieceCount => pieces?.Count ?? 0;
        public int Generation { get; private set; }

        internal void Init(DebrisManager owner, List<DestructiblePiece> blockPieces, Rigidbody body, int generation)
        {
            manager = owner;
            pieces = blockPieces;
            rb = body;
            Generation = generation;
            spawnTime = Time.time;
        }

        private void OnCollisionEnter(Collision c) => CheckImpact(c);

        // Stay is reported per collider pair (a block has dozens), so it returns immediately unless the block is
        // moving fast enough to be able to break. (The handler must exist from creation: Unity sets a body's
        // contact-report flags when it is created, so a Stay handler added later would never be called.)
        private void OnCollisionStay(Collision c)
        {
            if (armed) CheckImpact(c);
        }

        private bool armed = true;
        private float maxSeenDeltaV;
        private string maxSeenWith = "-";
        private float radius = -1f;

        private void UpdateArmed()
        {
            if (radius < 0f)
            {
                var b = new Bounds(transform.position, Vector3.zero);
                foreach (var col in GetComponentsInChildren<Collider>()) b.Encapsulate(col.bounds);
                radius = b.extents.magnitude;
            }
            // Fastest point of the block (linear + rotation at its extremity).
            float pointSpeed = rb.velocity.magnitude + rb.angularVelocity.magnitude * radius;
            armed = pointSpeed > manager.blockShatterDeltaV * 0.6f;
        }

        private void CheckImpact(Collision c)
        {
            if (done || !rb || Time.time - spawnTime < manager.blockGraceTime) return;
            var other = c.rigidbody;
            if (other && other.mass < rb.mass * 0.25f) return; // small debris never breaks a big block

            float deltaV = c.impulse.magnitude / rb.mass;
            if (deltaV > maxSeenDeltaV) { maxSeenDeltaV = deltaV; maxSeenWith = c.collider ? c.collider.name : "?"; }
            if (deltaV < manager.blockShatterDeltaV) return;

            Vector3 point = Vector3.zero;
            int n = Mathf.Min(c.contactCount, 8);
            for (int i = 0; i < n; i++) point += c.GetContact(i).point;
            hitPoint = n > 0 ? point / n : transform.position;
            hitDeltaV = deltaV;
            hitRelSpeed = c.relativeVelocity.magnitude;
            DebrisManager.CollectCrushPoints(c, crushPoints);

            done = true;
            manager.EnqueueFracture(this);
        }

        /// <summary>Called by DebrisManager within its per-frame fracture budget.</summary>
        internal void ExecuteFracture()
        {
            if (!rb) return;
            manager.FractureBlock(this, pieces, rb, Generation, hitPoint, hitDeltaV, crushPoints, hitRelSpeed);
            Destroy(gameObject);
        }

        private void Update()
        {
            if (done || !rb) return;
            float now = Time.time, age = now - spawnTime;
            if (transform.position.y < -60f)
            {
                done = true;
                manager.DiscardBlock(this);
                return;
            }

            UpdateArmed();
            bool slow = rb.velocity.sqrMagnitude < 0.3f && rb.angularVelocity.sqrMagnitude < 0.03f;
            if (!slow) slowSince = -1f;
            else if (slowSince < 0f) slowSince = now;

            if ((age > 1.5f && slowSince >= 0f && now - slowSince > 0.75f) || age > manager.blockMaxLifetime)
            {
                done = true;
                if (manager.debugLogBlocks)
                    Debug.Log($"[Block] freeze {name} age {age:0.00}s pos {transform.position:F1} maxDv {maxSeenDeltaV:0.0} (vs {maxSeenWith}) speed {rb.velocity.magnitude:0.0}");
                manager.FreezeBlock(this, pieces, rb); // stays as a big static rubble chunk
            }
        }
    }
}
