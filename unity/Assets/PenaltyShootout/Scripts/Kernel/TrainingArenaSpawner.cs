using System.Collections.Generic;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public sealed class TrainingArenaSpawner : MonoBehaviour
    {
        [SerializeField]
        private PenaltyAreaController arenaPrefab;

        [SerializeField]
        [Min(1)]
        private int arenaCount = 16;

        [SerializeField]
        [Min(20f)]
        private float spacing = 30f;

        [SerializeField]
        private ulong masterSeed = 20260723UL;

        private readonly List<PenaltyAreaController> arenas =
            new List<PenaltyAreaController>();

        public PenaltyAreaController ArenaPrefab
        {
            get => arenaPrefab;
            set => arenaPrefab = value;
        }

        public int ArenaCount
        {
            get => arenaCount;
            set => arenaCount = Mathf.Max(1, value);
        }

        public float Spacing
        {
            get => spacing;
            set => spacing = Mathf.Max(20f, value);
        }

        public IReadOnlyList<PenaltyAreaController> Arenas => arenas;

        public void Spawn()
        {
            Clear();
            if (arenaPrefab == null)
            {
                Debug.LogError("TrainingArenaSpawner requires an arena prefab.", this);
                return;
            }

            var columns = Mathf.CeilToInt(Mathf.Sqrt(arenaCount));
            for (var index = 0; index < arenaCount; index++)
            {
                var row = index / columns;
                var column = index % columns;
                var position = transform.position +
                    new Vector3(column * spacing, 0f, row * spacing);
                var arena = Instantiate(arenaPrefab, position, Quaternion.identity, transform);
                arena.name = $"TrainingArena_{index:000}";
                arena.ArenaId = index;
                arena.MasterSeed = masterSeed;
                arena.ShowDebugUi = index == 0;
                arenas.Add(arena);
            }
        }

        public void Clear()
        {
            for (var index = arenas.Count - 1; index >= 0; index--)
            {
                if (arenas[index] != null)
                {
                    Destroy(arenas[index].gameObject);
                }
            }

            arenas.Clear();
        }
    }
}
