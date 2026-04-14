using UnityEngine;
using System.Collections;

public partial class NPCController
{
    // Move um passo em direção ao alvo, com retries quando houver bloqueio.
    private void ProcessDirectedMovement(Vector3 target, System.Action onReached)
    {
        // No ponto de transição, nunca força entrada se já houver outro NPC ocupando.
        if (phase == NpcPhase.GoingToTransitionPoint && IsOccupiedByOtherNpc(target))
        {
            nextMoveTime = Time.time + 0.15f;
            return;
        }

        if (HasReachedTarget2D(target))
        {
            transform.position = target;
            onReached.Invoke();
            return;
        }

        if ((phase == NpcPhase.GoingToTransitionPoint || phase == NpcPhase.GoingSlightlyRightAtTransition) &&
            Vector2.Distance(transform.position, target) <= 0.25f)
        {
            transform.position = target;
            onReached.Invoke();
            return;
        }

        bool allowDetour = phase != NpcPhase.GoingToTransitionPoint &&
                           phase != NpcPhase.GoingSlightlyRightAtTransition;
        if (TryGetStepTowardTarget(target, out Vector3 nextStep, out Vector2 facing, allowDetour))
        {
            if (hasPreviousDirectedTile && Vector2.Distance(nextStep, previousDirectedTile) < 0.01f)
            {
                directedRetryCount++;
                nextMoveTime = Time.time + 0.05f;
                if ((phase == NpcPhase.GoingToTransitionPoint || phase == NpcPhase.GoingSlightlyRightAtTransition) &&
                    directedRetryCount >= 20)
                {
                    transform.position = target;
                    onReached.Invoke();
                }
                return;
            }

            directedRetryCount = 0;
            previousDirectedTile = transform.position;
            hasPreviousDirectedTile = true;
            animator.SetFloat("moveX", facing.x);
            animator.SetFloat("moveY", facing.y);
            StartCoroutine(Move(nextStep, false));
        }
        else
        {
            directedRetryCount++;
            if (phase == NpcPhase.GoingToArea && directedRetryCount >= 20)
            {
                phaseTarget = RandomPointInArea(area);
                directedRetryCount = 0;
            }
            else if ((phase == NpcPhase.GoingToTransitionPoint || phase == NpcPhase.GoingSlightlyRightAtTransition) &&
                     directedRetryCount >= 20)
            {
                // Em transição, espera liberar em vez de sobrepor NPCs.
                nextMoveTime = Time.time + 0.15f;
                return;
            }
            else if (phase == NpcPhase.GoingToOutsideArea && directedRetryCount >= 40)
            {
                phaseTarget = RandomPointInArea(outsideArea);
                directedRetryCount = 0;
            }
            nextMoveTime = Time.time + 0.05f;
        }
    }

    // Executa um passo aleatório de roaming dentro da área informada.
    private void ProcessNpcMoveInArea(MoveArea currentArea)
    {
        if (!TryGetRandomAreaStep(currentArea, out Vector3 target, out Vector2 facing))
        {
            roamNoMoveCount++;
            if (roamNoMoveCount >= 10)
            {
                hasPreviousRoamTile = false;
                hasLastRoamDirection = false;
                roamNoMoveCount = 0;
            }
            nextMoveTime = Time.time + 0.05f;
            return;
        }

        roamNoMoveCount = 0;
        animator.SetFloat("moveX", facing.x);
        animator.SetFloat("moveY", facing.y);
        previousRoamTile = transform.position;
        hasPreviousRoamTile = true;
        lastRoamDirection = facing;
        hasLastRoamDirection = true;
        StartCoroutine(Move(target, true));
    }

    // Escolhe um passo cardinal válido evitando retorno imediato.
    private bool TryGetRandomAreaStep(MoveArea currentArea, out Vector3 nextTarget, out Vector2 facing)
    {
        Vector2[] directions = new Vector2[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        int startIndex = Random.Range(0, directions.Length);

        for (int i = 0; i < directions.Length; i++)
        {
            Vector2 dir = directions[(startIndex + i) % directions.Length];
            Vector3 candidate = transform.position + new Vector3(dir.x, dir.y, 0f);
            if (!currentArea.Contains(candidate) || !IsWalkable(candidate))
                continue;
            if (hasPreviousRoamTile && Vector2.Distance(candidate, previousRoamTile) < 0.01f)
                continue;
            if (hasLastRoamDirection && Vector2.Dot(dir, lastRoamDirection) < -0.99f)
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
    private IEnumerator Move(Vector3 targetPos, bool randomNpcMove)
    {
        isMoving = true;
        bool blocked = false;

        while (Vector2.Distance(transform.position, targetPos) > 0.0001f)
        {
            Vector3 nextPos = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
            if (!IsWalkable(nextPos))
            {
                blocked = true;
                break;
            }
            transform.position = nextPos;
            yield return null;
        }

        isMoving = false;
        if (blocked)
        {
            // Tenta novamente em pouco tempo, em vez de travar.
            if (!randomNpcMove)
            {
                directedRetryCount++;
                if (phase == NpcPhase.GoingToArea && directedRetryCount >= 20)
                {
                    phaseTarget = RandomPointInArea(area);
                    directedRetryCount = 0;
                }
            }
            nextMoveTime = Time.time + 0.05f;
            yield break;
        }

        transform.position = targetPos;
        if (randomNpcMove)
            ScheduleNextMove();
        else
            nextMoveTime = Time.time;
    }
}
