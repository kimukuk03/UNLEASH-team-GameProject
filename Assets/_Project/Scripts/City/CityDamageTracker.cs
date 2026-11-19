using System;
using System.Collections.Generic;
using UnityEngine;

namespace CowMeat.City
{
    /// <summary>Tracks overall city damage (game goal: minimize city damage). Lives on CityRoot.</summary>
    [DisallowMultipleComponent]
    public class CityDamageTracker : MonoBehaviour
    {
        [SerializeField] private List<Building> buildings = new List<Building>();
        [Tooltip("Sum of registered buildings' max health. Cached so removed/destroyed buildings still count as fully damaged.")]
        [SerializeField] private float totalMaxHealth;

        private readonly HashSet<Building> subscribed = new HashSet<Building>();

        public event Action<CityDamageTracker> Changed;

        public IReadOnlyList<Building> Buildings => buildings;
        public int BuildingCount => buildings.Count;
        public float TotalMaxHealth => totalMaxHealth;

        /// <summary>Remaining health. A missing (destroyed/removed) building counts as 0.</summary>
        public float TotalCurrentHealth
        {
            get { float t = 0f; foreach (var b in buildings) if (b) t += b.CurrentHealth; return t; }
        }

        /// <summary>0 = untouched city, 1 = everything destroyed.</summary>
        public float DamageRatio => totalMaxHealth > 0f ? Mathf.Clamp01(1f - TotalCurrentHealth / totalMaxHealth) : 0f;

        public int CollapsedCount
        {
            get { int n = 0; foreach (var b in buildings) if (!b || b.State == BuildingState.Collapsed) n++; return n; }
        }

        /// <summary>Re-collects all child buildings (edit time / regeneration).</summary>
        [ContextMenu("Collect Buildings")]
        public void Collect()
        {
            UnsubscribeAll();
            buildings.Clear();
            totalMaxHealth = 0f;
            foreach (var b in GetComponentsInChildren<Building>(true)) Register(b);
        }

        /// <summary>Adds a building (e.g. spawned at runtime). Safe to call twice.</summary>
        public void Register(Building b)
        {
            if (!b || buildings.Contains(b)) return;
            buildings.Add(b);
            totalMaxHealth += b.MaxHealth;
            if (Application.isPlaying) Subscribe(b);
        }

        public Building FindById(string id)
        {
            foreach (var b in buildings) if (b && b.BuildingId == id) return b;
            return null;
        }

        private void Awake()
        {
            if (buildings.Count == 0) Collect();
            foreach (var b in buildings) Subscribe(b);
        }

        private void OnDestroy() => UnsubscribeAll();

        private void Subscribe(Building b)
        {
            if (!b || !subscribed.Add(b)) return;
            b.Damaged += OnDamaged;
        }

        private void UnsubscribeAll()
        {
            foreach (var b in subscribed) if (b) b.Damaged -= OnDamaged;
            subscribed.Clear();
        }

        // Damaged fires on every hit, including the collapsing one, after state is updated.
        private void OnDamaged(Building b, float amount) => Changed?.Invoke(this);
    }
}
