using System.Collections.Generic;
using UnityEngine;

namespace CowMeat.City
{
    public enum VisualFitMode
    {
        Uniform, // keep proportions, fit inside the building envelope (recommended for real assets)
        Stretch, // non-uniform scale to exactly fill footprint x height
        None     // use the prefab at its native size
    }

    /// <summary>
    /// Describes how one external building asset is used as a Building's visual.
    /// Create via Assets > Create > COW_MEAT > Building Visual Profile.
    /// </summary>
    [CreateAssetMenu(menuName = "COW_MEAT/Building Visual Profile", fileName = "BVP_New")]
    public class BuildingVisualProfile : ScriptableObject
    {
        [Tooltip("Intact building model (prefab or FBX).")]
        public GameObject prefab;

        [Tooltip("Euler rotation applied to the model to fix its forward/up axis (e.g. (-90,0,0) for Z-up FBX).")]
        public Vector3 rotationOffset;

        public VisualFitMode fitMode = VisualFitMode.Uniform;

        [Tooltip("Future use: pre-fractured version. Children named 'Section_0', 'Section_1'... map to building sections.")]
        public GameObject fracturedPrefab;

        /// <summary>Bounds of the prefab (with rotationOffset applied) in its own root space. Works on uninstantiated prefab assets.</summary>
        public bool TryGetPrefabBounds(out Bounds bounds)
        {
            bounds = default;
            if (!prefab) return false;
            var root = prefab.transform;
            var rootToSpace = Matrix4x4.Rotate(Quaternion.Euler(rotationOffset)) *
                              Matrix4x4.TRS(Vector3.zero, root.localRotation, root.localScale);
            return VisualBounds.TryCompute(root, rootToSpace, out bounds);
        }
    }

    /// <summary>Mesh-based bounds in an arbitrary space (not world AABB, so rotation-safe and usable on prefab assets).</summary>
    public static class VisualBounds
    {
        /// <param name="rootToSpace">Matrix mapping root-local space into the target space.</param>
        public static bool TryCompute(Transform root, Matrix4x4 rootToSpace, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            var rootWorldToLocal = root.worldToLocalMatrix;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!TryGetMesh(r, out var mesh)) continue;
                var m = rootToSpace * rootWorldToLocal * r.transform.localToWorldMatrix;
                Encapsulate(ref bounds, ref any, mesh.bounds, m);
            }
            return any;
        }

        /// <summary>Bounds of a single renderer in the given space.</summary>
        public static bool TryCompute(Renderer r, Matrix4x4 worldToSpace, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            if (TryGetMesh(r, out var mesh))
                Encapsulate(ref bounds, ref any, mesh.bounds, worldToSpace * r.transform.localToWorldMatrix);
            return any;
        }

        private static bool TryGetMesh(Renderer r, out Mesh mesh)
        {
            mesh = null;
            if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (r.TryGetComponent<MeshFilter>(out var mf)) mesh = mf.sharedMesh;
            return mesh;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool any, Bounds local, Matrix4x4 m)
        {
            var c = local.center;
            var e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                var p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
    }
}
