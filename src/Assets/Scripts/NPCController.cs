// Controlador visual principal das crianças: movimentação por fases, filas de put/take,
// spots de descanso exclusivos e ponte de prontidão visual para a thread concorrente.
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public partial class NPCController : MonoBehaviour
{
    // Velocidade visual de deslocamento do NPC (unidades por segundo).
    private float moveSpeed = 2f;
    // Parâmetro Tb configurado para esta criança.
    private float tb = 5f;
    // Parâmetro Td configurado para esta criança.
    private float td = 5f;
    // Estado visual local: criança está com bola agora?
    [SerializeField] private bool hasBall;
    // Quando true, emite logs detalhados de transição de estado.
    [SerializeField] private bool logRuntimeState = true;
    // Máscara de colisão do cenário (paredes/obstáculos), ignorando outros NPCs.
    [SerializeField] private LayerMask worldCollisionLayer;

    // Janela de tempo ocioso entre passos de roaming.
    private const float minIdleTime = 0.5f;
    private const float maxIdleTime = 2f;
    // Raio da checagem de colisão para validar uma célula como caminhável.
    private const float collisionCheckRadius = 0.2f;
    // Tempo de espera antes de tentar reservar novamente um spot de descanso.
    private const float restSpotRetryDelaySeconds = 0.1f;
    // Capacidade inicial padrão do cesto quando este NPC sobe a thread.
    private const int basketCapacityK = 4;

    // Ponto de transição entre regiões do mapa (área de brincar, filas, descanso).
    private readonly Vector3 transitionPoint = new Vector3(-5.149299f, 2.803256f, 1f);
    // Retângulo da área de brincar (TB).
    private readonly MoveArea area = new MoveArea(-7.07f, -11.15f, 1.39f, -0.02f);

    // Slots físicos da fila de TAKE (quem está sem bola aguardando consumir).
    private static readonly Vector3[] takeQueuePositions = new Vector3[]
    {
        new Vector3(-1.47f, 2.54f, 1f),
        new Vector3(-0.25f, 2.54f, 1f),
        new Vector3(1.03f, 2.54f, 1f),
        new Vector3(2.25f, 2.54f, 1f),
        new Vector3(2.25f, 3.62f, 1f),
        new Vector3(1.03f, 3.62f, 1f),
        new Vector3(-0.25f, 3.62f, 1f),
        new Vector3(-0.25f, 4.8f, 1f),
        new Vector3(1.03f, 4.8f, 1f),
        new Vector3(2.25f, 4.8f, 1f),
    };

    // Slots físicos da fila de PUT (quem está com bola aguardando produzir).
    private static readonly Vector3[] putQueuePositions = new Vector3[]
    {
        new Vector3(-3.62f, 2.54f, 1f),
        new Vector3(-4.82f, 2.54f, 1f),
        new Vector3(-6.02f, 2.54f, 1f),
        new Vector3(-7.22f, 2.54f, 1f),
        new Vector3(-7.41f, 3.47f, 1f),
        new Vector3(-6.35f, 3.37f, 1f),
        new Vector3(-5.3f, 3.44f, 1f),
        new Vector3(-5.3f, 4.41f, 1f),
        new Vector3(-6.35f, 4.48f, 1f),
        new Vector3(-7.41f, 4.38f, 1f)
    };

    // Spots de descanso exclusivos (TD): um NPC por posição.
    private static readonly Vector3[] restSpotPositions = new Vector3[]
    {
        new Vector3(3.37f, 4.68f, 1f),
        new Vector3(4.19f, 4.68f, 1f),
        new Vector3(6.41f, 3.1f, 1f),
        new Vector3(6.41f, 2.25f, 1f),
        new Vector3(6.62f, 0.94f, 1f),
        new Vector3(6.62f, 4.22f, 1f),
        new Vector3(5.04f, 4.68f, 1f),
        new Vector3(3.37f, 1.05f, 1f),
        new Vector3(4.19f, 1.05f, 1f),
        new Vector3(5.04f, 1.05f, 1f),
    };

    // Estruturas estáticas que representam as filas visuais compartilhadas da cena.
    private static readonly List<NPCController> putBallVisualQueue = new List<NPCController>();
    private static readonly List<NPCController> takeBallVisualQueue = new List<NPCController>();
    // Ocupação exclusiva dos spots de descanso (um NPC por posição).
    private static readonly NPCController[] restSpotOccupants = new NPCController[restSpotPositions.Length];
    // Índice round-robin para distribuir ocupação de spots de descanso.
    private static int nextRestSpotPickIndex;
    // Gerador de ID automático quando a criança nasce sem ID explícito.
    private static int nextAutoThreadId = 1;

    // Referências/flags de estado visual local.
    private Animator animator;
    private bool isMoving;
    private float nextMoveTime;
    private bool hasInitializedSpawn;
    private bool hasExternalSpawnPosition;
    private Vector3 externalSpawnPosition;

    // Estado da máquina de fases de navegação visual do NPC.
    private NpcPhase phase;
    private Vector3 phaseTarget;
    private Vector3 previousRoamTile;
    // Variáveis de histórico para evitar que o o personagem fique preso ou repetitivo 
    // (ex.: colisão contínua, sem progresso, etc.).
    private bool hasPreviousRoamTile;
    // Guarda a última direção  válida para tentar preferi-la 
    // em caso de colisão ou falta de progresso.
    private Vector2 lastRoamDirection;
    // Flag para indicar se a última direção de roaming é válida e pode ser tentada novamente.
    private bool hasLastRoamDirection;
    private Vector3 previousDirectedTile;
    private bool hasPreviousDirectedTile;
    private int roamNoMoveCount;
    private int directedRetryCount;

    // Estado atual de fila deste NPC e reservas de slots visuais.
    private BasketQueueType activeVisualQueue = BasketQueueType.None;
    private int assignedQueueSlotIndex = -1;
    private int assignedRestSpotIndex = -1;

    // Ponte para o núcleo concorrente (thread real da criança).
    private ChildThreadCore threadCore;

    // Limpa estado estático compartilhado entre instâncias ao reiniciar o domínio.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetVisualState()
    {
        // Limpa filas visuais compartilhadas entre todas as instâncias.
        putBallVisualQueue.Clear();
        takeBallVisualQueue.Clear();
        // Zera ocupação de spots e ponteiros globais para um novo ciclo limpo.
        System.Array.Clear(restSpotOccupants, 0, restSpotOccupants.Length);
        nextRestSpotPickIndex = 0;
        nextAutoThreadId = 1;
        // Limpa snapshot global usado pelo HUD/monitoramento.
        ResetActiveChildren();
    }

    // Permite reset explícito do estado estático do NPC durante a execução.
    public static void ResetRuntimeStateNow()
    {
        // Encapsula o reset estático para chamadas externas (ex.: botão de reset do jogo).
        ResetVisualState();
    }

    // Inicialização do componente: pega Animator, ajusta posição inicial e reinicia ciclo.
    private void Awake()
    {
        // Obtém componente de animação obrigatório para atualizar parâmetros visuais.
        animator = GetComponent<Animator>();
        if (animator == null)
            Debug.LogError("Animator não encontrado em " + gameObject.name);

        // Aplica spawn inicial e reseta estado de navegação antes do primeiro frame.
        ApplySpawnPosition();
        ResetMovementLifecycle();
    }

    // Sobe a thread de controle e publica o primeiro snapshot para o HUD/log.
    private void Start()
    {
        // Sobe a thread core assim que o NPC entra em execução.
        StartThreadCoreIfNeeded();
        // Publica snapshot inicial para log/HUD.
        LogInitialChildSnapshot();
    }

    // Limpeza de runtime: remove registros, libera fila/spot e encerra a thread.
    private void OnDisable()
    {
        // Remove da lista global de monitoramento.
        UnregisterActiveChild();
        // Libera estruturas compartilhadas para não deixar reservas órfãs.
        LeaveVisualQueue();
        ReleaseRestSpot();
        // Encerra a thread concorrente associada a este NPC.
        StopThreadCore();
    }

    // Garante valores válidos no Inspector para evitar estados irreais no runtime.
    private void OnValidate()
    {
        // Garante parâmetros mínimos seguros quando alterados no Inspector.
        moveSpeed = Mathf.Max(0.01f, moveSpeed);
        tb = Mathf.Max(0f, tb);
        td = Mathf.Max(0f, td);
    }

    // Posiciona este NPC explicitamente em um ponto fixo de spawn.
    public void SetSpawnPosition(Vector3 spawnPosition)
    {
        // Converte para o plano 2D do jogo (z=1) e fixa spawn manual para esta instância.
        externalSpawnPosition = new Vector3(spawnPosition.x, spawnPosition.y, 1f);
        hasExternalSpawnPosition = true;
        hasInitializedSpawn = true;
        transform.position = externalSpawnPosition;
        // Reinicia fase/alvos para evitar inconsistência após teleporte de spawn.
        ResetMovementLifecycle();
    }

    // Define se a criança começa com bola antes da thread ser iniciada.
    public void ConfigureSpawnHasBall(bool startsWithBall)
    {
        if (threadCore != null)
        {
            Debug.LogWarning($"[{ChildIdentifier}] Não é possível alterar hasBall após iniciar a thread.");
            return;
        }

        if (hasBall == startsWithBall)
            return;

        // Aplica o estado inicial de posse da bola antes da criação da thread concorrente.
        hasBall = startsWithBall;
        if (animator != null)
            animator.SetBool("hasBall", hasBall);
    }

    // Define Tb/Td antes de iniciar a thread da criança.
    public void ConfigureSpawnTimings(float playTb, float restTd)
    {
        if (threadCore != null)
        {
            Debug.LogWarning($"[{ChildIdentifier}] Não é possível alterar Tb/Td após iniciar a thread.");
            return;
        }

        // Normaliza entradas para não permitir tempos negativos no ciclo da criança.
        tb = Mathf.Max(0f, playTb);
        td = Mathf.Max(0f, restTd);
    }

    // Loop visual por frame: sincroniza estado da thread e avança a fase corrente.
    private void Update()
    {
        // Mantém consistência do plano 2D a cada frame (evita drift de Z).
        ForceZToOne();
        // Espelha estado produzido pela thread para o lado visual do NPC.
        SyncStateFromCore();

        // O status vem da thread core e dirige o estado visual do NPC.
        ChildThreadStatus status = ResolveThreadStatus();
        // Publica para a thread se o NPC já está na posição correta para avançar de etapa.
        PublishVisualReadiness(status);

        if (animator != null)
        {
            // Atualiza parâmetros de animação com o snapshot atual.
            animator.SetBool("isMoving", isMoving);
            animator.SetBool("hasBall", hasBall);
            // Verdadeiro apenas durante Td (status de descanso).
            animator.SetBool("isRestingTd", status == ChildThreadStatus.Resting);
        }

        // Emite logs de transição de estado quando configurado.
        TrackThreadStatusChanges();

        // Se já está em deslocamento ou em cooldown de decisão, não recalcula fase agora.
        if (isMoving || Time.time < nextMoveTime)
            return;

        // Despacha a máquina de estados visuais com base no estado da thread.
        switch (status)
        {
            case ChildThreadStatus.PlayingWithBall:
                HandlePlayingWithBall();
                break;

            case ChildThreadStatus.WaitingBasketSpace:
            case ChildThreadStatus.WaitingBallInBasket:
                HandleWaitingForBasket(status);
                break;

            case ChildThreadStatus.Resting:
                HandleResting();
                break;
        }
    }

    // Cria e inicia a thread de SO associada a este NPC.
    private void StartThreadCoreIfNeeded()
    {
        // Garante idempotência: evita criar duas threads para o mesmo NPC.
        if (threadCore != null)
            return;

        // Atribui ID automático quando não foi definido externamente.
        if (threadId <= 0)
            threadId = nextAutoThreadId++;

        // Inicializa núcleo concorrente global e inicia a thread desta criança.
        BasketSemaphoreCore.EnsureInitialized(basketCapacityK);
        threadCore = new ChildThreadCore(threadId, tb, td, hasBall, basketCapacityK);
        // Inicia o loop concorrente (Tb -> put -> Td -> take).
        threadCore.Start();
    }

    // Encerra a thread e limpa a referência local.
    private void StopThreadCore()
    {
        if (threadCore == null)
            return;

        // Dispose aciona cancelamento cooperativo e tenta join da thread de SO.
        threadCore.Dispose();
        // Limpa referência para marcar que este NPC não está mais ligado a uma thread ativa.
        threadCore = null;
    }

    // Copia estado thread-safe da thread core para o lado visual do NPC.
    private void SyncStateFromCore()
    {
        if (threadCore == null)
            return;

        // Espelha flag produzida no core para manter HUD/animação coerentes.
        SetHasBall(threadCore.HasBall);
    }

    // Informa à thread core se o NPC já está fisicamente em posição para prosseguir.
    private void PublishVisualReadiness(ChildThreadStatus status)
    {
        if (threadCore == null)
            return;

        // Bridge entre mundo visual (Unity) e thread core (sem Unity API):
        // a thread só prossegue quando o avatar alcança o contexto físico esperado.
        bool playAreaReady = status == ChildThreadStatus.PlayingWithBall && area.Contains(transform.position);
        bool restSpotReady = false;
        bool putQueueTurnReady = false;
        bool takeQueueTurnReady = false;
        if (status == ChildThreadStatus.Resting && assignedRestSpotIndex >= 0 && assignedRestSpotIndex < restSpotPositions.Length)
            // Em Td, só libera a thread quando o NPC atingiu o spot reservado.
            restSpotReady = HasReachedTarget2D(restSpotPositions[assignedRestSpotIndex]);

        if (status == ChildThreadStatus.WaitingBasketSpace &&
            activeVisualQueue == BasketQueueType.PutBall &&
            assignedQueueSlotIndex == 0)
        {
            // Put só pode avançar quando o NPC está no primeiro slot da fila de put.
            putQueueTurnReady = HasReachedTarget2D(GetQueueTarget(BasketQueueType.PutBall, 0));
        }

        if (status == ChildThreadStatus.WaitingBallInBasket &&
            activeVisualQueue == BasketQueueType.TakeBall &&
            assignedQueueSlotIndex == 0)
        {
            // Take só pode avançar quando o NPC está no primeiro slot da fila de take.
            takeQueueTurnReady = HasReachedTarget2D(GetQueueTarget(BasketQueueType.TakeBall, 0));
        }

        // Entrega o snapshot de prontidão para a thread (ponte visual -> concorrente).
        threadCore.SetVisualReadiness(playAreaReady, restSpotReady, putQueueTurnReady, takeQueueTurnReady);
    }

    // Fase TB: garante que a criança esteja na área de brincar antes de continuar o ciclo.
    private void HandlePlayingWithBall()
    {
        // Em Tb não deve ocupar fila nem spot de descanso.
        LeaveVisualQueue();
        ReleaseRestSpot();

        if (!area.Contains(transform.position))
        {
            // Fora da área de brincar: entra em fase dirigida até reingressar na área válida.
            phase = NpcPhase.GoingToArea;
            if (!area.Contains(phaseTarget))
                phaseTarget = RandomPointInArea(area);

            ProcessDirectedMovement(phaseTarget, OnReachedArea);
            return;
        }

        // Já na área: segue roaming local de Tb.
        phase = NpcPhase.RoamingAreaTB;
        ProcessNpcMoveInArea(area);
    }

    // Fase de fila: move até o slot correto e mantém o NPC parado ao chegar.
    private void HandleWaitingForBasket(ChildThreadStatus status)
    {
        // Enquanto espera cesto, não deve manter reserva de spot de descanso.
        ReleaseRestSpot();
        if (!TryGetCurrentQueueTarget(status, out Vector3 queueTarget))
        {
            // Sem alvo válido no frame atual: reavalia em curto intervalo.
            nextMoveTime = Time.time + 0.1f;
            return;
        }

        // Define fase e alvo de fila para deslocamento até o slot atribuído.
        phase = NpcPhase.WaitingForBasketTurn;
        phaseTarget = queueTarget;
        if (!HasReachedTarget2D(queueTarget))
        {
            ProcessDirectedMovement(queueTarget, OnReachedBasketWaitingSpot);
            return;
        }

        if (animator != null)
        {
            // Ajusta facing em idle na fila para manter leitura visual do estado.
            float idleFacingX = status == ChildThreadStatus.WaitingBasketSpace ? 1f : -1f;
            animator.SetFloat("moveX", idleFacingX);
            animator.SetFloat("moveY", 0f);
        }

        // Mantém polling suave enquanto aguarda o desbloqueio da thread concorrente.
        nextMoveTime = Time.time + 0.1f;
    }

    // Fase TD: procura e ocupa um spot de descanso exclusivo.
    private void HandleResting()
    {
        // Em Td não participa de fila de cesto.
        LeaveVisualQueue();
        if (!TryGetRestSpotTarget(out Vector3 restTarget))
        {
            // Sem spot livre agora: tenta novamente após um pequeno atraso.
            nextMoveTime = Time.time + restSpotRetryDelaySeconds;
            return;
        }

        // Com spot reservado, navega até ele e entra em idle de descanso.
        phaseTarget = restTarget;
        if (!HasReachedTarget2D(restTarget))
        {
            phase = NpcPhase.GoingToRestSpotTD;
            ProcessDirectedMovement(restTarget, OnReachedRestSpot);
            return;
        }

        OnReachedRestSpot();
    }

    // Callback quando a criança chega na área principal de brincar.
    private void OnReachedArea()
    {
        // Limpa contadores de retry/histórico para reiniciar roaming sem viés.
        directedRetryCount = 0;
        roamNoMoveCount = 0;
        hasPreviousRoamTile = false;
        hasLastRoamDirection = false;
        hasPreviousDirectedTile = false;
        // Ao atingir a área, entra no estado de roaming de Tb.
        phase = NpcPhase.RoamingAreaTB;
        // Agenda próximo passo com atraso aleatório para naturalidade.
        ScheduleNextMove();
    }

    // Callback simples para estabilizar o movimento quando o NPC chega à fila.
    private void OnReachedBasketWaitingSpot()
    {
        // Reavalia rapidamente status/fila para avançar quando liberar a vez.
        nextMoveTime = Time.time + 0.1f;
    }

    // Fixa o NPC exatamente no spot de descanso e mantém a animação em idle.
    private void OnReachedRestSpot()
    {
        // Marca fase estável de descanso após chegada ao spot.
        phase = NpcPhase.RestingAtSpotTD;
        if (assignedRestSpotIndex >= 0 && assignedRestSpotIndex < restSpotPositions.Length)
            // Corrige posição final para o centro exato do spot reservado.
            transform.position = restSpotPositions[assignedRestSpotIndex];

        if (animator != null)
        {
            // Subdivide os spots de descanso em 3 faixas de orientação idle:
            // 0..2 = IdleDown, 3..6 = IdleLeft, 7..9 = IdleUp.
            Vector2 restIdleFacing = ResolveRestSpotIdleFacing(assignedRestSpotIndex);
            animator.SetFloat("moveX", restIdleFacing.x);
            animator.SetFloat("moveY", restIdleFacing.y);
        }
        // Polling leve para acompanhar mudança de estado da thread.
        nextMoveTime = Time.time + 0.1f;
    }

    private static Vector2 ResolveRestSpotIdleFacing(int restSpotIndex)
    {
        if (restSpotIndex >= 0 && restSpotIndex <= 2)
            return Vector2.down;

        if (restSpotIndex >= 3 && restSpotIndex <= 6)
            return Vector2.left;

        if (restSpotIndex >= 7 && restSpotIndex <= 9)
            return Vector2.up;

        // Fallback seguro para índices inesperados.
        return Vector2.left;
    }

    // Mapeia o status da thread para a fila visual correspondente.
    private BasketQueueType ResolveRequiredQueueType(ChildThreadStatus status)
    {
        switch (status)
        {
            case ChildThreadStatus.WaitingBasketSpace:
                return BasketQueueType.PutBall;
            case ChildThreadStatus.WaitingBallInBasket:
                return BasketQueueType.TakeBall;
            default:
                return BasketQueueType.None;
        }
    }

    // Calcula a posição alvo da fila de acordo com a ordem atual do NPC na lista.
    private bool TryGetCurrentQueueTarget(ChildThreadStatus status, out Vector3 waitingTarget)
    {
        BasketQueueType requiredQueue = ResolveRequiredQueueType(status);
        if (requiredQueue == BasketQueueType.None)
        {
            // Sem necessidade de fila neste estado: limpa associação e encerra cálculo.
            waitingTarget = transform.position;
            LeaveVisualQueue();
            return false;
        }

        // Garante presença na fila correta e remove entradas inválidas antes de calcular índice.
        EnsureVisualQueueMembership(requiredQueue);
        PruneVisualQueue(requiredQueue);

        List<NPCController> queue = GetVisualQueue(requiredQueue);
        if (queue == null)
        {
            waitingTarget = transform.position;
            return false;
        }

        int queueIndex = queue.IndexOf(this);
        if (queueIndex < 0)
        {
            // Se não foi encontrado após prune/reinserção, aborta neste frame.
            waitingTarget = transform.position;
            return false;
        }

        // Índice visual da fila define a posição física de espera desse NPC.
        assignedQueueSlotIndex = Mathf.Clamp(queueIndex, 0, GetQueueMaxIndex(requiredQueue));
        waitingTarget = GetQueueTarget(requiredQueue, assignedQueueSlotIndex);
        return true;
    }

    // Garante que o NPC pertença à fila correta antes de calcular o slot.
    private void EnsureVisualQueueMembership(BasketQueueType requiredQueue)
    {
        List<NPCController> queue = GetVisualQueue(requiredQueue);
        if (queue == null)
            return;

        // Já está corretamente vinculado à fila solicitada.
        if (activeVisualQueue == requiredQueue && queue.Contains(this))
            return;

        // Troca de fila é sempre explícita para manter ordenação consistente.
        LeaveVisualQueue();
        // Inserção no final preserva ordem de chegada para avançar por slots.
        queue.Add(this);
        activeVisualQueue = requiredQueue;
    }

    // Remove este NPC de qualquer fila visual para evitar duplicidade de ocupação.
    private void LeaveVisualQueue()
    {
        putBallVisualQueue.Remove(this);
        takeBallVisualQueue.Remove(this);
        activeVisualQueue = BasketQueueType.None;
        assignedQueueSlotIndex = -1;
    }

    // Retorna a lista estática da fila de put/take correspondente.
    private static List<NPCController> GetVisualQueue(BasketQueueType queueType)
    {
        switch (queueType)
        {
            case BasketQueueType.PutBall:
                return putBallVisualQueue;
            case BasketQueueType.TakeBall:
                return takeBallVisualQueue;
            default:
                return null;
        }
    }

    // Remove NPCs inválidos/inativos para manter a fila consistente com a cena.
    private static void PruneVisualQueue(BasketQueueType queueType)
    {
        List<NPCController> queue = GetVisualQueue(queueType);
        if (queue == null || queue.Count == 0)
            return;

        for (int i = queue.Count - 1; i >= 0; i--)
        {
            NPCController npc = queue[i];
            if (npc == null || !npc.isActiveAndEnabled)
            {
                // Remove referências quebradas/objetos inativos.
                queue.RemoveAt(i);
                continue;
            }

            BasketQueueType npcRequiredQueue = npc.ResolveRequiredQueueType(npc.ResolveThreadStatus());
            if (npcRequiredQueue != queueType)
                // Remove NPC que mudou de estado e não deveria mais ocupar esta fila.
                queue.RemoveAt(i);
        }
    }

    // Recupera o maior índice disponível da fila (último slot).
    private static int GetQueueMaxIndex(BasketQueueType queueType)
    {
        Vector3[] positions = GetQueuePositions(queueType);
        if (positions == null || positions.Length == 0)
            return 0;

        return positions.Length - 1;
    }

    // Converte um índice da fila em uma posição física no mundo.
    private Vector3 GetQueueTarget(BasketQueueType queueType, int queueIndex)
    {
        Vector3[] positions = GetQueuePositions(queueType);
        if (positions == null || positions.Length == 0)
            return transitionPoint;

        // Clampa índice para não acessar fora dos limites do array de slots.
        int clampedIndex = Mathf.Clamp(queueIndex, 0, positions.Length - 1);
        Vector3 queueTarget = positions[clampedIndex];
        // Força plano 2D consistente.
        queueTarget.z = 1f;
        return queueTarget;
    }

    // Expõe o array de posições fixas da fila solicitada.
    private static Vector3[] GetQueuePositions(BasketQueueType queueType)
    {
        switch (queueType)
        {
            case BasketQueueType.PutBall:
                return putQueuePositions;
            case BasketQueueType.TakeBall:
                return takeQueuePositions;
            default:
                return null;
        }
    }

    // Escolhe um spot livre de forma estável, preservando exclusividade entre NPCs.
    private bool TryGetRestSpotTarget(out Vector3 restSpotTarget)
    {
        // Limpa claims obsoletos antes de tentar reservar.
        PruneRestSpotClaims();

        if (assignedRestSpotIndex >= 0 && assignedRestSpotIndex < restSpotOccupants.Length)
        {
            NPCController currentOwner = restSpotOccupants[assignedRestSpotIndex];
            if (currentOwner == null || currentOwner == this)
            {
                // Mantém/recupera posse do spot previamente atribuído quando ainda válido.
                restSpotOccupants[assignedRestSpotIndex] = this;
                restSpotTarget = restSpotPositions[assignedRestSpotIndex];
                return true;
            }
        }

        // Round-robin entre spots livres para distribuir ocupação ao longo do tempo.
        int totalSpots = restSpotPositions.Length;
        for (int offset = 0; offset < totalSpots; offset++)
        {
            int candidateIndex = (nextRestSpotPickIndex + offset) % totalSpots;
            NPCController owner = restSpotOccupants[candidateIndex];
            if (owner != null && owner != this)
                continue;

            // Reserva spot livre e avança ponteiro round-robin para próxima distribuição.
            restSpotOccupants[candidateIndex] = this;
            assignedRestSpotIndex = candidateIndex;
            nextRestSpotPickIndex = (candidateIndex + 1) % totalSpots;
            restSpotTarget = restSpotPositions[candidateIndex];
            return true;
        }

        // Sem spot disponível no momento.
        restSpotTarget = transitionPoint;
        return false;
    }

    // Libera spots cuja criança saiu do estado de descanso ou foi desativada.
    private static void PruneRestSpotClaims()
    {
        for (int i = 0; i < restSpotOccupants.Length; i++)
        {
            NPCController occupant = restSpotOccupants[i];
            if (occupant == null)
                continue;

            if (!occupant.isActiveAndEnabled || occupant.ResolveThreadStatus() != ChildThreadStatus.Resting)
            {
                // Libera claim quando o dono saiu de cena ou não está mais em estado de descanso.
                restSpotOccupants[i] = null;
                if (occupant.assignedRestSpotIndex == i)
                    occupant.assignedRestSpotIndex = -1;
            }
        }
    }

    // Remove a posse deste NPC sobre o spot atual, se houver.
    private void ReleaseRestSpot()
    {
        if (assignedRestSpotIndex < 0 || assignedRestSpotIndex >= restSpotOccupants.Length)
        {
            // Já sem spot válido: normaliza estado local e retorna.
            assignedRestSpotIndex = -1;
            return;
        }

        // Remove posse global se este NPC ainda for o dono registrado do spot.
        if (restSpotOccupants[assignedRestSpotIndex] == this)
            restSpotOccupants[assignedRestSpotIndex] = null;

        // Limpa índice local para forçar nova reserva quando necessário.
        assignedRestSpotIndex = -1;
    }

    // Aplica a posição de spawn apenas na primeira inicialização.
    private void ApplySpawnPosition()
    {
        if (hasInitializedSpawn)
            return;

        // Evita reaplicar spawn em inicializações subsequentes.
        hasInitializedSpawn = true;
        if (hasExternalSpawnPosition)
            // Usa spawn externo quando configurado por spawner/manager.
            transform.position = externalSpawnPosition;

        // Normaliza Z após posicionamento.
        ForceZToOne();
    }

    // Reinicia as variáveis de movimento quando o spawn/estado muda.
    private void ResetMovementLifecycle()
    {
        // Zera reservas compartilhadas antes de reiniciar a fase visual.
        LeaveVisualQueue();
        ReleaseRestSpot();
        // Limpa histórico de direção/retry para não carregar viés do ciclo anterior.
        directedRetryCount = 0;
        roamNoMoveCount = 0;
        hasPreviousRoamTile = false;
        hasLastRoamDirection = false;
        hasPreviousDirectedTile = false;

        // Define fase inicial conforme posse de bola no spawn.
        phase = hasBall ? NpcPhase.GoingToArea : NpcPhase.GoingToTransitionPoint;
        phaseTarget = hasBall ? RandomPointInArea(area) : transitionPoint;
        // Introduz pequeno jitter para evitar alinhamento perfeito entre múltiplos NPCs no mesmo frame.
        nextMoveTime = Time.time + Random.Range(0.1f, 0.35f);
    }

    // Mantém Z fixo em 1 para preservar o plano 2D do jogo.
    private void ForceZToOne()
    {
        Vector3 p = transform.position;
        if (Mathf.Abs(p.z - 1f) > 0.0001f)
            // Corrige apenas quando necessário para evitar escritas por frame sem necessidade.
            transform.position = new Vector3(p.x, p.y, 1f);
    }

    // Atualiza o estado local de posse da bola e espelha na animação/log.
    private void SetHasBall(bool value)
    {
        if (hasBall == value)
            return;

        bool previousValue = hasBall;
        hasBall = value;
        if (logRuntimeState)
        {
            // Snapshot de telemetria útil para depurar divergência entre visual e núcleo concorrente.
            int capacity = BasketSemaphoreCore.Capacity > 0 ? BasketSemaphoreCore.Capacity : basketCapacityK;
            EmitChildLog(
                $"[{ChildIdentifier}] hasBall {previousValue} -> {hasBall} | phase={phase} target={phaseTarget} cesto={BasketSemaphoreCore.BasketCount}/{capacity} (M={BasketSemaphoreCore.TotalSystemBalls})"
            );
        }

        if (animator != null)
            animator.SetBool("hasBall", hasBall);
    }
}
