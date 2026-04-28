// Define enums e tipos auxiliares compartilhados entre os arquivos parciais do NPCController.
using UnityEngine;

public partial class NPCController
{
    // Estados do ciclo de vida do NPC.
    private enum NpcPhase
    {
        GoingToArea,
        RoamingAreaTB,
        GoingToTransitionPoint,
        WaitingForBasketTurn,
        GoingToRestSpotTD,
        RestingAtSpotTD
    }

    private enum BasketQueueType
    {
        None,
        PutBall,
        TakeBall
    }

    public enum ChildThreadStatus
    {
        PlayingWithBall,
        WaitingBallInBasket,
        WaitingBasketSpace,
        Resting
    }

    public enum ChildThreadSchedulingState
    {
        Running,
        Ready,
        Blocked
    }

    // Estrutura de limites retangulares para áreas de movimentação.
    private struct MoveArea
    {
        public float minX;
        public float maxX;
        public float minY;
        public float maxY;

        // Constrói a área já normalizada (min/max) independente da ordem dos pontos recebidos.
        // Normaliza os limites da área, aceitando os pontos em qualquer ordem.
        public MoveArea(float x1, float x2, float y1, float y2)
        {
            minX = Mathf.Min(x1, x2);
            maxX = Mathf.Max(x1, x2);
            minY = Mathf.Min(y1, y2);
            maxY = Mathf.Max(y1, y2);
        }

        // Teste de ponto dentro do retângulo 2D.
        // Verifica se a posição está dentro do retângulo 2D.
        public bool Contains(Vector3 position)
        {
            return position.x >= minX && position.x <= maxX && position.y >= minY && position.y <= maxY;
        }
    }
}
