using System.Collections.Generic;
using UnityEngine;

namespace CowMeat.City
{
    /// <summary>
    /// Builds a placeholder building GameObject from a BuildingSpec.
    /// Usable from editor tools and at runtime (e.g. spawning or rebuilding buildings).
    /// </summary>
    public static class BuildingFactory
    {
        private struct Volume
        {
            public Vector3 center;
            public Vector3 size;
            public Volume(Vector3 c, Vector3 s) { center = c; size = s; }
            public float Bottom => center.y - size.y * 0.5f;
            public float Top => center.y + size.y * 0.5f;
        }

        public static Building Create(BuildingSpec spec, BuildingMaterialSet mats, Transform parent, int layer = 0)
        {
            var rng = new System.Random(spec.seed);

            var root = new GameObject($"{spec.id}_{spec.archetype}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = spec.position;
            root.transform.localRotation = Quaternion.Euler(0f, spec.yaw, 0f);
            var building = root.AddComponent<Building>();

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var placeholder = new GameObject("Placeholder").transform;
            placeholder.SetParent(visual, false);
            var collision = new GameObject("Collision").transform;
            collision.SetParent(root.transform, false);

            var volumes = Layout(spec, rng);

            float totalVolume = 0f;
            int topIndex = 0;
            for (int i = 0; i < volumes.Count; i++)
            {
                totalVolume += volumes[i].size.x * volumes[i].size.y * volumes[i].size.z;
                if (volumes[i].Top > volumes[topIndex].Top) topIndex = i;
            }

            var tiers = new GameObject[volumes.Count];
            for (int i = 0; i < volumes.Count; i++)
                tiers[i] = PrimitiveBuilder.Box($"Tier_{i}", placeholder, volumes[i].center, volumes[i].size, mats.facade);

            var roof = BuildRoof(spec, volumes[topIndex], placeholder, mats, rng);

            var sections = new BuildingSection[volumes.Count];
            for (int i = 0; i < volumes.Count; i++)
            {
                var v = volumes[i];
                var col = new GameObject($"Section_{i}");
                col.transform.SetParent(collision, false);
                col.transform.localPosition = v.center;
                col.AddComponent<BoxCollider>().size = v.size;

                var section = col.AddComponent<BuildingSection>();
                float share = totalVolume > 0f ? (v.size.x * v.size.y * v.size.z) / totalVolume : 1f / volumes.Count;
                var parts = i == topIndex ? new[] { tiers[i], roof } : new[] { tiers[i] };
                section.Setup(building, i, v.Bottom, v.Top, spec.maxHealth * share, parts);
                sections[i] = section;
            }

            building.Initialize(spec, visual, placeholder, collision, sections);
            PrimitiveBuilder.SetLayerRecursively(root, layer);
            return building;
        }

        private static List<Volume> Layout(BuildingSpec s, System.Random rng)
        {
            float fx = s.footprint.x, fz = s.footprint.y, h = s.height;
            var list = new List<Volume>();

            switch (s.archetype)
            {
                case BuildingArchetype.Setback:
                {
                    float[] hf = { 0.5f, 0.3f, 0.2f };
                    float[] sc = { 1f, 0.78f, 0.55f };
                    float y = 0f;
                    for (int i = 0; i < hf.Length; i++)
                    {
                        float th = h * hf[i];
                        list.Add(new Volume(new Vector3(0f, y + th * 0.5f, 0f), new Vector3(fx * sc[i], th, fz * sc[i])));
                        y += th;
                    }
                    break;
                }
                case BuildingArchetype.PodiumTower:
                {
                    float p = Mathf.Clamp(h * 0.15f, 10f, 18f);
                    list.Add(new Volume(new Vector3(0f, p * 0.5f, 0f), new Vector3(fx, p, fz)));
                    float k = Range(rng, 0.55f, 0.7f);
                    float tx = fx * k, tz = fz * k;
                    var offset = new Vector2((fx - tx) * 0.5f * Range(rng, -1f, 1f), (fz - tz) * 0.5f * Range(rng, -1f, 1f));
                    Stack(list, offset, tx, tz, p, h, 2);
                    break;
                }
                case BuildingArchetype.Spire:
                {
                    float bodyTop = h * 0.85f;
                    Stack(list, Vector2.zero, fx, fz, 0f, bodyTop, 2);
                    list.Add(new Volume(new Vector3(0f, bodyTop + (h - bodyTop) * 0.5f, 0f), new Vector3(fx * 0.62f, h - bodyTop, fz * 0.62f)));
                    break;
                }
                case BuildingArchetype.Cluster:
                {
                    bool alongX = fx >= fz;
                    float sign = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float length = alongX ? fx : fz;
                    float depth = alongX ? fz : fx;
                    float mainLen = length * 0.6f, sideLen = length - mainLen;
                    float mainC = sign * (-length * 0.5f + mainLen * 0.5f);
                    float sideC = sign * (length * 0.5f - sideLen * 0.5f);
                    float sideH = h * Range(rng, 0.4f, 0.6f);
                    float sideDepth = depth * 0.85f;

                    var sideCenter = alongX ? new Vector3(sideC, sideH * 0.5f, 0f) : new Vector3(0f, sideH * 0.5f, sideC);
                    var sideSize = alongX ? new Vector3(sideLen, sideH, sideDepth) : new Vector3(sideDepth, sideH, sideLen);
                    list.Add(new Volume(sideCenter, sideSize));

                    var mainOffset = alongX ? new Vector2(mainC, 0f) : new Vector2(0f, mainC);
                    float mx = alongX ? mainLen : depth, mz = alongX ? depth : mainLen;
                    Stack(list, mainOffset, mx, mz, 0f, h, 2);
                    break;
                }
                default: // Slab
                    Stack(list, Vector2.zero, fx, fz, 0f, h, h > 100f ? 3 : 2);
                    break;
            }
            return list;
        }

        private static void Stack(List<Volume> list, Vector2 offset, float sx, float sz, float y0, float y1, int count)
        {
            float step = (y1 - y0) / count;
            for (int i = 0; i < count; i++)
                list.Add(new Volume(new Vector3(offset.x, y0 + step * (i + 0.5f), offset.y), new Vector3(sx, step, sz)));
        }

        private static GameObject BuildRoof(BuildingSpec spec, Volume top, Transform visual, BuildingMaterialSet mats, System.Random rng)
        {
            var roof = new GameObject("Roof");
            roof.transform.SetParent(visual, false);
            float ty = top.Top;
            var c = top.center;

            // Mechanical penthouse
            if (top.size.x >= 16f && top.size.z >= 16f && rng.NextDouble() < 0.6)
            {
                var ps = new Vector3(top.size.x * Range(rng, 0.3f, 0.45f), Range(rng, 3f, 6f), top.size.z * Range(rng, 0.3f, 0.45f));
                PrimitiveBuilder.Box("Penthouse", roof.transform, new Vector3(c.x, ty + ps.y * 0.5f, c.z), ps, mats.roofDetail);
            }

            // AC units
            int ac = rng.Next(2, 5);
            for (int i = 0; i < ac; i++)
            {
                var s = new Vector3(Range(rng, 2f, 4f), Range(rng, 1.5f, 2.5f), Range(rng, 2f, 4f));
                var p = new Vector3(c.x + top.size.x * Range(rng, -0.35f, 0.35f), ty + s.y * 0.5f, c.z + top.size.z * Range(rng, -0.35f, 0.35f));
                PrimitiveBuilder.Box($"ACUnit_{i}", roof.transform, p, s, mats.roofDetail);
            }

            // Spire or antenna with warning beacon
            float mastHeight = 0f, mastRadius = 0f;
            Vector3 mastBase = new Vector3(c.x, ty, c.z);
            if (spec.archetype == BuildingArchetype.Spire)
            {
                mastHeight = spec.height * 0.18f;
                mastRadius = 0.8f;
            }
            else if (rng.NextDouble() < 0.55)
            {
                mastHeight = Range(rng, 8f, 16f);
                mastRadius = 0.25f;
                mastBase += new Vector3(top.size.x * Range(rng, -0.3f, 0.3f), 0f, top.size.z * Range(rng, -0.3f, 0.3f));
            }

            if (mastHeight > 0f)
            {
                PrimitiveBuilder.Cylinder("Mast", roof.transform, mastBase + Vector3.up * (mastHeight * 0.5f), mastRadius, mastHeight, mats.roofDetail);
                if (mats.beacon)
                    PrimitiveBuilder.Box("Beacon", roof.transform, mastBase + Vector3.up * (mastHeight + 0.4f), Vector3.one * 0.8f, mats.beacon);
            }
            return roof;
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
