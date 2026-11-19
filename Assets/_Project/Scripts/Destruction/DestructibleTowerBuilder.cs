using System;
using System.Collections.Generic;
using CowMeat.City;
using UnityEngine;

namespace CowMeat.Destruction
{
    /// <summary>
    /// Builds a piece-based tower: per level 4 corner columns, wall panels per face, and floor slab quadrants.
    /// Default: 30 x 30 x 100 m, 10 levels -> 10 * (4 + 12 + 4) + 4 roof = 204 pieces.
    /// </summary>
    public static class DestructibleTowerBuilder
    {
        [Serializable]
        public struct Settings
        {
            public float width;
            public float depth;
            public int levels;
            public float levelHeight;
            public int panelsPerFace;
            public float wallThickness;
            public float columnSize;
            public float slabThickness;
            public float wallHealth;
            public float columnHealth;
            public float slabHealth;

            public static Settings Default => new Settings
            {
                width = 30f, depth = 30f, levels = 10, levelHeight = 10f, panelsPerFace = 3,
                wallThickness = 0.8f, columnSize = 2f, slabThickness = 0.6f,
                wallHealth = 120f, columnHealth = 260f, slabHealth = 150f,
            };
        }

        private struct PieceDef
        {
            public string name;
            public Vector3 center;
            public Vector3 size;
            public int level;
            public PieceKind kind;
            public Material material;
        }

        public static DestructibleStructure Build(GameObject root, Settings s, Material wallMat, Material columnMat, Material slabMat, int layer)
        {
            var defs = new List<PieceDef>();
            float hw = s.width * 0.5f, hd = s.depth * 0.5f, H = s.levelHeight, t = s.wallThickness, col = s.columnSize;

            for (int L = 0; L < s.levels; L++)
            {
                float y = L * H + H * 0.5f;

                // Corner columns
                for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                    defs.Add(new PieceDef
                    {
                        name = $"L{L:00}_Column_{(cx < 0 ? "W" : "E")}{(cz < 0 ? "S" : "N")}",
                        center = new Vector3(cx * (hw - col * 0.5f), y, cz * (hd - col * 0.5f)),
                        size = new Vector3(col, H, col), level = L, kind = PieceKind.Column, material = columnMat,
                    });

                // Wall panels between the columns
                AddFace(defs, "N", L, y, H, t, s.panelsPerFace, -hw + col, hw - col, alongX: true, fixedCoord: hd - t * 0.5f, wallMat);
                AddFace(defs, "S", L, y, H, t, s.panelsPerFace, -hw + col, hw - col, alongX: true, fixedCoord: -hd + t * 0.5f, wallMat);
                AddFace(defs, "E", L, y, H, t, s.panelsPerFace, -hd + col, hd - col, alongX: false, fixedCoord: hw - t * 0.5f, wallMat);
                AddFace(defs, "W", L, y, H, t, s.panelsPerFace, -hd + col, hd - col, alongX: false, fixedCoord: -hw + t * 0.5f, wallMat);

                // Floor slab (2 x 2 quadrants) at the bottom of the level
                AddSlab(defs, $"L{L:00}_Slab", L, L * H + s.slabThickness * 0.5f, hw - t, hd - t, s.slabThickness, slabMat);
            }
            // Roof slab inside the top level
            AddSlab(defs, $"L{s.levels - 1:00}_Roof", s.levels - 1, s.levels * H - s.slabThickness * 0.5f, hw - t, hd - t, s.slabThickness, slabMat);

            var structure = root.GetComponent<DestructibleStructure>();
            if (!structure) structure = root.AddComponent<DestructibleStructure>();
            var piecesRoot = new GameObject("Pieces").transform;
            piecesRoot.SetParent(root.transform, false);

            var pieces = new DestructiblePiece[defs.Count];
            var bounds = new Bounds[defs.Count];
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                var go = PrimitiveBuilder.Box(d.name, piecesRoot, d.center, d.size, d.material, true);
                go.layer = layer;
                var piece = go.AddComponent<DestructiblePiece>();
                float hp = d.kind == PieceKind.Wall ? s.wallHealth : d.kind == PieceKind.Column ? s.columnHealth : s.slabHealth;
                bounds[i] = new Bounds(root.transform.TransformPoint(d.center), d.size);
                piece.Setup(structure, i, d.level, d.kind, hp, bounds[i].min.y - root.transform.position.y < 0.05f);
                pieces[i] = piece;
            }

            // Adjacency: touching/overlapping boxes (slightly inflated).
            for (int i = 0; i < defs.Count; i++)
            {
                var nb = new List<int>();
                var bi = bounds[i];
                bi.Expand(0.3f);
                for (int j = 0; j < defs.Count; j++)
                    if (i != j && bi.Intersects(bounds[j])) nb.Add(j);
                pieces[i].SetNeighbors(nb.ToArray());
            }

            structure.Setup(pieces, s.levels, root.GetComponent<Building>());
            return structure;
        }

        /// <summary>
        /// Creates a complete destructible city building (Building + DestructibleStructure on one root) from a spec.
        /// Footprint/height of the spec are snapped to the piece grid; the spec's maxHealth is kept, so the
        /// building weighs the same in the CityDamageTracker as a regular one (piece losses are scaled).
        /// </summary>
        public static DestructibleStructure CreateBuilding(BuildingSpec spec, Settings s, Transform parent,
                                                           Material wallMat, Material columnMat, Material slabMat, int layer)
        {
            var go = new GameObject(spec.id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = spec.position;
            go.transform.localRotation = Quaternion.Euler(0f, spec.yaw, 0f);
            go.layer = layer;
            var building = go.AddComponent<Building>();
            var structure = Build(go, s, wallMat, columnMat, slabMat, layer);

            spec.footprint = new Vector2(s.width, s.depth);
            spec.height = s.levels * s.levelHeight;
            spec.archetype = BuildingArchetype.Slab;
            var pieces = go.transform.Find("Pieces");
            building.Initialize(spec, pieces, pieces, pieces, new BuildingSection[0]);
            structure.Setup(new List<DestructiblePiece>(structure.Pieces).ToArray(), s.levels, building);
            return structure;
        }

        private static void AddFace(List<PieceDef> defs, string face, int L, float y, float H, float t, int count,
                                    float from, float to, bool alongX, float fixedCoord, Material mat)
        {
            float w = (to - from) / count;
            for (int i = 0; i < count; i++)
            {
                float c = from + w * (i + 0.5f);
                defs.Add(new PieceDef
                {
                    name = $"L{L:00}_Wall_{face}{i}",
                    center = alongX ? new Vector3(c, y, fixedCoord) : new Vector3(fixedCoord, y, c),
                    size = alongX ? new Vector3(w, H, t) : new Vector3(t, H, w),
                    level = L, kind = PieceKind.Wall, material = mat,
                });
            }
        }

        private static void AddSlab(List<PieceDef> defs, string name, int L, float y, float innerHalfX, float innerHalfZ, float thick, Material mat)
        {
            for (int qx = 0; qx < 2; qx++)
            for (int qz = 0; qz < 2; qz++)
                defs.Add(new PieceDef
                {
                    name = $"{name}_{qx}{qz}",
                    center = new Vector3((qx - 0.5f) * innerHalfX, y, (qz - 0.5f) * innerHalfZ),
                    size = new Vector3(innerHalfX, thick, innerHalfZ),
                    level = L, kind = PieceKind.Slab, material = mat,
                });
        }
    }
}
