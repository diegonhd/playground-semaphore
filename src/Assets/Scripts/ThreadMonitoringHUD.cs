// Script purpose:
// HUD de monitoramento em runtime: mostra estado das crianças/cesto e log compacto de eventos.
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ThreadMonitoringHUD : MonoBehaviour
{
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text logText;
    [SerializeField] private ScrollRect statusScrollRect;
    [SerializeField] private ScrollRect logScrollRect;
    [SerializeField] private int maxLogLines = 20;
    [SerializeField] private int maxLogEntryChars = 110;
    [SerializeField] private float refreshIntervalSeconds = 0.15f;
    [SerializeField] private bool autoScrollLogToBottom = true;

    private readonly Queue<string> logLines = new Queue<string>();
    // StringBuilder reduz alocação/GC em comparação com concatenação frequente de strings.
    private readonly StringBuilder statusBuilder = new StringBuilder(2048);
    private readonly StringBuilder logBuilder = new StringBuilder(4096);
    private float nextRefreshTime;
    private bool uiLayoutNormalized;

    // Resolve referências e normaliza layout assim que o HUD entra em cena.
    private void Awake()
    {
        CacheReferences();
        NormalizeUiLayout();
    }

    // Ajuda o Editor a preencher referências padrão ao adicionar o componente.
    private void Reset()
    {
        CacheReferences();
    }

    // Limita valores do Inspector e tenta re-resolver referências no Editor.
    private void OnValidate()
    {
        // OnValidate roda no editor e mantém limites seguros para campos serializados.
        maxLogLines = Mathf.Max(1, maxLogLines);
        maxLogEntryChars = Mathf.Max(24, maxLogEntryChars);
        refreshIntervalSeconds = Mathf.Max(0.01f, refreshIntervalSeconds);
        CacheReferences();
    }

    // Inscreve o HUD no evento de logs das crianças.
    private void OnEnable()
    {
        NPCController.ChildEventLogged += HandleChildEventLogged;
    }

    // Remove a inscrição do evento para evitar handlers órfãos.
    private void OnDisable()
    {
        NPCController.ChildEventLogged -= HandleChildEventLogged;
    }

    // Atualiza os textos em intervalo fixo para reduzir custo por frame.
    private void Update()
    {
        if (statusText == null || logText == null)
            CacheReferences();

        if (statusText == null || logText == null)
            return;

        if (!uiLayoutNormalized)
            NormalizeUiLayout();

        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
        RefreshStatus();
        RefreshLog();
    }

    // Recebe logs das crianças, compacta e mantém apenas os mais recentes.
    private void HandleChildEventLogged(string message)
    {
        string stamp = DateTime.Now.ToString("HH:mm:ss");
        string compactMessage = CompactLogMessage(message);
        if (string.IsNullOrWhiteSpace(compactMessage))
            return;

        logLines.Enqueue($"[{stamp}] {compactMessage}");
        while (logLines.Count > maxLogLines)
            logLines.Dequeue();
    }

    // Reescreve o painel de status com a visão consolidada da simulação.
    private void RefreshStatus()
    {
        NPCController[] children = NPCController.GetActiveChildrenSnapshot();
        Array.Sort(children, (left, right) => left.ThreadId.CompareTo(right.ThreadId));

        int logicalCores = Math.Max(1, Environment.ProcessorCount);
        int executingCount = 0;
        int readyCount = 0;
        int blockedCount = 0;

        for (int i = 0; i < children.Length; i++)
        {
            switch (children[i].CurrentSchedulingState)
            {
                case NPCController.ChildThreadSchedulingState.Blocked:
                    blockedCount++;
                    break;
                case NPCController.ChildThreadSchedulingState.Ready:
                    readyCount++;
                    break;
                default:
                    executingCount++;
                    break;
            }
        }

        double expectedCpuPercent = (Mathf.Min(executingCount, logicalCores) * 100d) / logicalCores;

        statusBuilder.Clear();
        statusBuilder.Append("Exec: ")
            .Append(executingCount)
            .Append(" | Prontas: ")
            .Append(readyCount)
            .Append(" | Bloqueadas: ")
            .Append(blockedCount)
            .Append(" | CPU prevista: ")
            .Append(expectedCpuPercent.ToString("0.0"))
            .Append('%')
            .AppendLine()
            .AppendLine();

        foreach (NPCController child in children)
        {
            string statusLine = child.CurrentThreadStatusText;
            if (string.IsNullOrWhiteSpace(statusLine))
                continue;

            statusBuilder.Append('[')
                .Append(child.ChildIdentifier)
                .Append("] ")
                .AppendLine(statusLine);
        }

        statusText.text = statusBuilder.ToString();
        RefreshScroll(statusText, statusScrollRect, false, true, false);
    }

    // Reescreve o painel de log.
    private void RefreshLog()
    {
        if (logLines.Count == 0)
        {
            logText.text = "Log dos eventos";
            RefreshScroll(logText, logScrollRect, autoScrollLogToBottom, false, true);
            return;
        }

        logBuilder.Clear();
        foreach (string line in logLines)
            logBuilder.AppendLine(line);

        logText.text = logBuilder.ToString();
        RefreshScroll(logText, logScrollRect, autoScrollLogToBottom, false, true);
    }

    // Procura textos e ScrollRects dentro do HUD, inclusive se estiverem inativos.
    private void CacheReferences()
    {
        // includeInactive=true ("true") também procura referências em objetos desativados no Canvas.
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text candidate = texts[i];
            if (candidate == null)
                continue;

            if (statusText == null && candidate.name == "StatusText")
                statusText = candidate;
            else if (logText == null && candidate.name == "LogText")
                logText = candidate;
        }

        if (statusScrollRect == null && statusText != null)
            statusScrollRect = statusText.GetComponentInParent<ScrollRect>(true);

        if (logScrollRect == null && logText != null)
            logScrollRect = logText.GetComponentInParent<ScrollRect>(true);

        ScrollRect[] scrollRects = GetComponentsInChildren<ScrollRect>(true);
        for (int i = 0; i < scrollRects.Length; i++)
        {
            ScrollRect scrollRect = scrollRects[i];
            if (scrollRect == null)
                continue;

            if (statusScrollRect == null && scrollRect.name.IndexOf("status", StringComparison.OrdinalIgnoreCase) >= 0)
                statusScrollRect = scrollRect;
            else if (logScrollRect == null && scrollRect.name.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0)
                logScrollRect = scrollRect;
        }
    }

    // Corrige anchors/pivots para tornar o layout do HUD previsível.
    private void NormalizeUiLayout()
    {
        if (statusScrollRect != null)
            NormalizeScrollRectLayout(statusScrollRect);

        if (logScrollRect != null)
            NormalizeScrollRectLayout(logScrollRect);

        uiLayoutNormalized = statusText != null && logText != null;
    }

    // Ajusta viewport/content para que o ScrollRect se comporte corretamente.
    private static void NormalizeScrollRectLayout(ScrollRect scrollRect)
    {
        if (scrollRect == null)
            return;

        if (scrollRect.viewport != null)
        {
            RectTransform viewport = scrollRect.viewport;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.anchoredPosition = Vector2.zero;
            viewport.sizeDelta = new Vector2(
                scrollRect.verticalScrollbar != null ? -20f : 0f,
                scrollRect.horizontalScrollbar != null ? -20f : 0f
            );
        }

        if (scrollRect.content != null)
        {
            RectTransform content = scrollRect.content;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(Mathf.Max(0f, content.sizeDelta.x), Mathf.Max(300f, content.sizeDelta.y));
        }
    }

    // Torna o texto visível e ancorado ao topo do conteúdo.


    // Reduz mensagens longas/verbosas antes de exibir no painel.
    private string CompactLogMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "-";

        string compact = message;

        int phaseIndex = compact.IndexOf(" | phase=", StringComparison.OrdinalIgnoreCase);
        if (phaseIndex >= 0)
            compact = compact.Substring(0, phaseIndex);

        compact = compact
            .Replace("Aguardando que outra criança coloque uma bola no cesto", "Aguardando bola")
            .Replace("Aguardando que o cesto tenha espaço para que ela coloque sua bola", "Aguardando espaco")
            .Replace("Brincando com a bola", "Brincando")
            .Replace("Descansando", "Descanso")
            .Replace("status: ", "")
            .Replace("dados: ", "");

        if (compact.Length > maxLogEntryChars)
            compact = compact.Substring(0, maxLogEntryChars - 1) + "…";

        return compact;
    }

    // Reaplica wrapping/overflow e força a atualização do viewport.
    private static void RefreshScroll(TMP_Text text, ScrollRect scrollRect, bool snapToBottom, bool allowHorizontal, bool wrapText)
    {
        if (text == null)
            return;

        text.enableWordWrapping = wrapText;
        text.overflowMode = TextOverflowModes.Overflow;

        if (scrollRect == null || scrollRect.content == null)
            return;

        scrollRect.horizontal = allowHorizontal;
        scrollRect.vertical = true;

        // Mantém o conteúdo visual consistente após atualização de texto no mesmo frame.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
        if (allowHorizontal)
            scrollRect.horizontalNormalizedPosition = 0f;
        scrollRect.verticalNormalizedPosition = snapToBottom ? 0f : 1f;
    }

}
