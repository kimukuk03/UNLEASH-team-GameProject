using System;
using UnityEngine;

namespace CowMeat.City
{
    /// <summary>
    /// A destructible chunk of a building (e.g. a group of floors). Owns its collider and health,
    /// and references the visual parts that should disappear/collapse when it is destroyed.
    /// Collider/health never change when the visual is swapped; only the visual-part mapping does.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class BuildingSection : MonoBehaviour
    {
        [SerializeField] private Building owner;
        [SerializeField] private int index;
        [SerializeField] private float minHeight;
        [SerializeField] private float maxHeight;
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth = 100f;
        [Tooltip("Currently active visual parts (placeholder or external asset).")]
        [SerializeField] private GameObject[] visualParts = Array.Empty<GameObject>();
        [Tooltip("Placeholder parts built by the factory; restored when the external visual is removed.")]
        [SerializeField] private GameObject[] placeholderParts = Array.Empty<GameObject>();

        private Collider cachedCollider;

        public Building Owner => owner;
        public int Index => index;
        public float MinHeight => minHeight;
        public float MaxHeight => maxHeight;
        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public float HealthRatio => maxHealth > 0f ? currentHealth / maxHealth : 0f;
        public bool IsDestroyed => currentHealth <= 0f;
        public GameObject[] VisualParts => visualParts;
        public Collider Collider => cachedCollider ? cachedCollider : (cachedCollider = GetComponent<Collider>());

        public void Setup(Building building, int sectionIndex, float min, float max, float health, GameObject[] parts)
        {
            owner = building;
            index = sectionIndex;
            minHeight = min;
            maxHeight = max;
            maxHealth = Mathf.Max(1f, health);
            currentHealth = maxHealth;
            placeholderParts = parts ?? Array.Empty<GameObject>();
            visualParts = placeholderParts;
        }

        internal void Bind(Building building) => owner = building;

        /// <summary>Applies damage. Returns true only on the hit that destroys the section.</summary>
        public bool ApplyDamage(float amount)
        {
            if (IsDestroyed || amount <= 0f) return false;
            currentHealth = Mathf.Max(0f, currentHealth - amount);
            return currentHealth <= 0f;
        }

        public void ResetHealth() => currentHealth = maxHealth;

        /// <summary>Marks the section destroyed regardless of health (e.g. lost support from below).</summary>
        public void ForceDestroy() => currentHealth = 0f;

        public void SetVisualParts(GameObject[] parts) => visualParts = parts ?? Array.Empty<GameObject>();

        public void RestorePlaceholderParts() => visualParts = placeholderParts;

        /// <summary>Shows/hides this section's visual parts (simple placeholder for future collapse effects).</summary>
        public void SetVisualActive(bool active)
        {
            foreach (var p in visualParts) if (p) p.SetActive(active);
        }

        /// <summary>Section box in the owner building's local space (sections are axis-aligned in that space).</summary>
        public Bounds GetLocalBounds()
        {
            var space = owner ? owner.transform : transform.parent;
            var center = space ? space.InverseTransformPoint(transform.position) : transform.position;
            var size = Collider is BoxCollider box ? Vector3.Scale(box.size, transform.lossyScale) : Collider.bounds.size;
            if (space) size = Vector3.Scale(size, new Vector3(1f / space.lossyScale.x, 1f / space.lossyScale.y, 1f / space.lossyScale.z));
            return new Bounds(center, size);
        }
    }
}
