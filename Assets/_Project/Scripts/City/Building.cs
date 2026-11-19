using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CowMeat.City
{
    public enum BuildingState { Intact, Damaged, Collapsed }

    /// <summary>
    /// Root component of a destructible building.
    /// Hierarchy:  Building (this)          — position + yaw (root transform), ID, health, state
    ///               ├ Visual
    ///               │   ├ Placeholder      — factory-built cubes (kept, hidden while an external visual is applied)
    ///               │   └ External         — optional external asset, scaled/fitted here (scale lives on this node)
    ///               │       └ Offset       — profile.rotationOffset (fixes asset forward/up axis)
    ///               │           └ <asset>
    ///               └ Collision/Section_i  — BoxCollider + BuildingSection (never changes with the visual)
    /// Find the building from a hit with collider.GetComponentInParent&lt;Building&gt;().
    /// </summary>
    [DisallowMultipleComponent]
    public class Building : MonoBehaviour, IDamageable
    {
        [Header("Identity")]
        [SerializeField] private string buildingId;
        [SerializeField] private BuildingArchetype archetype;
        [SerializeField] private Vector2 footprint;
        [SerializeField] private float height;

        [Header("Health")]
        [SerializeField] private float maxHealth = 1000f;
        [SerializeField] private float currentHealth = 1000f;
        [SerializeField, Range(0f, 1f)] private float damagedThreshold = 0.7f;
        [SerializeField] private BuildingState state = BuildingState.Intact;

        [Header("Destruction (placeholder behaviour)")]
        [Tooltip("Destroyed sections disable their collider and hide their visual, so they stop blocking beams/cover. " +
                 "Sections resting on a destroyed section collapse with it. Replace with real collapse FX later.")]
        [SerializeField] private bool removeDestroyedSections = true;

        [Header("Structure")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform placeholderVisual;
        [SerializeField] private Transform externalVisual;
        [SerializeField] private BuildingVisualProfile appliedProfile;
        [SerializeField] private Transform collisionRoot;
        [SerializeField] private BuildingSection[] sections = Array.Empty<BuildingSection>();

        /// <summary>(building, damage amount). Fired after health/state are updated.</summary>
        public event Action<Building, float> Damaged;
        public event Action<Building, BuildingSection> SectionDestroyed;
        public event Action<Building> Collapsed;

        public string BuildingId => buildingId;
        public BuildingArchetype Archetype => archetype;
        public Vector2 Footprint => footprint;
        public float Height => height;
        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public float HealthRatio => maxHealth > 0f ? currentHealth / maxHealth : 0f;
        public BuildingState State => state;
        public Transform VisualRoot => visualRoot;
        public Transform ActiveVisual => externalVisual ? externalVisual : placeholderVisual;
        public bool HasExternalVisual => externalVisual;
        public BuildingVisualProfile AppliedProfile => appliedProfile;
        public Transform CollisionRoot => collisionRoot;
        public BuildingSection[] Sections => sections;

        public void Initialize(BuildingSpec spec, Transform visual, Transform placeholder, Transform collision, BuildingSection[] builtSections)
        {
            buildingId = spec.id;
            archetype = spec.archetype;
            footprint = spec.footprint;
            height = spec.height;
            maxHealth = Mathf.Max(1f, spec.maxHealth);
            currentHealth = maxHealth;
            state = BuildingState.Intact;
            visualRoot = visual;
            placeholderVisual = placeholder;
            externalVisual = null;
            appliedProfile = null;
            collisionRoot = collision;
            sections = builtSections ?? Array.Empty<BuildingSection>();
            foreach (var s in sections) if (s) s.Bind(this);
        }

        private void Awake()
        {
            foreach (var s in sections) if (s) s.Bind(this);
        }

        // ------------------------------------------------------------------ damage

        public void ApplyDamage(float amount, Vector3 worldPoint) => ApplyDamage(amount, FindSectionAt(worldPoint));

        public void ApplyDamage(float amount, BuildingSection section = null)
        {
            if (state == BuildingState.Collapsed || amount <= 0f) return;

            var destroyedNow = new List<BuildingSection>();
            if (section && section.Owner == this && section.ApplyDamage(amount))
                DestroySectionAndSupported(section, destroyedNow);

            currentHealth = Mathf.Max(0f, currentHealth - amount);
            if (sections.Length > 0 && AllSectionsDestroyed()) currentHealth = 0f; // nothing left standing

            if (currentHealth <= 0f)
            {
                state = BuildingState.Collapsed;
                foreach (var s in sections)
                {
                    if (!s || s.IsDestroyed) continue;
                    s.ForceDestroy();
                    RemoveSection(s);
                    destroyedNow.Add(s);
                }
            }
            else if (HealthRatio <= damagedThreshold)
            {
                state = BuildingState.Damaged;
            }

            foreach (var s in destroyedNow) SectionDestroyed?.Invoke(this, s);
            Damaged?.Invoke(this, amount);
            if (state == BuildingState.Collapsed) Collapsed?.Invoke(this);
        }

        private void DestroySectionAndSupported(BuildingSection destroyed, List<BuildingSection> result)
        {
            RemoveSection(destroyed);
            result.Add(destroyed);

            var db = destroyed.GetLocalBounds();
            foreach (var other in sections)
            {
                if (!other || other.IsDestroyed) continue;
                var ob = other.GetLocalBounds();
                bool above = ob.min.y >= db.max.y - 0.5f;
                bool overlapsXZ = ob.min.x < db.max.x && ob.max.x > db.min.x && ob.min.z < db.max.z && ob.max.z > db.min.z;
                if (!above || !overlapsXZ) continue;
                other.ForceDestroy();
                DestroySectionAndSupported(other, result);
            }
        }

        private void RemoveSection(BuildingSection s)
        {
            if (!removeDestroyedSections) return;
            s.Collider.enabled = false;
            s.SetVisualActive(false);
        }

        private bool AllSectionsDestroyed()
        {
            foreach (var s in sections) if (s && !s.IsDestroyed) return false;
            return true;
        }

        /// <summary>Returns the intact section whose collider is closest to the given world point.</summary>
        public BuildingSection FindSectionAt(Vector3 worldPoint)
        {
            BuildingSection best = null;
            float bestDist = float.MaxValue;
            foreach (var s in sections)
            {
                if (!s || s.IsDestroyed) continue;
                float d = s.Collider.bounds.SqrDistance(worldPoint);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        [ContextMenu("Reset Health")]
        public void ResetHealth()
        {
            currentHealth = maxHealth;
            state = BuildingState.Intact;
            foreach (var s in sections)
            {
                if (!s) continue;
                s.ResetHealth();
                s.Collider.enabled = true;
                s.SetVisualActive(true);
            }
        }

        // ------------------------------------------------------------------ visuals

        /// <summary>
        /// Applies an external asset as the visual. ID, colliders, sections and health are untouched.
        /// The asset is fitted (per profile.fitMode) into the footprint x height envelope in building-local space,
        /// so it stays aligned with the colliders even when the building root is rotated.
        /// The placeholder is hidden, not destroyed (see RevertToPlaceholder).
        /// </summary>
        public GameObject ApplyVisual(BuildingVisualProfile profile)
        {
            if (!profile || !profile.prefab) return null;
            RemoveExternalVisual();

            var holder = new GameObject("External").transform;
            holder.SetParent(visualRoot ? visualRoot : transform, false);
            var offset = new GameObject("Offset").transform;
            offset.SetParent(holder, false);
            offset.localRotation = Quaternion.Euler(profile.rotationOffset);

            var instance = InstantiateAsset(profile.prefab, offset);
            instance.transform.localPosition = Vector3.zero; // keep the asset root's own rotation/scale

            StripPhysics(instance);
            PrimitiveBuilder.SetLayerRecursively(holder.gameObject, gameObject.layer);
            Fit(holder, profile.fitMode);

            if (placeholderVisual) placeholderVisual.gameObject.SetActive(false);
            externalVisual = holder;
            appliedProfile = profile;
            MapRenderersToSections(holder);
            return instance;
        }

        /// <summary>Removes the external visual and re-enables the placeholder.</summary>
        [ContextMenu("Revert To Placeholder Visual")]
        public void RevertToPlaceholder()
        {
            RemoveExternalVisual();
            if (placeholderVisual) placeholderVisual.gameObject.SetActive(true);
            foreach (var s in sections) if (s) s.RestorePlaceholderParts();
        }

        /// <summary>Kept for compatibility. Prefer ApplyVisual(profile).</summary>
        public GameObject ReplaceVisual(GameObject visualPrefab, bool fitToBounds = true)
        {
            if (!visualPrefab) return null;
            var temp = ScriptableObject.CreateInstance<BuildingVisualProfile>();
            temp.prefab = visualPrefab;
            temp.fitMode = fitToBounds ? VisualFitMode.Stretch : VisualFitMode.None;
            var result = ApplyVisual(temp);
            appliedProfile = null; // temp profile is not an asset
            return result;
        }

        private void RemoveExternalVisual()
        {
            if (!externalVisual) { appliedProfile = null; return; }
            var go = externalVisual.gameObject;
            externalVisual = null;
            appliedProfile = null;
            if (Application.isPlaying)
            {
                go.SetActive(false);
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        private static GameObject InstantiateAsset(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(prefab))
                return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
#endif
            return Instantiate(prefab, parent);
        }

        private static void StripPhysics(GameObject go)
        {
            // Hits must land on Section colliders only.
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        }

        /// <summary>Scales/moves the holder so the asset's bounds (in building-local space) fit the envelope, bottom-centered at the origin.</summary>
        private void Fit(Transform holder, VisualFitMode mode)
        {
            holder.localScale = Vector3.one;
            holder.localPosition = Vector3.zero;
            var worldToLocal = transform.worldToLocalMatrix;
            if (!VisualBounds.TryCompute(holder, worldToLocal * holder.localToWorldMatrix, out var b)) return;

            // holder is identity relative to the building here, so b is in building-local space.
            var target = new Vector3(footprint.x, height, footprint.y);
            Vector3 scale = Vector3.one;
            if (b.size.x > 1e-4f && b.size.y > 1e-4f && b.size.z > 1e-4f)
            {
                var ratio = new Vector3(target.x / b.size.x, target.y / b.size.y, target.z / b.size.z);
                if (mode == VisualFitMode.Stretch) scale = ratio;
                else if (mode == VisualFitMode.Uniform) scale = Vector3.one * Mathf.Min(ratio.x, Mathf.Min(ratio.y, ratio.z));
            }

            holder.localScale = scale;
            holder.localPosition = -Vector3.Scale(scale, new Vector3(b.center.x, b.min.y, b.center.z));
        }

        /// <summary>
        /// Assigns external renderers to sections:
        /// 1) a parent object named "Section_i" maps to section i (for pre-split / fractured assets);
        /// 2) otherwise every section the renderer substantially overlaps (a single-mesh tower maps to all sections);
        /// 3) otherwise the nearest section.
        /// </summary>
        private void MapRenderersToSections(Transform holder)
        {
            var parts = new List<GameObject>[sections.Length];
            for (int i = 0; i < parts.Length; i++) parts[i] = new List<GameObject>();
            var worldToLocal = transform.worldToLocalMatrix;

            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
            {
                int named = FindNamedSectionIndex(r.transform, holder);
                if (named >= 0 && named < sections.Length) { parts[named].Add(r.gameObject); continue; }
                if (!VisualBounds.TryCompute(r, worldToLocal, out var rb)) continue;

                bool assigned = false;
                int nearest = -1;
                float nearestDist = float.MaxValue;
                for (int i = 0; i < sections.Length; i++)
                {
                    if (!sections[i]) continue;
                    var sb = sections[i].GetLocalBounds();
                    float sectionVolume = Mathf.Max(1e-4f, sb.size.x * sb.size.y * sb.size.z);
                    if (OverlapVolume(rb, sb) >= 0.25f * sectionVolume || sb.Contains(rb.center))
                    {
                        parts[i].Add(r.gameObject);
                        assigned = true;
                    }
                    float d = sb.SqrDistance(rb.center);
                    if (d < nearestDist) { nearestDist = d; nearest = i; }
                }
                if (!assigned && nearest >= 0) parts[nearest].Add(r.gameObject);
            }

            for (int i = 0; i < sections.Length; i++)
                if (sections[i]) sections[i].SetVisualParts(parts[i].ToArray());
        }

        private static int FindNamedSectionIndex(Transform t, Transform stop)
        {
            for (; t && t != stop; t = t.parent)
                if (t.name.StartsWith("Section_") && int.TryParse(t.name.Substring(8), out int idx)) return idx;
            return -1;
        }

        private static float OverlapVolume(Bounds a, Bounds b)
        {
            var min = Vector3.Max(a.min, b.min);
            var max = Vector3.Min(a.max, b.max);
            var d = max - min;
            return d.x > 0f && d.y > 0f && d.z > 0f ? d.x * d.y * d.z : 0f;
        }
    }
}
