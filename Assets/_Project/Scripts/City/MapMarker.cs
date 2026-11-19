using UnityEngine;

namespace CowMeat.City
{
    /// <summary>Gameplay marker (spawn points, combat zone). Drawn as a gizmo in the Scene view.</summary>
    public class MapMarker : MonoBehaviour
    {
        public enum MarkerType { PlayerSpawn, MonsterSpawn, CombatZone, CameraPoint }

        [SerializeField] private MarkerType type;
        [SerializeField] private float radius = 5f;

        public MarkerType Type => type;
        public float Radius => radius;

        public void Setup(MarkerType markerType, float markerRadius)
        {
            type = markerType;
            radius = markerRadius;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = type switch
            {
                MarkerType.PlayerSpawn => Color.cyan,
                MarkerType.MonsterSpawn => Color.red,
                MarkerType.CombatZone => new Color(1f, 0.6f, 0f),
                _ => Color.white
            };

            var p = transform.position + Vector3.up * 0.5f;
            const int segments = 48;
            var prev = p + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var next = p + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
            Gizmos.DrawLine(p, p + Vector3.up * 20f);
            Gizmos.DrawLine(p, p + transform.forward * radius);
        }
    }
}
