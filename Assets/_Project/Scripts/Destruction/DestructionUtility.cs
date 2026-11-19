using System;
using System.Collections.Generic;
using CowMeat.City;
using UnityEngine;

namespace CowMeat.Destruction
{
    /// <summary>Weapon-agnostic damage queries usable by robot/kaiju attacks later.</summary>
    public static class DestructionUtility
    {
        private static readonly Collider[] OverlapBuffer = new Collider[512];
        private static readonly HashSet<IDamageable> DamagedSet = new HashSet<IDamageable>();
        private static readonly HashSet<Rigidbody> PushedSet = new HashSet<Rigidbody>();

        public struct BeamResult
        {
            public int piecesDestroyed;
            public int targetsHit;
            public Vector3 end;
            public bool stopped;
        }

        /// <summary>Radial damage with linear falloff. Returns number of destroyed pieces.</summary>
        public static int Explode(Vector3 center, float radius, float damage, float force, int mask = ~0)
        {
            int count = Physics.OverlapSphereNonAlloc(center, radius, OverlapBuffer, mask, QueryTriggerInteraction.Ignore);
            // Reused sets (no per-blast garbage; a collapse fires dozens of blasts in one frame).
            var damaged = DamagedSet;
            var pushed = PushedSet;
            damaged.Clear();
            pushed.Clear();
            int destroyed = 0;
            for (int i = 0; i < count; i++)
            {
                var c = OverlapBuffer[i];
                if (!c) continue;
                var closest = c.bounds.ClosestPoint(center);
                float f = Mathf.Max(0.2f, 1f - Vector3.Distance(center, closest) / radius);
                var rb = c.attachedRigidbody;
                // One push per body (a compound collapse block has many colliders); big blocks are not flung by blasts.
                if (rb && (!pushed.Add(rb) || rb.GetComponent<CollapseBlock>())) rb = null;

                var piece = c.GetComponent<DestructiblePiece>();
                if (piece)
                {
                    if (piece.IsIntact)
                    {
                        var dir = c.bounds.center - center;
                        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.up;
                        if (piece.Structure.DamagePiece(piece, damage * f, dir * force * f, false)) destroyed++;
                    }
                    else if (rb && !rb.isKinematic)
                    {
                        rb.AddExplosionForce(force, center, radius, 0.5f, ForceMode.VelocityChange);
                    }
                    continue;
                }

                var target = c.GetComponentInParent<IDamageable>();
                if (target != null && damaged.Add(target)) target.ApplyDamage(damage * f, closest);
                if (rb && !rb.isKinematic) rb.AddExplosionForce(force, center, radius, 0.5f, ForceMode.VelocityChange);
            }
            return destroyed;
        }

        /// <summary>
        /// Penetrating beam: pulverizes intact pieces along a thick ray (real hole), losing power per piece.
        /// Other IDamageables (e.g. Buildings) take damage once. Non-damageable geometry (ground) stops it.
        /// </summary>
        /// <param name="detachBelowPower">Pieces killed while the beam's remaining power is below this break off and fly out
        /// (exit side) instead of being pulverized. 0 = always pulverize.</param>
        public static BeamResult FireBeam(Vector3 origin, Vector3 direction, float length, float radius, float damage, float penetration,
                                          int mask = ~0, float detachBelowPower = 0.8f)
        {
            var result = new BeamResult { end = origin + direction.normalized * length };
            direction.Normalize();
            var hits = Physics.SphereCastAll(origin, radius, direction, length, mask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            float power = 1f;
            var damaged = new HashSet<IDamageable>();
            foreach (var h in hits)
            {
                var piece = h.collider.GetComponent<DestructiblePiece>();
                if (piece)
                {
                    if (!piece.IsIntact) continue; // loose debris does not block the beam
                    var pieceBounds = piece.Collider.bounds;
                    bool core = power >= detachBelowPower;
                    var kick = direction * UnityEngine.Random.Range(16f, 26f) + Vector3.up * UnityEngine.Random.Range(2f, 5f);
                    float dmg = damage * power;
                    // Exit-side pieces: just enough damage to break them (no overkill -> they detach and fly out, not dust).
                    if (!core && dmg >= piece.Health) dmg = piece.Health + 0.01f;
                    bool killed = piece.Structure.DamagePiece(piece, dmg, kick, core);
                    if (killed)
                    {
                        result.piecesDestroyed++;
                        DebrisManager.Instance.EmitBeamEjecta(pieceBounds, direction, power);
                    }
                    power -= (piece.Kind == PieceKind.Slab ? 0.3f : 1f) / Mathf.Max(0.1f, penetration) * (killed ? 1f : 3f);
                }
                else
                {
                    var target = h.collider.GetComponentInParent<IDamageable>();
                    if (target != null)
                    {
                        if (!damaged.Add(target)) continue;
                        target.ApplyDamage(damage * power, h.point);
                        result.targetsHit++;
                        power -= 0.5f;
                    }
                    else if (!h.collider.attachedRigidbody)
                    {
                        power = 0f; // static world geometry (ground, rubble) stops the beam
                    }
                }

                if (power <= 0f)
                {
                    result.end = h.distance > 0f ? origin + direction * h.distance : h.point;
                    result.stopped = true;
                    break;
                }
            }
            return result;
        }

        /// <summary>
        /// Blasts the ground-floor perimeter of a structure (test / scripted collapse): explosions every ~15 m
        /// along its outer walls at 5 m height. Returns destroyed piece count.
        /// </summary>
        public static int BlastBase(DestructibleStructure s, float damage = 900f, float radius = 9f)
        {
            if (!s) return 0;
            bool any = false;
            var b = new Bounds();
            foreach (var p in s.Pieces)
            {
                if (!p || p.Level != 0 || !p.Collider) continue;
                if (!any) { b = p.Collider.bounds; any = true; }
                else b.Encapsulate(p.Collider.bounds);
            }
            if (!any) return 0;

            float y = b.min.y + 5f;
            int nx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / 15f)), nz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / 15f));
            var points = new List<Vector3>();
            for (int i = 0; i <= nx; i++)
            {
                float x = Mathf.Lerp(b.min.x, b.max.x, i / (float)nx);
                points.Add(new Vector3(x, y, b.min.z));
                points.Add(new Vector3(x, y, b.max.z));
            }
            for (int k = 1; k < nz; k++)
            {
                float z = Mathf.Lerp(b.min.z, b.max.z, k / (float)nz);
                points.Add(new Vector3(b.min.x, y, z));
                points.Add(new Vector3(b.max.x, y, z));
            }
            int destroyed = 0;
            foreach (var pt in points) destroyed += Explode(pt, radius, damage, 8f);
            return destroyed;
        }

        /// <summary>Returns the first INTACT destructible piece on the path, or null if the path is open.</summary>
        public static DestructiblePiece FirstIntactPieceOnPath(Vector3 origin, Vector3 direction, float length, float radius, int mask = ~0)
        {
            var hits = Physics.SphereCastAll(origin, radius, direction.normalized, length, mask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var piece = h.collider.GetComponent<DestructiblePiece>();
                if (piece && piece.IsIntact) return piece;
            }
            return null;
        }
    }
}
