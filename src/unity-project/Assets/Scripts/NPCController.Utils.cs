// Helpers de navegação (escolha de passo, desvios, utilidades geométricas e agendamento de movimento).
using UnityEngine;

public partial class NPCController
{
    // Verifica se o NPC já alcançou o alvo usando apenas X e Y.
    // Mede proximidade no plano XY ignorando Z.
    private bool HasReachedTarget2D(Vector3 target)
    {
        // Limiar pequeno evita oscilação infinita por erro de ponto flutuante.
        return Vector2.Distance(transform.position, target) < 0.05f;
    }

    // Tenta passo direto por eixo; se permitido, tenta desvio.
    // Escolhe o próximo passo rumo ao alvo, evitando voltar imediatamente para o tile anterior.
    private bool TryGetStepTowardTarget(Vector3 target, out Vector3 nextTarget, out Vector2 facing, bool allowDetour = true)
    {
        Vector3 current = transform.position;
        float dx = target.x - current.x;
        float dy = target.y - current.y;

        Vector3[] candidateTargets = new Vector3[6];
        Vector2[] candidateFacings = new Vector2[6];
        int candidateCount = 0;

        // Função local C#: encapsula deduplicação de candidatos sem expor método extra na classe.
        void AddCandidate(Vector3 candidateTarget, Vector2 candidateFacing)
        {
            for (int i = 0; i < candidateCount; i++)
            {
                if (Vector2.Distance(candidateTargets[i], candidateTarget) < 0.01f)
                    return;
            }

            candidateTargets[candidateCount] = candidateTarget;
            candidateFacings[candidateCount] = candidateFacing;
            candidateCount++;
        }

        bool xFirst = Mathf.Abs(dx) >= Mathf.Abs(dy);
        // Prioriza eixo com maior distância para reduzir caminho até o alvo.
        if (TryBuildAxisStep(current, dx, dy, xFirst, out Vector3 firstAxisStep, out Vector2 firstAxisFacing))
            AddCandidate(firstAxisStep, firstAxisFacing);

        // Também testa eixo alternativo para contornar bloqueios simples.
        if (TryBuildAxisStep(current, dx, dy, !xFirst, out Vector3 secondAxisStep, out Vector2 secondAxisFacing))
            AddCandidate(secondAxisStep, secondAxisFacing);

        if (allowDetour)
        {
            Vector2[] directions = new Vector2[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2 dir = directions[i];
                Vector3 candidate = current + new Vector3(dir.x, dir.y, 1f - current.z);
                if (!IsWalkable(candidate))
                    continue;

                // Em modo detour, inclui passos cardinais válidos adicionais.
                AddCandidate(candidate, dir);
            }
        }

        if (candidateCount == 0)
        {
            nextTarget = current;
            facing = Vector2.zero;
            return false;
        }

        int bestIndex = -1;
        float bestDistance = float.MaxValue;
        int fallbackBacktrackIndex = -1;
        float fallbackBacktrackDistance = float.MaxValue;

        for (int i = 0; i < candidateCount; i++)
        {
            float distanceToTarget = Vector2.Distance(candidateTargets[i], target);
            bool isImmediateBacktrack = hasPreviousDirectedTile &&
                                        Vector2.Distance(candidateTargets[i], previousDirectedTile) < 0.01f;
            if (isImmediateBacktrack)
            {
                // Guarda melhor opção de fallback caso só exista retorno imediato possível.
                if (distanceToTarget < fallbackBacktrackDistance)
                {
                    fallbackBacktrackDistance = distanceToTarget;
                    fallbackBacktrackIndex = i;
                }
                continue;
            }

            // Escolhe candidato com menor distância ao alvo.
            if (distanceToTarget < bestDistance)
            {
                bestDistance = distanceToTarget;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0)
        {
            // Caminho ótimo sem backtrack imediato.
            nextTarget = candidateTargets[bestIndex];
            facing = candidateFacings[bestIndex];
            return true;
        }

        if (fallbackBacktrackIndex >= 0)
        {
            // Fallback seguro quando não há alternativa além de retroceder.
            nextTarget = candidateTargets[fallbackBacktrackIndex];
            facing = candidateFacings[fallbackBacktrackIndex];
            return true;
        }

        nextTarget = current;
        facing = Vector2.zero;
        return false;
    }

    // Monta passo de até 1 tile em apenas um eixo (X ou Y).
    // Monta um passo cardinal em um único eixo.
    private bool TryBuildAxisStep(Vector3 current, float dx, float dy, bool moveX, out Vector3 nextTarget, out Vector2 facing)
    {
        if (moveX)
        {
            if (Mathf.Abs(dx) < 0.001f)
            {
                nextTarget = current;
                facing = Vector2.zero;
                return false;
            }

            float stepX = Mathf.Sign(dx) * Mathf.Min(1f, Mathf.Abs(dx));
            // Move no máximo 1 tile por decisão no eixo X.
            nextTarget = new Vector3(current.x + stepX, current.y, 1f);
            facing = new Vector2(Mathf.Sign(stepX), 0f);
        }
        else
        {
            if (Mathf.Abs(dy) < 0.001f)
            {
                nextTarget = current;
                facing = Vector2.zero;
                return false;
            }

            float stepY = Mathf.Sign(dy) * Mathf.Min(1f, Mathf.Abs(dy));
            // Move no máximo 1 tile por decisão no eixo Y.
            nextTarget = new Vector3(current.x, current.y + stepY, 1f);
            facing = new Vector2(0f, Mathf.Sign(stepY));
        }

        // Só aceita candidato que não colide no cenário.
        return IsWalkable(nextTarget);
    }

    // Sorteia ponto aleatório dentro de uma área retangular.
    // Sorteia um alvo dentro da área TB.
    private Vector3 RandomPointInArea(MoveArea moveArea)
    {
        // Arredonda para grade discreta usada no deslocamento cardinal.
        return new Vector3(
            Mathf.Round(Random.Range(moveArea.minX, moveArea.maxX)),
            Mathf.Round(Random.Range(moveArea.minY, moveArea.maxY)),
            1f
        );
    }

    // Agenda a próxima tentativa de movimento com atraso aleatório.
    // Define quando o NPC tenta se mover novamente.
    private void ScheduleNextMove()
    {
        nextMoveTime = Time.time + Random.Range(minIdleTime, maxIdleTime);
    }

}
