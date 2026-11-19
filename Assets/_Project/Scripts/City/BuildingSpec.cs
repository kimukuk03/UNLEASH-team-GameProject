using System;
using UnityEngine;

namespace CowMeat.City
{
    /// <summary>Silhouette type of a placeholder building.</summary>
    public enum BuildingArchetype
    {
        Slab,        // simple box tower
        Setback,     // stacked tiers shrinking upwards
        PodiumTower, // wide podium + offset tower
        Spire,       // tower + crown + spire
        Cluster      // tall main volume + lower side wing
    }

    /// <summary>Pure data describing one building. Generators produce specs, the factory turns them into GameObjects.</summary>
    [Serializable]
    public struct BuildingSpec
    {
        public string id;
        public Vector3 position;   // base center (world / parent space)
        [Tooltip("Root Y rotation. Keep multiples of 90 while using the placeholder facade shader (world-axis windows).")]
        public float yaw;
        public Vector2 footprint;  // x = width (local X), y = depth (local Z)
        public float height;
        public BuildingArchetype archetype;
        public int seed;
        public float maxHealth;
    }

    /// <summary>Materials used by the factory for one building.</summary>
    [Serializable]
    public class BuildingMaterialSet
    {
        public Material facade;
        public Material roofDetail;
        public Material beacon;
    }
}
