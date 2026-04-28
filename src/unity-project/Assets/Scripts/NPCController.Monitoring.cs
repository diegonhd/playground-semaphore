// Superfície de monitoramento das crianças: IDs, snapshot para HUD e emissão de eventos/logs.
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class NPCController
{
    [SerializeField, HideInInspector] private int threadId;
    [SerializeField, HideInInspector] private int spawnSlotIndex = -1;
    [SerializeField, HideInInspector] private string customChildIdentifier = string.Empty;

    private bool hasPublishedInitialChildLog;
    private ChildThreadStatus lastPublishedThreadStatus;
    private static readonly object activeChildrenLock = new object();
    private static readonly List<NPCController> activeChildren = new List<NPCController>();

    // Event C#: desacopla emissão de log do consumo (HUD/console).
    public static event Action<string> ChildEventLogged;

    public int ThreadId => threadId;
    public int SpawnSlotIndex => spawnSlotIndex;
    public string ChildIdentifier =>
        !string.IsNullOrWhiteSpace(customChildIdentifier)
            ? customChildIdentifier
            : (threadId > 0 ? $"C{threadId:00}" : name);
    public float PlayTimeTb => tb;
    public float RestTimeTd => td;
    public int CurrentPlayTimeRemainingSeconds => threadCore != null ? threadCore.CurrentPlaySecondsRemaining : -1;
    public int CurrentRestTimeRemainingSeconds => threadCore != null ? threadCore.CurrentRestSecondsRemaining : -1;
    public ChildThreadStatus CurrentThreadStatus => ResolveThreadStatus();
    public ChildThreadSchedulingState CurrentSchedulingState => ResolveSchedulingState();
    public string CurrentThreadStatusText => BuildMonitoringStatusText(CurrentThreadStatus);

    public static int BasketBallCount => BasketSemaphoreCore.BasketCount;
    public static int BasketCapacity => BasketSemaphoreCore.Capacity;
    public static int TotalSystemBalls => BasketSemaphoreCore.TotalSystemBalls;
    public static NPCController[] GetActiveChildrenSnapshot()
    {
        // Snapshot defensivo para o HUD iterar sem segurar lock durante render.
        lock (activeChildrenLock)
            return activeChildren.ToArray();
    }

    public void AssignThreadId(int id)
    {
        if (id <= 0)
        {
            Debug.LogWarning($"[{name}] ThreadId inválido recebido: {id}");
            return;
        }

        // Define ID estável para exibição/ordenação no HUD.
        threadId = id;
    }

    public void AssignSpawnSlotIndex(int slotIndex)
    {
        // Guarda slot de origem para rastreamento/telemetria.
        spawnSlotIndex = slotIndex;
    }

    public void AssignCustomIdentifier(string identifier)
    {
        // Normaliza string para evitar espaços extras em logs e HUD.
        customChildIdentifier = string.IsNullOrWhiteSpace(identifier) ? string.Empty : identifier.Trim();
    }

    // Entra na lista global assim que o objeto fica ativo na cena.
    private void OnEnable()
    {
        // Registro é feito ao habilitar para refletir apenas NPCs ativos em cena.
        RegisterActiveChild();
    }

    // Converte o estado interno do visual/thread em uma enum simples para UI/lógica.
    private ChildThreadStatus ResolveThreadStatus()
    {
        if (threadCore != null)
            // Quando a thread existe, ela é fonte da verdade do estado lógico.
            return threadCore.CurrentStatus;

        if (phase == NpcPhase.GoingToRestSpotTD || phase == NpcPhase.RestingAtSpotTD)
            return ChildThreadStatus.Resting;

        if (phase == NpcPhase.WaitingForBasketTurn)
            return activeVisualQueue == BasketQueueType.PutBall
                ? ChildThreadStatus.WaitingBasketSpace
                : ChildThreadStatus.WaitingBallInBasket;

        return hasBall ? ChildThreadStatus.PlayingWithBall : ChildThreadStatus.WaitingBallInBasket;
    }

    // Resolve o estado de escalonamento publicado no HUD (Executando/Pronto/Bloqueado).
    private ChildThreadSchedulingState ResolveSchedulingState()
    {
        if (threadCore != null)
            // Usa estado publicado pelo core quando disponível.
            return threadCore.CurrentSchedulingState;

        ChildThreadStatus status = ResolveThreadStatus();
        // Fallback local sem thread: status de fila mapeia para bloqueado.
        return status == ChildThreadStatus.WaitingBasketSpace || status == ChildThreadStatus.WaitingBallInBasket
            ? ChildThreadSchedulingState.Blocked
            : ChildThreadSchedulingState.Running;
    }

    // Traduz o status para texto amigável exibido no HUD.
    private string BuildMonitoringStatusText(ChildThreadStatus status)
    {
        switch (status)
        {
            case ChildThreadStatus.PlayingWithBall:
                return $"Brincando ({Math.Max(0, CurrentPlayTimeRemainingSeconds)})";
            case ChildThreadStatus.WaitingBallInBasket:
                return "Aguardando que outra criança coloque uma bola no cesto";
            case ChildThreadStatus.WaitingBasketSpace:
                return "Aguardando que o cesto tenha espaço para que ela coloque sua bola";
            case ChildThreadStatus.Resting:
                return $"Descansando ({Math.Max(0, CurrentRestTimeRemainingSeconds)})";
            default:
                return string.Empty;
        }
    }

    // Publica um primeiro snapshot com Tb/Td e status corrente.
    private void LogInitialChildSnapshot()
    {
        // Marca que já houve emissão inicial para diferenciar de mudanças subsequentes.
        hasPublishedInitialChildLog = true;
        lastPublishedThreadStatus = CurrentThreadStatus;
        EmitChildLog(
            $"[{ChildIdentifier}] dados: Tb={tb:0.##}, Td={td:0.##}, status={BuildActivityLogStatusText(lastPublishedThreadStatus)}"
        );
    }

    // Emite log sempre que o status da thread muda.
    private void TrackThreadStatusChanges()
    {
        if (!hasPublishedInitialChildLog)
        {
            LogInitialChildSnapshot();
            return;
        }

        ChildThreadStatus currentStatus = CurrentThreadStatus;
        // Evita ruído: só loga quando houver transição real.
        if (currentStatus == lastPublishedThreadStatus)
            return;

        EmitChildLog(
            $"[{ChildIdentifier}] status: {BuildActivityLogStatusText(lastPublishedThreadStatus)} -> {BuildActivityLogStatusText(currentStatus)}"
        );
        // Atualiza memória do último status para próximas comparações.
        lastPublishedThreadStatus = currentStatus;
    }

    // Centraliza a emissão de log no Console e no evento do HUD.
    private void EmitChildLog(string message)
    {
        // Console local + evento global para HUD.
        Debug.Log(message);
        ChildEventLogged?.Invoke(message);
    }

    // Mantém uma versão estável dos rótulos para o log de atividades.
    private static string BuildActivityLogStatusText(ChildThreadStatus status)
    {
        switch (status)
        {
            case ChildThreadStatus.PlayingWithBall:
                return "Brincando com a bola";
            case ChildThreadStatus.WaitingBallInBasket:
                return "Aguardando que outra criança coloque uma bola no cesto";
            case ChildThreadStatus.WaitingBasketSpace:
                return "Aguardando que o cesto tenha espaço para que ela coloque sua bola";
            case ChildThreadStatus.Resting:
                return "Descansando";
            default:
                return "Status desconhecido";
        }
    }

    // Registra um NPC ativo na lista compartilhada do HUD.
    // Adiciona um NPC à coleção compartilhada sem duplicá-lo.
    private static void RegisterActiveChild(NPCController child)
    {
        if (child == null)
            return;

        lock (activeChildrenLock)
        {
            if (!activeChildren.Contains(child))
                activeChildren.Add(child);
        }
    }

    // Remove um NPC desativado da lista compartilhada do HUD.
    // Remove um NPC da coleção compartilhada.
    private static void UnregisterActiveChild(NPCController child)
    {
        if (child == null)
            return;

        lock (activeChildrenLock)
            activeChildren.Remove(child);
    }

    // Wrapper de instância para registrar este próprio NPC.
    private void RegisterActiveChild()
    {
        RegisterActiveChild(this);
    }

    // Wrapper de instância para remover este próprio NPC.
    private void UnregisterActiveChild()
    {
        UnregisterActiveChild(this);
    }

    // Limpa a coleção global quando a cena/domínio reinicia.
    private static void ResetActiveChildren()
    {
        lock (activeChildrenLock)
            activeChildren.Clear();
    }
}
