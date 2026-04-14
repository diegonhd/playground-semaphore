using UnityEngine;
using System.Collections.Generic;

public class ChildBehaviour : MonoBehaviour
{
    [SerializeField] public NPCController npcPrefab;
    [SerializeField] private int instancesToSpawn = 1;
    [SerializeField] private Transform spawnParent;
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private float spawnZ = 1f;
    [SerializeField] private LayerMask spawnBlockingLayers;
    [SerializeField] private float spawnCheckRadius = 0.12f;
    [SerializeField] private float minSpawnSeparation = 0.6f;
    [SerializeField] private int maxSpawnAttemptsPerInstance = 30;
    [SerializeField] private bool allowRealtimeSpawn = true;
    [SerializeField] private KeyCode realtimeSpawnKey = KeyCode.N;
    [SerializeField] private int realtimeSpawnAmount = 1;

    private readonly SpawnArea areaA = new SpawnArea(1.16f, 2.5f, -3.19f, 1.4f);

    private void Start()
    {
        if (!spawnOnStart)
            return;

        SpawnInstances();
    }

    private void Update()
    {
        if (!allowRealtimeSpawn)
            return;

        if (Input.GetKeyDown(realtimeSpawnKey))
            SpawnRealtime();
    }

    public void SpawnInstances()
    {
        SpawnMany(instancesToSpawn);
    }

    public void SpawnRealtime()
    {
        SpawnMany(realtimeSpawnAmount);
    }

    private void SpawnMany(int count)
    {
        if (npcPrefab == null || count <= 0)
            return;

        List<Vector3> occupiedSpawns = CollectExistingNpcPositions();

        for (int i = 0; i < count; i++)
        {
            if (!TryGetSpawnPosition(occupiedSpawns, out Vector3 spawnPos))
            {
                Debug.LogWarning($"Falha ao encontrar spawn livre para NPC {i + 1}/{count}.");
                continue;
            }

            occupiedSpawns.Add(spawnPos);
            spawnPos.z = 1f;
            NPCController instance = Instantiate(npcPrefab, spawnPos, Quaternion.identity, spawnParent);
            instance.SetSpawnPosition(spawnPos);
        }
    }

    private List<Vector3> CollectExistingNpcPositions()
    {
        var positions = new List<Vector3>();
        NPCController[] existingNpcs = FindObjectsOfType<NPCController>();
        for (int i = 0; i < existingNpcs.Length; i++)
            positions.Add(existingNpcs[i].transform.position);
        return positions;
    }

    private bool TryGetSpawnPosition(List<Vector3> occupiedSpawns, out Vector3 spawnPos)
    {
        int attempts = Mathf.Max(1, maxSpawnAttemptsPerInstance);
        float separation = Mathf.Max(0.01f, minSpawnSeparation);
        for (int i = 0; i < attempts; i++)
        {
            Vector3 candidate = areaA.RandomPoint(spawnZ);
            candidate.z = 1f;
            if (Physics2D.OverlapCircle((Vector2)candidate, Mathf.Max(0.01f, spawnCheckRadius), spawnBlockingLayers) != null)
                continue;

            bool tooClose = false;
            for (int j = 0; j < occupiedSpawns.Count; j++)
            {
                if (Vector2.Distance(candidate, occupiedSpawns[j]) < separation)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            spawnPos = candidate;
            return true;
        }

        spawnPos = Vector3.zero;
        return false;
    }

    private struct SpawnArea
    {
        private readonly float minX;
        private readonly float maxX;
        private readonly float minY;
        private readonly float maxY;

        public SpawnArea(float x1, float x2, float y1, float y2)
        {
            minX = Mathf.Min(x1, x2);
            maxX = Mathf.Max(x1, x2);
            minY = Mathf.Min(y1, y2);
            maxY = Mathf.Max(y1, y2);
        }

        public Vector3 RandomPoint(float z)
        {
            return new Vector3(
                Random.Range(minX, maxX),
                Random.Range(minY, maxY),
                1f
            );
        }
    }
}
