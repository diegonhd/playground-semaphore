// Script purpose:
// Controla o cesto via UI: permite editar K por input field e monitora bolas/capacidade em runtime.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BasketControl : MonoBehaviour
{
    [SerializeField] private TMP_InputField kInputField;
    [SerializeField] private Button applyKButton;
    [SerializeField] private TMP_Text basketMonitorText;
    [SerializeField] private TMP_Text kLogText;
    [SerializeField] private ScrollRect kLogScrollRect;
    [SerializeField] private float refreshIntervalSeconds = 0.2f;
    [SerializeField] private int maxKLogLines = 20;
    [SerializeField] private bool autoScrollKLogToBottom = true;
    [SerializeField] private int initialK = 4;

    private float nextRefreshTime;
    private string lastApplyFeedback = string.Empty;
    private readonly Queue<string> kLogLines = new Queue<string>();
    private readonly StringBuilder kLogBuilder = new StringBuilder(2048);

    private void Awake()
    {
        initialK = Mathf.Clamp(initialK, 1, BasketSemaphoreCore.MaxCapacity);
        BasketSemaphoreCore.EnsureInitialized(initialK);

        if (kInputField != null)
            kInputField.text = BasketSemaphoreCore.Capacity.ToString(CultureInfo.InvariantCulture);

        if (applyKButton != null)
            applyKButton.onClick.AddListener(ApplyKFromInputField);

        if (kLogScrollRect == null && kLogText != null)
            kLogScrollRect = kLogText.GetComponentInParent<ScrollRect>(true);
    }

    private void OnDestroy()
    {
        if (applyKButton != null)
            applyKButton.onClick.RemoveListener(ApplyKFromInputField);
    }

    private void OnValidate()
    {
        refreshIntervalSeconds = Mathf.Max(0.05f, refreshIntervalSeconds);
        maxKLogLines = Mathf.Max(1, maxKLogLines);
        initialK = Mathf.Clamp(initialK, 1, BasketSemaphoreCore.MaxCapacity);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
        RefreshMonitorText();
    }

    public void ApplyKFromInputField()
    {
        if (kInputField == null)
        {
            lastApplyFeedback = "Campo de K não configurado.";
            AppendKLog(lastApplyFeedback);
            return;
        }

        string raw = kInputField.text;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out int parsedK) &&
            !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedK))
        {
            lastApplyFeedback = $"K inválido: \"{raw}\".";
            AppendKLog(lastApplyFeedback);
            return;
        }

        if (BasketSemaphoreCore.TrySetCapacity(parsedK, out string reason))
        {
            lastApplyFeedback = reason;
            kInputField.text = BasketSemaphoreCore.Capacity.ToString(CultureInfo.InvariantCulture);
            AppendKLog($"Solicitação K={parsedK}: {reason}");
            return;
        }

        lastApplyFeedback = reason;
        AppendKLog($"Solicitação K={parsedK}: {reason}");
    }

    private void RefreshMonitorText()
    {
        if (basketMonitorText == null)
            return;

        int currentCapacity = BasketSemaphoreCore.Capacity;
        int basketCount = BasketSemaphoreCore.BasketCount;

        basketMonitorText.text =
            $"K atual: {currentCapacity}\n" +
            $"Bolas no cesto: {basketCount}\n" +
            $"Total no sistema (M): {BasketSemaphoreCore.TotalSystemBalls}\n" +
            (!string.IsNullOrWhiteSpace(lastApplyFeedback) ? $"Última ação: {lastApplyFeedback}" : string.Empty);
    }

    private void AppendKLog(string message)
    {
        string stamp = DateTime.Now.ToString("HH:mm:ss");
        kLogLines.Enqueue($"[{stamp}] {message}");
        while (kLogLines.Count > maxKLogLines)
            kLogLines.Dequeue();

        RefreshKLogText();
    }

    private void RefreshKLogText()
    {
        if (kLogText == null)
            return;

        kLogBuilder.Clear();
        foreach (string line in kLogLines)
            kLogBuilder.AppendLine(line);

        kLogText.text = kLogBuilder.Length > 0 ? kLogBuilder.ToString() : "Log de K";

        if (autoScrollKLogToBottom && kLogScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            kLogScrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
