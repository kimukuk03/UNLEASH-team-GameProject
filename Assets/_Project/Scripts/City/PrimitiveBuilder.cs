using UnityEngine;

namespace CowMeat.City
{
    /// <summary>Creates renderer-only primitives that share Unity's built-in meshes (no collider unless requested).</summary>
    public static class PrimitiveBuilder
    {
        private static Mesh cubeMesh;
        private static Mesh cylinderMesh;

        public static Mesh CubeMesh => cubeMesh ? cubeMesh : (cubeMesh = Grab(PrimitiveType.Cube));
        public static Mesh CylinderMesh => cylinderMesh ? cylinderMesh : (cylinderMesh = Grab(PrimitiveType.Cylinder));

        private static Mesh Grab(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }

        public static GameObject Box(string name, Transform parent, Vector3 localCenter, Vector3 size, Material material, bool withCollider = false)
        {
            var go = Create(name, parent, localCenter, size, CubeMesh, material);
            if (withCollider) go.AddComponent<BoxCollider>();
            return go;
        }

        /// <summary>Cylinder centered at localCenter with total height and radius.</summary>
        public static GameObject Cylinder(string name, Transform parent, Vector3 localCenter, float radius, float height, Material material)
        {
            return Create(name, parent, localCenter, new Vector3(radius * 2f, height * 0.5f, radius * 2f), CylinderMesh, material);
        }

        private static GameObject Create(string name, Transform parent, Vector3 localPos, Vector3 scale, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
