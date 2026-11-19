using System;
using CowMeat.City;
using UnityEngine;

namespace CowMeat.Destruction
{
    public enum PieceKind { Wall, Column, Slab }
    public enum PieceState { Intact, Detached, Pulverized }

    /// <summary>
    /// One chunk of a destructible structure (wall panel, column, floor slab).
    /// Intact pieces are static colliders (no Rigidbody); physics is added only when the piece breaks off.
    /// </summary>
    [DisallowMultipleComponent]
    public class DestructiblePiece : MonoBehaviour, IDamageable
    {
        [SerializeField] private DestructibleStructure structure;
        [SerializeField] private int index;
        [SerializeField] private int level;
        [SerializeField] private PieceKind kind;
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float health = 100f;
        [SerializeField] private bool grounded;
        [SerializeField] private int[] neighbors = Array.Empty<int>();
        [SerializeField] private PieceState state = PieceState.Intact;

        /// <summary>Set once by DebrisManager when the piece first starts moving (keeps its original facade mapping).</summary>
        [System.NonSerialized] internal bool preparedForMotion;

        private Collider cachedCollider;
        private Renderer cachedRenderer;

        public DestructibleStructure Structure => structure;
        public int Index => index;
        public int Level => level;
        public PieceKind Kind => kind;
        public float MaxHealth => maxHealth;
        public float Health => health;
        public bool Grounded => grounded;
        public int[] Neighbors => neighbors;
        public PieceState State => state;
        public bool IsIntact => state == PieceState.Intact;
        /// <summary>Walls and columns carry the floors above; slabs do not.</summary>
        public bool IsBearing => kind != PieceKind.Slab;
        public Collider Collider => cachedCollider ? cachedCollider : (cachedCollider = GetComponent<Collider>());
        public Renderer Renderer => cachedRenderer ? cachedRenderer : (cachedRenderer = GetComponent<Renderer>());

        public void Setup(DestructibleStructure owner, int pieceIndex, int pieceLevel, PieceKind pieceKind, float hp, bool isGrounded)
        {
            structure = owner;
            index = pieceIndex;
            level = pieceLevel;
            kind = pieceKind;
            maxHealth = Mathf.Max(1f, hp);
            health = maxHealth;
            grounded = isGrounded;
            state = PieceState.Intact;
        }

        public void SetNeighbors(int[] indices) => neighbors = indices ?? Array.Empty<int>();

        internal float TakeDamage(float amount)
        {
            health -= amount;
            return health;
        }

        internal void SetState(PieceState newState) => state = newState;

        /// <summary>IDamageable: e.g. falling debris hitting this piece.</summary>
        public void ApplyDamage(float amount, Vector3 worldPoint)
        {
            if (structure && IsIntact) structure.DamagePiece(this, amount, Vector3.zero, false);
        }
    }
}
