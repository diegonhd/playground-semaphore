using UnityEngine;

[RequireComponent(typeof(Animator))]
public partial class NPCController : MonoBehaviour
{
    // Configurações principais ajustáveis no Inspector.
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float minIdleTime = 0.5f;
    [SerializeField] private float maxIdleTime = 2f;
    [SerializeField] private float tb = 5f;
    [SerializeField] private float td = 5f;
    [SerializeField] private bool useDefaultSpawnPosition = false;
    [SerializeField] private Vector3 defaultSpawnPosition = new Vector3(4.15f, 0.62f, 1f);
    [SerializeField] private LayerMask worldCollisionLayer;
    [SerializeField] private LayerMask npcCollisionLayer;
    [SerializeField] private Vector3 transitionPoint = new Vector3(2.35f, 3.38f, 1f);
    [SerializeField] private float transitionRightOffset = 0.2f;
    [SerializeField] private float transitionWaitSeconds = 2f;
    [SerializeField] private float collisionCheckRadius = 0.12f;
    [SerializeField] private float npcOccupancyCheckRadius = 0.12f;

    // Estado interno de execução do controlador.
    private Animator animator;
    private bool isMoving;
    private float nextMoveTime;
    private float phaseEndTime;
    private float waitEndTime;
    private bool hasInitializedSpawn;

    // Limites retangulares das duas zonas de movimentação.
    private readonly MoveArea area = new MoveArea(-1.27f, -8.98f, 5.75f, -2.77f);
    private readonly MoveArea outsideArea = new MoveArea(5.51f, 1.51f, 2.74f, -3.19f);

    private NpcPhase phase;
    private Vector3 phaseTarget;
    private Vector3 previousRoamTile;
    private bool hasPreviousRoamTile;
    private Vector2 lastRoamDirection;
    private bool hasLastRoamDirection;
    private Vector3 previousDirectedTile;
    private bool hasPreviousDirectedTile;
    private int roamNoMoveCount;
    private int directedRetryCount;
    private bool hasExternalSpawnPosition;
    private Vector3 externalSpawnPosition;

    // Cacheia componentes e inicializa o primeiro alvo de navegação.
    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null) Debug.LogError("Animator não encontrado em " + gameObject.name);

        ApplySpawnPosition();
        ResetLifecycle();
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0.01f, moveSpeed);
        minIdleTime = Mathf.Max(0f, minIdleTime);
        maxIdleTime = Mathf.Max(minIdleTime, maxIdleTime);
        tb = Mathf.Max(0f, tb);
        td = Mathf.Max(0f, td);
        collisionCheckRadius = Mathf.Max(0.01f, collisionCheckRadius);
        npcOccupancyCheckRadius = Mathf.Max(0.01f, npcOccupancyCheckRadius);
        transitionRightOffset = Mathf.Max(0f, transitionRightOffset);
        transitionWaitSeconds = Mathf.Max(0f, transitionWaitSeconds);
        defaultSpawnPosition.z = 1f;
        transitionPoint.z = 1f;
    }

    private void ResetLifecycle()
    {
        phase = NpcPhase.GoingToArea;
        phaseTarget = RandomPointInArea(area);
        directedRetryCount = 0;
        roamNoMoveCount = 0;
        hasPreviousRoamTile = false;
        hasLastRoamDirection = false;
        hasPreviousDirectedTile = false;
        nextMoveTime = Time.time + Random.Range(0.05f, 0.35f);
    }

    // Aplica posição de spawn quando o objeto já iniciou.
    private void ApplySpawnPosition()
    {
        if (hasInitializedSpawn)
            return;

        hasInitializedSpawn = true;

        if (hasExternalSpawnPosition)
            transform.position = externalSpawnPosition;
        else if (useDefaultSpawnPosition)
            transform.position = defaultSpawnPosition;

        ForceZToOne();
    }

    // Chamado pelo ChildBehaviour para definir spawn customizado.
    public void SetSpawnPosition(Vector3 spawnPosition)
    {
        externalSpawnPosition = new Vector3(spawnPosition.x, spawnPosition.y, 1f);
        hasExternalSpawnPosition = true;
        hasInitializedSpawn = true;
        transform.position = externalSpawnPosition;
        ResetLifecycle();
    }

    // Máquina de estados principal do ciclo de vida do NPC.
    private void Update()
    {
        ForceZToOne();

        if (animator != null)
            animator.SetBool("isMoving", isMoving);

        if (isMoving || Time.time < nextMoveTime)
            return;

        switch (phase)
        {
            case NpcPhase.GoingToArea:
                // Entra na área interna antes de iniciar o roaming de TB.
                if (area.Contains(transform.position))
                {
                    OnReachedArea();
                    break;
                }
                ProcessDirectedMovement(phaseTarget, OnReachedArea);
                break;

            case NpcPhase.RoamingAreaTB:
                // Vaga na área interna até o tempo TB acabar.
                if (Time.time >= phaseEndTime)
                {
                    phase = NpcPhase.GoingToTransitionPoint;
                    phaseTarget = transitionPoint;
                    nextMoveTime = Time.time;
                }
                else
                {
                    ProcessNpcMoveInArea(area);
                }
                break;

            case NpcPhase.GoingToTransitionPoint:
                // Vai até o ponto de transição.
                ProcessDirectedMovement(phaseTarget, OnReachedTransitionPoint);
                break;

            case NpcPhase.GoingSlightlyRightAtTransition:
                // Dá um pequeno passo para a direita após chegar no ponto de transição.
                if (IsOccupiedByOtherNpc(phaseTarget))
                {
                    nextMoveTime = Time.time + 0.1f;
                    break;
                }
                ProcessDirectedMovement(phaseTarget, OnReachedSlightRightAtTransition);
                break;

            case NpcPhase.WaitingAfterRightMove:
                // Aguarda 2 segundos após o passo à direita.
                if (Time.time >= waitEndTime)
                {
                    phase = NpcPhase.GoingToOutsideArea;
                    phaseTarget = RandomPointInArea(outsideArea);
                    nextMoveTime = Time.time;
                }
                break;

            case NpcPhase.GoingToOutsideArea:
                // Vai para um ponto da área externa.
                ProcessDirectedMovement(phaseTarget, OnReachedOutsideArea);
                break;

            case NpcPhase.RoamingOutsideTD:
                // Vaga na área externa até o tempo TD acabar.
                if (Time.time >= phaseEndTime)
                    phase = NpcPhase.Done;
                else
                    ProcessNpcMoveInArea(outsideArea);
                break;
        }
    }

    // Entra na fase de roaming da área interna.
    private void OnReachedArea()
    {
        directedRetryCount = 0;
        roamNoMoveCount = 0;
        hasPreviousRoamTile = false;
        hasLastRoamDirection = false;
        hasPreviousDirectedTile = false;
        phase = NpcPhase.RoamingAreaTB;
        phaseEndTime = Time.time + tb;
        ScheduleNextMove();
    }

    // Entra na fase de espera ao alcançar a transição.
    private void OnReachedTransitionPoint()
    {
        directedRetryCount = 0;
        hasPreviousDirectedTile = false;
        phase = NpcPhase.GoingSlightlyRightAtTransition;
        phaseTarget = GetTransitionRightTarget();
        nextMoveTime = Time.time;
    }

    // Após o ajuste lateral, aguarda antes de ir para fora.
    private void OnReachedSlightRightAtTransition()
    {
        directedRetryCount = 0;
        hasPreviousDirectedTile = false;
        phase = NpcPhase.WaitingAfterRightMove;
        waitEndTime = Time.time + transitionWaitSeconds;
        nextMoveTime = waitEndTime;
    }

    // Entra na fase de roaming da área externa após chegar fora.
    private void OnReachedOutsideArea()
    {
        directedRetryCount = 0;
        roamNoMoveCount = 0;
        hasPreviousRoamTile = false;
        hasLastRoamDirection = false;
        hasPreviousDirectedTile = false;
        phase = NpcPhase.RoamingOutsideTD;
        phaseEndTime = Time.time + td;
        ScheduleNextMove();
    }

    private Vector3 GetTransitionRightTarget()
    {
        return new Vector3(
            transitionPoint.x + transitionRightOffset,
            transitionPoint.y,
            1f
        );
    }

    private void ForceZToOne()
    {
        Vector3 p = transform.position;
        if (Mathf.Abs(p.z - 1f) > 0.0001f)
            transform.position = new Vector3(p.x, p.y, 1f);
    }

}
