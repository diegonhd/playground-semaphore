using UnityEngine;

public partial class NPCController
{
    // Verifica se o NPC já alcançou o alvo usando apenas X e Y.
    private bool HasReachedTarget2D(Vector3 target)
    {
        return Vector2.Distance(transform.position, target) < 0.05f;
    }

    // Tenta passo direto por eixo; se permitido, tenta desvio.
    private bool TryGetStepTowardTarget(Vector3 target, out Vector3 nextTarget, out Vector2 facing, bool allowDetour = true)
    {
        Vector3 current = transform.position;
        float dx = target.x - current.x;
        float dy = target.y - current.y;

        bool xFirst = Mathf.Abs(dx) >= Mathf.Abs(dy);
        if (TryBuildAxisStep(current, dx, dy, xFirst, out nextTarget, out facing))
            return true;

        if (TryBuildAxisStep(current, dx, dy, !xFirst, out nextTarget, out facing))
            return true;

        if (allowDetour && TryGetDetourStep(current, target, out nextTarget, out facing))
            return true;

        nextTarget = current;
        facing = Vector2.zero;
        return false;
    }

    // Escolhe o melhor vizinho cardinal que aproxime do alvo.
    private bool TryGetDetourStep(Vector3 current, Vector3 target, out Vector3 nextTarget, out Vector2 facing)
    {
        Vector2[] directions = new Vector2[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        float bestDistance = float.MaxValue;
        Vector3 bestTarget = current;
        Vector2 bestFacing = Vector2.zero;
        bool found = false;

        for (int i = 0; i < directions.Length; i++)
        {
            Vector2 dir = directions[i];
            Vector3 candidate = current + new Vector3(dir.x, dir.y, 1f - current.z);
            if (!IsWalkable(candidate))
                continue;

            float dist = Vector2.Distance(candidate, target);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestTarget = candidate;
                bestFacing = dir;
                found = true;
            }
        }

        nextTarget = bestTarget;
        facing = bestFacing;
        return found;
    }

    // Monta passo de até 1 tile em apenas um eixo (X ou Y).
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
            nextTarget = new Vector3(current.x, current.y + stepY, 1f);
            facing = new Vector2(0f, Mathf.Sign(stepY));
        }

        return IsWalkable(nextTarget);
    }

    // Sorteia ponto aleatório dentro de uma área retangular.
    private Vector3 RandomPointInArea(MoveArea moveArea)
    {
        return new Vector3(
            Mathf.Round(Random.Range(moveArea.minX, moveArea.maxX)),
            Mathf.Round(Random.Range(moveArea.minY, moveArea.maxY)),
            1f
        );
    }

    // Agenda a próxima tentativa de movimento com atraso aleatório.
    private void ScheduleNextMove()
    {
        nextMoveTime = Time.time + Random.Range(minIdleTime, maxIdleTime);
    }

}
