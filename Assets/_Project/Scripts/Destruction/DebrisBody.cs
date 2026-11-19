using UnityEngine;

namespace CowMeat.Destruction
{
    /// <summary>A single falling piece. Transfers impact damage to whatever IDamageable it hits and emits impact dust.</summary>
    public class DebrisBody : MonoBehaviour
    {
        private DebrisManager manager;
        internal float lastHit = -10f;
        internal float lastFx = -10f;

        public DestructiblePiece Piece { get; private set; }
        public Rigidbody Body { get; private set; }
        public float SpawnTime { get; private set; }
        public float Volume { get; private set; }
        public Vector3 SpawnPosition { get; private set; }

        internal void Init(DebrisManager owner, DestructiblePiece piece, Rigidbody rb, float volume)
        {
            manager = owner;
            Piece = piece;
            Body = rb;
            Volume = volume;
            SpawnTime = Time.time;
            SpawnPosition = transform.position;
        }

        private void OnCollisionEnter(Collision c)
        {
            if (manager && Body) manager.OnDebrisCollision(c, this);
        }
    }
}
