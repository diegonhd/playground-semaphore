using UnityEngine;

public partial class NPCController
{
    // Valida colisão com cenário e com outros NPCs antes de mover.
    private bool IsWalkable(Vector3 targetPos)
    {
        LayerMask combinedMask = worldCollisionLayer | npcCollisionLayer;
        Collider2D[] hits = Physics2D.OverlapCircleAll((Vector2)targetPos, collisionCheckRadius, combinedMask);
        foreach (var hit in hits)
        {
            if (hit != null && hit.gameObject != gameObject)
                return false;
        }
        return true;
    }

    // Verifica apenas ocupação por outro NPC (resolução de disputa de alvo).
    private bool IsOccupiedByOtherNpc(Vector3 targetPos)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll((Vector2)targetPos, npcOccupancyCheckRadius, npcCollisionLayer);
        foreach (var hit in hits)
        {
            if (hit != null && hit.gameObject != gameObject)
                return true;
        }
        return false;
    }
}
