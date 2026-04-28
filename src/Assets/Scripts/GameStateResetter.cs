// Script purpose:
// Reseta o estado do jogo em runtime: para as threads ativas, limpa estado estático e recarrega a cena atual.
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameStateResetter : MonoBehaviour
{
    [SerializeField] private Button resetButton;
    [SerializeField] private bool reloadCurrentScene = true;

    private bool isResetting;

    private void Awake()
    {
        if (resetButton != null)
            resetButton.onClick.AddListener(ResetGameState); // Adiciona listener para o botão de resetar 
            // o estado do jogo.
    }

    private void OnDestroy()
    {
        if (resetButton != null)
            resetButton.onClick.RemoveListener(ResetGameState); // Remove listener para evitar chamadas 
            // após destruição do objeto.
    }

    // Pode ser ligado ao OnClick de um botão no Inspector.
    public void ResetGameState() // Método público para iniciar o reset do estado do jogo.
    {
        if (!isResetting)
            StartCoroutine(ResetRoutine()); 
    }

    private IEnumerator ResetRoutine() // Rotina de reset que desativa NPCs, 
    // espera um frame e limpa estado estático.
    {
        isResetting = true;
        Time.timeScale = 1f; // atribui 1 frame para que as threads possam processar o 
        // cancelamento e encerrar suas execuções de forma cooperativa

        NPCController[] activeChildren = FindObjectsOfType<NPCController>();
        // Encontra todos os NPCs ativos na cena para desativá-los antes de resetar o estado do jogo
        for (int i = 0; i < activeChildren.Length; i++)
        {
            NPCController child = activeChildren[i];
            if (child != null && child.gameObject.activeSelf)
                child.gameObject.SetActive(false);
        }

        // Aguarda um frame para completar OnDisable/Dispose das threads antes de limpar semáforos.
        yield return null;

        NPCController.ResetRuntimeStateNow();
        ChildBehaviour.ResetRuntimeStateNow();
        BasketSemaphoreCore.ResetRuntimeStateNow();

        if (reloadCurrentScene)
        {
            Scene currentScene = SceneManager.GetActiveScene();
            if (currentScene.IsValid())
                SceneManager.LoadScene(currentScene.buildIndex);
        }

        isResetting = false;
    }
}
