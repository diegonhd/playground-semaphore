// Regras de colisão do NPC com cenário/obstáculos.
using UnityEngine;

public partial class NPCController
{
    // Verifica se o destino está livre para movimento (ignorando colisão entre NPCs).
    private bool IsWalkable(Vector3 targetPos)
    {
        // Consulta colisores do cenário no ponto alvo usando a layer configurada.
        Collider2D[] hits = Physics2D.OverlapCircleAll((Vector2)targetPos, collisionCheckRadius, worldCollisionLayer);
        foreach (var hit in hits)
        {
            if (hit != null && hit.gameObject != gameObject)
                // Qualquer obstáculo diferente de si mesmo invalida o passo.
                return false;
        }
        // Sem bloqueio encontrado: posição é caminhável.
        return true;
    }
}
