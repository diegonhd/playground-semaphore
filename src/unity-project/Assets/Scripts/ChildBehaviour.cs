// Script purpose:
// Cria crianças sob demanda (via botão/input), sem spawn inicial automático.
// Cada criação usa Tb/Td informados pelo usuário, respeita limite de 10 instâncias
// e escolhe um prefab dentre os configurados.
using System.Collections.Generic;
using System.Globalization;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-900)]
public class ChildBehaviour : MonoBehaviour
{
    [SerializeField] private NPCController npcPrefab;
    [SerializeField] private List<NPCController> npcPrefabs = new List<NPCController>();
    [SerializeField] private LayerMask spawnBlockingLayers;
    [SerializeField] private TMP_InputField tbInputField;
    [SerializeField] private TMP_InputField tdInputField;
    [SerializeField] private TMP_InputField childIdentityInputField;
    [SerializeField] private Button createChildButton;
    [SerializeField] private Toggle hasBallToggle;
    [SerializeField] private float defaultTb = 5f;
    [SerializeField] private float defaultTd = 5f;

    private const int maxThreadsN = 10;
    private const float spawnZ = 1f;
    private const float spawnCheckRadius = 0.2f;
    private const float minSpawnSeparation = 0.6f;
    private const int maxSpawnAttemptsPerInstance = 30;
    private const float spawnSlotMatchTolerance = 0.15f;

    private static readonly Vector3[] fixedSpawnPositions = new Vector3[]
    {
        new Vector3(6.75f, 5.2f, spawnZ),
        new Vector3(5.6f, 5.2f, spawnZ),
        new Vector3(4.45f, 5.2f, spawnZ),
        new Vector3(3.3f, 5.2f, spawnZ),
        new Vector3(2.15f, 5.2f, spawnZ),
        new Vector3(6.75f, -0.35f, spawnZ),
        new Vector3(5.6f, -0.35f, spawnZ),
        new Vector3(4.45f, -0.35f, spawnZ),
        new Vector3(3.3f, -0.35f, spawnZ),
        new Vector3(2.15f, -0.35f, spawnZ),
    };

    private static readonly HashSet<int> claimedSpawnSlots = new HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetClaimedSpawnSlots()
    {
        claimedSpawnSlots.Clear();
    }

    // Permite reset explícito dos slots de spawn durante a execução.
    public static void ResetRuntimeStateNow()
    {
        ResetClaimedSpawnSlots();
    }

    private void Awake()
    {
        if (createChildButton != null)
            createChildButton.onClick.AddListener(CreateChildFromInputFields);
    }

    private void OnDestroy()
    {
        if (createChildButton != null)
            createChildButton.onClick.RemoveListener(CreateChildFromInputFields);
    }

    private void OnValidate()
    {
        defaultTb = Mathf.Max(0f, defaultTb);
        defaultTd = Mathf.Max(0f, defaultTd);
    }

    // Método público para ser usado diretamente no OnClick do botão no Inspector.
    public void CreateChildFromInputFields()
    {
        if (!TryReadDuration(tbInputField, defaultTb, "Tb", out float tbValue))
            return;

        if (!TryReadDuration(tdInputField, defaultTd, "Td", out float tdValue))
            return;

        bool startsWithBall = hasBallToggle != null && hasBallToggle.isOn;
        SpawnSingle(tbValue, tdValue, startsWithBall);
    }

    private void SpawnSingle(float tbValue, float tdValue, bool startsWithBall)
    {
        if (!TryGetPrefabForSpawn(out NPCController prefabToSpawn))
        {
            Debug.LogWarning("Nenhum prefab de NPC foi configurado para criação.");
            return;
        }

        NPCController[] existingNpcs = FindObjectsOfType<NPCController>();
        ReserveClaimedSlotsFromExistingNpcs(existingNpcs);
        HashSet<int> usedThreadIds = EnsureThreadIdsForExistingNpcs(existingNpcs, maxThreadsN);
        List<Vector3> occupiedSpawns = CollectNpcPositions(existingNpcs);

        int availableByThreads = maxThreadsN - existingNpcs.Length;
        int availableByFixedSlots = fixedSpawnPositions.Length - claimedSpawnSlots.Count;
        int availableSlots = Mathf.Min(availableByThreads, availableByFixedSlots);
        if (availableSlots <= 0)
        {
            Debug.LogWarning($"Sem slots de spawn disponíveis. N={maxThreadsN}, slots fixos={fixedSpawnPositions.Length}.");
            return;
        }

        if (!TryGetSpawnPosition(occupiedSpawns, out int spawnSlotIndex, out Vector3 spawnPos))
        {
            Debug.LogWarning("Falha ao encontrar spawn livre para nova criança.");
            return;
        }

        if (!TryResolveChildIdentity(existingNpcs, usedThreadIds, out int threadId, out string customIdentifier))
        {
            return;
        }

        spawnPos.z = spawnZ;
        NPCController instance = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);
        instance.AssignThreadId(threadId);
        instance.AssignCustomIdentifier(customIdentifier);
        instance.AssignSpawnSlotIndex(spawnSlotIndex);
        instance.ConfigureSpawnHasBall(startsWithBall);
        instance.ConfigureSpawnTimings(tbValue, tdValue);
        instance.SetSpawnPosition(spawnPos);

        claimedSpawnSlots.Add(spawnSlotIndex);
    }

    private bool TryGetPrefabForSpawn(out NPCController prefabToSpawn)
    {
        prefabToSpawn = null;
        var availablePrefabs = new List<NPCController>();

        if (npcPrefabs != null)
        {
            for (int i = 0; i < npcPrefabs.Count; i++)
            {
                NPCController candidate = npcPrefabs[i];
                if (candidate == null || availablePrefabs.Contains(candidate))
                    continue;

                availablePrefabs.Add(candidate);
            }
        }

        if (npcPrefab != null && !availablePrefabs.Contains(npcPrefab))
            availablePrefabs.Add(npcPrefab);

        if (availablePrefabs.Count == 0)
            return false;

        prefabToSpawn = availablePrefabs[UnityEngine.Random.Range(0, availablePrefabs.Count)];
        return true;
    }

    private bool TryReadDuration(TMP_InputField inputField, float fallbackValue, string label, out float parsedValue)
    {
        parsedValue = fallbackValue;
        if (inputField == null)
            return true;

        string raw = inputField.text;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out float value) ||
            float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            parsedValue = Mathf.Max(0f, value);
            return true;
        }

        Debug.LogWarning($"{label} inválido: \"{raw}\". Use número (ex.: 2.5).");
        return false;
    }

    private static List<Vector3> CollectNpcPositions(NPCController[] npcs)
    {
        var positions = new List<Vector3>();
        for (int i = 0; i < npcs.Length; i++)
            positions.Add(npcs[i].transform.position);
        return positions;
    }

    private static HashSet<int> EnsureThreadIdsForExistingNpcs(NPCController[] existingNpcs, int maxThreads)
    {
        var usedThreadIds = new HashSet<int>();

        for (int i = 0; i < existingNpcs.Length; i++)
        {
            NPCController npc = existingNpcs[i];
            int currentId = npc.ThreadId;
            bool hasValidUnusedId = currentId >= 1 && currentId <= maxThreads && !usedThreadIds.Contains(currentId);
            if (hasValidUnusedId)
            {
                usedThreadIds.Add(currentId);
                continue;
            }

            if (!TryGetNextAvailableThreadId(usedThreadIds, maxThreads, out int reassignedId))
            {
                Debug.LogWarning($"Não foi possível atribuir ID de thread para {npc.name}.");
                continue;
            }

            npc.AssignThreadId(reassignedId);
            usedThreadIds.Add(reassignedId);
        }

        return usedThreadIds;
    }

    private static bool TryGetNextAvailableThreadId(HashSet<int> usedThreadIds, int maxThreads, out int nextId)
    {
        for (int id = 1; id <= maxThreads; id++)
        {
            if (usedThreadIds.Contains(id))
                continue;

            nextId = id;
            return true;
        }

        nextId = -1;
        return false;
    }

    // Permite informar no input um ID numérico (1..N) ou um username livre.
    // - ID numérico: usa exatamente esse ID se estiver disponível.
    // - Username: aloca próximo ID livre e usa o texto como identificador exibido.
    private bool TryResolveChildIdentity(
        NPCController[] existingNpcs,
        HashSet<int> usedThreadIds,
        out int threadId,
        out string customIdentifier
    )
    {
        customIdentifier = string.Empty;

        string rawIdentity = childIdentityInputField != null ? childIdentityInputField.text : string.Empty;
        string identity = string.IsNullOrWhiteSpace(rawIdentity) ? string.Empty : rawIdentity.Trim();
        if (string.IsNullOrEmpty(identity))
        {
            if (!TryGetNextAvailableThreadId(usedThreadIds, maxThreadsN, out threadId))
            {
                Debug.LogWarning("Não há IDs de thread disponíveis para novas crianças.");
                return false;
            }

            return true;
        }

        if (int.TryParse(identity, NumberStyles.Integer, CultureInfo.InvariantCulture, out int typedId) ||
            int.TryParse(identity, NumberStyles.Integer, CultureInfo.CurrentCulture, out typedId))
        {
            if (typedId < 1 || typedId > maxThreadsN)
            {
                Debug.LogWarning($"ID inválido: {typedId}. Use um número entre 1 e {maxThreadsN}.");
                threadId = -1;
                return false;
            }

            if (usedThreadIds.Contains(typedId))
            {
                Debug.LogWarning($"ID {typedId} já está em uso por outra criança.");
                threadId = -1;
                return false;
            }

            threadId = typedId;
            return true;
        }

        if (HasChildIdentifierConflict(existingNpcs, identity))
        {
            Debug.LogWarning($"Username \"{identity}\" já está em uso por outra criança.");
            threadId = -1;
            return false;
        }

        if (!TryGetNextAvailableThreadId(usedThreadIds, maxThreadsN, out threadId))
        {
            Debug.LogWarning("Não há IDs de thread disponíveis para novas crianças.");
            return false;
        }

        customIdentifier = identity;
        return true;
    }

    private static bool HasChildIdentifierConflict(NPCController[] existingNpcs, string identity)
    {
        for (int i = 0; i < existingNpcs.Length; i++)
        {
            if (string.Equals(existingNpcs[i].ChildIdentifier, identity, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void ReserveClaimedSlotsFromExistingNpcs(NPCController[] existingNpcs)
    {
        for (int i = 0; i < existingNpcs.Length; i++)
        {
            NPCController npc = existingNpcs[i];
            int slotIndex = npc.SpawnSlotIndex;
            if (slotIndex >= 0 && slotIndex < fixedSpawnPositions.Length)
            {
                claimedSpawnSlots.Add(slotIndex);
                continue;
            }

            int inferredSlotIndex = FindMatchingSpawnSlotIndex(npc.transform.position);
            if (inferredSlotIndex < 0)
                continue;

            claimedSpawnSlots.Add(inferredSlotIndex);
            npc.AssignSpawnSlotIndex(inferredSlotIndex);
        }
    }

    private static int FindMatchingSpawnSlotIndex(Vector3 position)
    {
        for (int i = 0; i < fixedSpawnPositions.Length; i++)
        {
            if (Vector2.Distance(position, fixedSpawnPositions[i]) <= spawnSlotMatchTolerance)
                return i;
        }

        return -1;
    }

    private bool TryGetSpawnPosition(List<Vector3> occupiedSpawns, out int spawnSlotIndex, out Vector3 spawnPos)
    {
        int attempts = Mathf.Min(maxSpawnAttemptsPerInstance, fixedSpawnPositions.Length);
        for (int i = 0; i < attempts; i++)
        {
            if (claimedSpawnSlots.Contains(i))
                continue;

            Vector3 candidate = fixedSpawnPositions[i];
            candidate.z = spawnZ;
            if (Physics2D.OverlapCircle((Vector2)candidate, spawnCheckRadius, spawnBlockingLayers) != null)
                continue;

            bool tooClose = false;
            for (int j = 0; j < occupiedSpawns.Count; j++)
            {
                if (Vector2.Distance(candidate, occupiedSpawns[j]) < minSpawnSeparation)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            spawnSlotIndex = i;
            spawnPos = candidate;
            return true;
        }

        spawnSlotIndex = -1;
        spawnPos = Vector3.zero;
        return false;
    }
}
