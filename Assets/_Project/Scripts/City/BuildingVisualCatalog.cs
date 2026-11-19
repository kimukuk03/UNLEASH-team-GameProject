using System;
using System.Collections.Generic;
using UnityEngine;

namespace CowMeat.City
{
    /// <summary>
    /// Building ID → visual profile assignments. Read by CityGenerator on every (re)generation,
    /// so assignments survive regeneration. Empty catalog = placeholder city.
    /// </summary>
    [CreateAssetMenu(menuName = "COW_MEAT/Building Visual Catalog", fileName = "BuildingVisualCatalog")]
    public class BuildingVisualCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string buildingId;   // e.g. "BLD_007"
            public BuildingVisualProfile profile;
        }

        public List<Entry> entries = new List<Entry>();

        public BuildingVisualProfile Find(string buildingId)
        {
            foreach (var e in entries)
                if (e != null && e.profile && e.buildingId == buildingId) return e.profile;
            return null;
        }
    }
}
