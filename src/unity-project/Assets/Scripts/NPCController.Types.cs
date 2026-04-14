using UnityEngine;

public partial class NPCController
{
    // Estados do ciclo de vida do NPC.
    private enum NpcPhase
    {
        GoingToArea,
        RoamingAreaTB,
        GoingToTransitionPoint,
        GoingSlightlyRightAtTransition,
        WaitingAfterRightMove,
        GoingToOutsideArea,
        RoamingOutsideTD,
        Done
    }

    // Estrutura de limites retangulares para áreas de movimentação.
    private struct MoveArea
    {
        public float minX;
        public float maxX;
        public float minY;
        public float maxY;

        public MoveArea(float x1, float x2, float y1, float y2)
        {
            minX = Mathf.Min(x1, x2);
            maxX = Mathf.Max(x1, x2);
            minY = Mathf.Min(y1, y2);
            maxY = Mathf.Max(y1, y2);
        }

        public bool Contains(Vector3 position)
        {
            return position.x >= minX && position.x <= maxX && position.y >= minY && position.y <= maxY;
        }
    }
}
