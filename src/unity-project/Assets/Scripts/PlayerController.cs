using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3f;
    private bool isMoving;
    private Vector2 input;
    private Animator animator;
    private bool hasIsMovingParam; // Cache whether animator has 'isMoving' bool parameter.
     // Variável para armazenar o componente Animator do jogador.
    public LayerMask solidObjectsLayer; // Variável para armazenar a LayerMask dos objetos sólidos, usada para detectar colisões.
    private void Awake()
    {
        animator = GetComponent<Animator>(); // Obtém o componente Animator do jogador e o armazena na variável animator.
        if (animator == null) Debug.LogError("Animator não encontrado em " + gameObject.name);
        else {
            // Verifica se o Animator possui o parâmetro 'isMoving' antes de atualizá-lo em Update()
            foreach (var p in animator.parameters)
            {
                if (p.type == UnityEngine.AnimatorControllerParameterType.Bool && p.name == "isMoving")
                {
                    hasIsMovingParam = true;
                    break;
                }
            }
        }
    }
    

    private void Update() //  Essa função roda a cada frame.
    {
        if(!isMoving) // Se o jogador não estiver se movendo, então ele pode receber input do teclado.
        {   
            input.x = Input.GetAxisRaw("Horizontal"); 
            input.y = Input.GetAxisRaw("Vertical");
            // Verificam se o jogador está pressionando alguma key

            Debug.Log($"input: {input}");
            
            if(input.x != 0) input.y = 0;
            // Se o jogador estiver pressionando uma key horizontal, então o input vertical é zerado para evitar movimento diagonal.
            if (input != Vector2.zero) // Se o input for diferente de zero, então...
            {   
                animator.SetFloat("moveX", input.x);
                animator.SetFloat("moveY", input.y);
                var targetPos = transform.position; // Armazena a posição atual do jogador em uma variável.
                targetPos.x += input.x; 
                targetPos.y += input.y;
                // Incrementa o valor do input ao targetPos, para calcular a nova posição do jogador.
                if(IsWalkable(targetPos))
                    StartCoroutine(Move(targetPos)); // Inicia a Coroutine Move, passando a nova posição como argumento para que o jogador se mova suavemente para lá.
            }
        }
        animator.SetBool("isMoving", isMoving); // Atualiza o parâmetro "isMoving" do Animator com o valor da variável isMoving, para controlar as animações de movimento do jogador.

    }

    IEnumerator Move(Vector3 targetPos) { 
        // Essa função é uma Coroutine, que permite que o jogador se mova suavemente para a nova posição ao invés de teletransportar instantaneamente.
        isMoving = true; // Define isMoving como true, para impedir que o jogador receba novos inputs enquanto estiver se movendo.
        while((targetPos - transform.position).sqrMagnitude > 0.0001f)
        // Enquanto a distância entre a posição atual do jogador e a posição alvo for maior que um valor muito pequeno (Mathf.Epsilon), o jogador continuará se movendo em direção ao targetPos.
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
            // Move o jogador em direção ao targetPos a uma velocidade determinada por moveSpeed, multiplicada pelo tempo entre os frames (Time.deltaTime) para garantir um movimento suave e consistente.
            yield return null; // Aguarda o próximo frame antes de continuar a execução da Coroutine, permitindo que o movimento seja atualizado a cada frame.
        }
        transform.position = targetPos; // Garante que o jogador esteja exatamente na posição alvo ao final do movimento.
        isMoving = false; // Define isMoving como false, permitindo que o jogador receba novos inputs para se mover novamente.
    }

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

