// Rotinas de movimento (direcionado e roaming) usando coroutines para deslocamento suave por frame.
using UnityEngine;
using System.Collections;

public partial class NPCController
{
    // Move um passo em direção ao alvo, com retries quando houver bloqueio.
    // Movimento orientado por alvo: escolhe um passo válido e só avança se houver caminho.
    private void ProcessDirectedMovement(Vector3 target, System.Action onReached)
    {
        if (HasReachedTarget2D(target))
        {
            // Se já chegou, fixa posição final e dispara callback da fase.
            transform.position = target;
            onReached.Invoke();
            return;
        }

        // Sempre tenta todas as direções válidas antes de desistir do passo.
        bool allowDetour = true;
        if (TryGetStepTowardTarget(target, out Vector3 nextStep, out Vector2 facing, allowDetour))
        {
            // Passo encontrado: zera retries e registra tile atual para evitar backtrack imediato.
            directedRetryCount = 0;
            previousDirectedTile = transform.position;
            hasPreviousDirectedTile = true;
            // Atualiza direção de animação antes de mover.
            animator.SetFloat("moveX", facing.x);
            animator.SetFloat("moveY", facing.y);
            // StartCoroutine executa movimento assíncrono sem bloquear o Update principal.
            StartCoroutine(Move(nextStep, false));
        }
        else
        {
            // Sem caminho no frame atual: incrementa retry e aplica estratégia por fase.
            directedRetryCount++;
            if (phase == NpcPhase.GoingToArea && directedRetryCount >= 20)
            {
                // Reamostra alvo de área para escapar de padrões de bloqueio persistente.
                phaseTarget = RandomPointInArea(area);
                directedRetryCount = 0;
            }
            else if (phase == NpcPhase.GoingToTransitionPoint && directedRetryCount >= 20)
            {
                // Em transição, aplica um recuo curto para não entrar em loop de retries agressivos.
                nextMoveTime = Time.time + 0.15f;
                return;
            }
            else if (phase == NpcPhase.GoingToRestSpotTD && directedRetryCount >= 40)
            {
                // Em descanso, apenas reseta contador para continuar tentando sem travar.
                directedRetryCount = 0;
            }
            // Retry rápido no próximo ciclo.
            nextMoveTime = Time.time + 0.05f;
        }
    }

    // Executa um passo aleatório de roaming dentro da área informada.
    // Roaming livre dentro da área TB, evitando loops imediatos.
    private void ProcessNpcMoveInArea(MoveArea currentArea)
    {
        if (!TryGetRandomAreaStep(currentArea, out Vector3 target, out Vector2 facing))
        {
            // Sem passo livre: acumula falhas para destravar histórico se necessário.
            roamNoMoveCount++;
            if (roamNoMoveCount >= 10)
            {
                // Limpa memória de direção/tile para ampliar espaço de busca.
                hasPreviousRoamTile = false;
                hasLastRoamDirection = false;
                roamNoMoveCount = 0;
            }
            nextMoveTime = Time.time + 0.05f;
            return;
        }

        // Passo válido: reseta falhas, ajusta animação e registra histórico.
        roamNoMoveCount = 0;
        animator.SetFloat("moveX", facing.x);
        animator.SetFloat("moveY", facing.y);
        previousRoamTile = transform.position;
        hasPreviousRoamTile = true;
        lastRoamDirection = facing;
        hasLastRoamDirection = true;
        // Move de forma suave e agenda próximo passo ao final da coroutine.
        StartCoroutine(Move(target, true));
    }

    // Escolhe um passo cardinal válido evitando retorno imediato.
    // Seleciona um passo cardinal aleatório válido dentro da área atual.
    private bool TryGetRandomAreaStep(MoveArea currentArea, out Vector3 nextTarget, out Vector2 facing)
    {
        Vector2[] directions = new Vector2[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        // Embaralha ponto de início para variar escolhas entre frames/NPCs.
        int startIndex = Random.Range(0, directions.Length);

        for (int i = 0; i < directions.Length; i++)
        {
            Vector2 dir = directions[(startIndex + i) % directions.Length];
            Vector3 candidate = transform.position + new Vector3(dir.x, dir.y, 0f);
            if (!currentArea.Contains(candidate) || !IsWalkable(candidate))
                continue;
            if (hasPreviousRoamTile && Vector2.Distance(candidate, previousRoamTile) < 0.01f)
                // Evita voltar imediatamente para o tile de origem.
                continue;
            if (hasLastRoamDirection && Vector2.Dot(dir, lastRoamDirection) < -0.99f)
                // Dot ~ -1: direção oposta quase exata (evita movimento "ping-pong").
                continue;

            nextTarget = candidate;
            facing = dir;
            return true;
        }

        nextTarget = transform.position;
        facing = Vector2.zero;
        return false;
    }

    // Coroutine de movimento suave; interrompe se o caminho bloquear.
    // Coroutine de deslocamento suave que interrompe se uma colisão aparecer no caminho.
    private IEnumerator Move(Vector3 targetPos, bool randomNpcMove)
    {
        // Marca estado de deslocamento para pausar decisões concorrentes no Update.
        isMoving = true;
        bool blocked = false;

        while (Vector2.Distance(transform.position, targetPos) > 0.0001f)
        {
            Vector3 nextPos = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
            if (!IsWalkable(nextPos))
            {
                // Colisão detectada no caminho: interrompe deslocamento atual.
                blocked = true;
                break;
            }
            transform.position = nextPos;
            // yield return null: continua no próximo frame, mantendo animação fluida.
            yield return null;
        }

        // Sai do estado de deslocamento ao terminar (com sucesso ou bloqueio).
        isMoving = false;
        if (blocked)
        {
            // Tenta novamente em pouco tempo, em vez de travar.
            if (!randomNpcMove)
            {
                directedRetryCount++;
                if (phase == NpcPhase.GoingToArea && directedRetryCount >= 20)
                {
                    // Reamostra alvo para escapar de bloqueio recorrente no trajeto dirigido.
                    phaseTarget = RandomPointInArea(area);
                    directedRetryCount = 0;
                }
            }
            nextMoveTime = Time.time + 0.05f;
            yield break;
        }

        // Garante posição final exata ao concluir o deslocamento.
        transform.position = targetPos;
        if (randomNpcMove)
            // Em roaming, próximo passo respeita janela aleatória de idle.
            ScheduleNextMove();
        else
            // Em movimento dirigido, permite nova decisão imediatamente.
            nextMoveTime = Time.time;
    }
}
