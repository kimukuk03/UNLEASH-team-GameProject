using UnityEngine;

namespace CowMeat.City
{
    /// <summary>Anything that can receive damage at a world point (buildings, destructible pieces, later robot/kaiju parts).</summary>
    public interface IDamageable
    {
        void ApplyDamage(float amount, Vector3 worldPoint);
    }
}
