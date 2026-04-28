// Controla movimento grid-like do jogador via teclado e animações (sem atravessar obstáculos).
using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3f;
    private bool isMoving;
    private Vector2 input;
    private Animator animator;
    public LayerMask solidObjectsLayer;
    // Inicializa o Animator do jogador e reporta erro se estiver faltando.
    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null) Debug.LogError("Animator não encontrado em " + gameObject.name);
    }
    

    // Lê input de direção, trava movimento diagonal e dispara a coroutine de passo.
    private void Update()
    {
        if (!isMoving)
        {
            // GetAxisRaw retorna -1/0/1 sem suavização (ideal para movimento em grade).
            input.x = Input.GetAxisRaw("Horizontal"); 
            input.y = Input.GetAxisRaw("Vertical");
            
            if(input.x != 0) input.y = 0;
            if (input != Vector2.zero)
            {
                animator.SetFloat("moveX", input.x);
                animator.SetFloat("moveY", input.y);
                var targetPos = transform.position;
                targetPos.x += input.x; 
                targetPos.y += input.y;
                if(IsWalkable(targetPos))
                    StartCoroutine(Move(targetPos));
            }
        }
        animator.SetBool("isMoving", isMoving);
    }

    // Move o jogador de forma suave em grade, sem bloquear o frame principal.
    private IEnumerator Move(Vector3 targetPos)
    {
        isMoving = true;
        // sqrMagnitude evita raiz quadrada por frame (mais barato que magnitude).
        while((targetPos - transform.position).sqrMagnitude > 0.0001f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = targetPos;
        isMoving = false;
    }

    // Bloqueia o passo se houver qualquer collider sólido no destino.
    private bool IsWalkable(Vector3 targetPos)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll((Vector2)targetPos, 0.1f, solidObjectsLayer);
        foreach (var hit in hits)
        {
            if (hit != null && hit.gameObject != gameObject)
                return false;
        }
        return true;
    }
}

