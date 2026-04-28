# Analise Minuciosa do Tratamento dos Problemas de Concorrencia

## Objetivo
Este documento descreve, em detalhe, como o sistema trata os problemas de concorrencia do cenario produtor/consumidor no projeto Unity, com base nas funcoes, variaveis e metodos implementados no codigo.

Cada secao contem:
- Problema tratado
- Estrategia adotada
- Referencias (arquivo:linha)
- Trechos reais do codigo (nao apenas citacao de linha)

---

## 1) Exclusao mutua e controle de buffer limitado (cesto)

### Problema
Evitar que duas threads alterem o cesto ao mesmo tempo e impedir overflow/underflow de bolas.

### Estrategia
Uso de tres semaforos:
- `mutex`: protege secao critica
- `empty`: controla vagas livres
- `full`: controla bolas disponiveis

### Referencias
- BasketSemaphoreCore.cs:18
- BasketSemaphoreCore.cs:20
- BasketSemaphoreCore.cs:22
- BasketSemaphoreCore.cs:83
- BasketSemaphoreCore.cs:85
- BasketSemaphoreCore.cs:86
- BasketSemaphoreCore.cs:188
- BasketSemaphoreCore.cs:198
- BasketSemaphoreCore.cs:201
- BasketSemaphoreCore.cs:228
- BasketSemaphoreCore.cs:238
- BasketSemaphoreCore.cs:241

### Trecho real
```csharp
private static SemaphoreSlim mutex;
private static SemaphoreSlim empty;
private static SemaphoreSlim full;

mutex = new SemaphoreSlim(1, 1);
empty = new SemaphoreSlim(capacity, maxDynamicCapacity);
full = new SemaphoreSlim(0, maxDynamicCapacity);

public static void PutBall(CancellationToken cancellationToken)
{
    empty.Wait(cancellationToken);
    mutex.Wait(cancellationToken);
    ...
}

public static void TakeBall(CancellationToken cancellationToken)
{
    full.Wait(cancellationToken);
    mutex.Wait(cancellationToken);
    ...
}
```

---

## 2) Correcao de contadores sob concorrencia

### Problema
Evitar corrida de dados em `basketCount`, `totalSystemBalls` e leituras inconsistentes.

### Estrategia
Uso de `Volatile.Read`, `Interlocked.Increment` e `Interlocked.CompareExchange`.

### Referencias
- BasketSemaphoreCore.cs:35
- BasketSemaphoreCore.cs:36
- BasketSemaphoreCore.cs:37
- BasketSemaphoreCore.cs:167
- BasketSemaphoreCore.cs:182
- BasketSemaphoreCore.cs:206
- BasketSemaphoreCore.cs:274
- BasketSemaphoreCore.cs:283

### Trecho real
```csharp
public static int BasketCount => Volatile.Read(ref basketCount);
public static int Capacity => Volatile.Read(ref capacity);
public static int TotalSystemBalls => Volatile.Read(ref totalSystemBalls);

if (startsWithBall)
    Interlocked.Increment(ref totalSystemBalls);

if (Interlocked.CompareExchange(ref totalSystemBalls, current - 1, current) == current)
    return;

Interlocked.Increment(ref basketCount);

private static bool TryDecrementNonNegative(ref int value)
{
    while (true)
    {
        int current = Volatile.Read(ref value);
        if (current <= 0)
            return false;

        if (Interlocked.CompareExchange(ref value, current - 1, current) == current)
            return true;
    }
}
```

---

## 3) Cancelamento seguro e encerramento sem thread zumbi

### Problema
Threads bloqueadas em espera podem impedir parada limpa (Play/Stop, reset, troca de cena).

### Estrategia
Cada crianca tem seu `CancellationTokenSource`; cancelamento e propagado para loops e semaforos.

### Referencias
- ChildThreadCore.cs:16
- ChildThreadCore.cs:124
- ChildThreadCore.cs:128
- ChildThreadCore.cs:136
- ChildThreadCore.cs:142
- ChildThreadCore.cs:171
- ChildThreadCore.cs:197
- ChildThreadCore.cs:204
- ChildThreadCore.cs:261
- ChildThreadCore.cs:276
- ChildThreadCore.cs:303

### Trecho real
```csharp
private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

private void Stop()
{
    if (Interlocked.Exchange(ref lifecycleState, 2) == 2)
        return;

    cancellationTokenSource.Cancel();

    if (workerThread != null && workerThread.IsAlive && Thread.CurrentThread != workerThread)
        workerThread.Join(2000);
}

private void RunLoop()
{
    CancellationToken cancellationToken = cancellationTokenSource.Token;
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ...
            BasketSemaphoreCore.PutBall(cancellationToken);
            ...
            BasketSemaphoreCore.TakeBall(cancellationToken);
            ...
        }
    }
    catch (OperationCanceledException)
    {
        // Encerramento esperado.
    }
}
```

---

## 4) Compensacao no `finally` para nao vazar token de semaforo

### Problema
Se houver cancelamento/falha no meio da operacao, sem compensacao ocorre corrupcao da contagem do semaforo.

### Estrategia
Controle com flags `acquired...` e `actionCompleted` para devolver o token correto no `finally`.

### Referencias
- BasketSemaphoreCore.cs:192
- BasketSemaphoreCore.cs:193
- BasketSemaphoreCore.cs:194
- BasketSemaphoreCore.cs:213
- BasketSemaphoreCore.cs:220
- BasketSemaphoreCore.cs:222
- BasketSemaphoreCore.cs:232
- BasketSemaphoreCore.cs:233
- BasketSemaphoreCore.cs:234
- BasketSemaphoreCore.cs:252
- BasketSemaphoreCore.cs:258
- BasketSemaphoreCore.cs:260

### Trecho real
```csharp
bool acquiredEmpty = false;
bool acquiredMutex = false;
bool actionCompleted = false;
...
finally
{
    if (acquiredMutex)
        mutex.Release();

    if (acquiredEmpty)
    {
        if (actionCompleted)
            full.Release();
        else
            empty.Release();
    }
}
```

```csharp
bool acquiredFull = false;
bool acquiredMutex = false;
bool actionCompleted = false;
...
finally
{
    if (acquiredMutex)
        mutex.Release();

    if (acquiredFull)
    {
        if (actionCompleted)
            empty.Release();
        else
            full.Release();
    }
}
```

---

## 5) Impor espacamento global entre acessos ao cesto (2s)

### Problema
Evitar rajada de operacoes put/take consecutivas sem intervalo global.

### Estrategia
Timestamp global da ultima acao concluida + espera calculada com cancelamento.

### Referencias
- BasketSemaphoreCore.cs:11
- BasketSemaphoreCore.cs:30
- BasketSemaphoreCore.cs:205
- BasketSemaphoreCore.cs:244
- BasketSemaphoreCore.cs:300
- BasketSemaphoreCore.cs:305
- BasketSemaphoreCore.cs:310
- BasketSemaphoreCore.cs:311
- BasketSemaphoreCore.cs:317
- BasketSemaphoreCore.cs:319

### Trecho real
```csharp
private const int interActionDelayMilliseconds = 2000;
private static long lastBasketActionCompletedAtMs;

private static void WaitForInterActionGap(CancellationToken cancellationToken)
{
    while (true)
    {
        long elapsed = Environment.TickCount - Volatile.Read(ref lastBasketActionCompletedAtMs);
        int remaining = (int)(interActionDelayMilliseconds - elapsed);
        if (remaining <= 0)
            return;

        cancellationToken.WaitHandle.WaitOne(Math.Max(1, remaining));
        cancellationToken.ThrowIfCancellationRequested();
    }
}

private static void MarkBasketActionCompleted()
{
    Volatile.Write(ref lastBasketActionCompletedAtMs, Environment.TickCount);
}
```

---

## 6) Ajuste de capacidade K em runtime sem quebrar consistencia

### Problema
Alterar K com sistema em execucao pode gerar estado impossivel (ex.: mais bolas no cesto que capacidade).

### Estrategia
`TrySetCapacity` valida limites, trava com `mutex`, ajusta `empty` por delta de vagas e falha com motivo explicito quando nao pode reduzir no instante.

### Referencias
- BasketSemaphoreCore.cs:12
- BasketSemaphoreCore.cs:92
- BasketSemaphoreCore.cs:100
- BasketSemaphoreCore.cs:106
- BasketSemaphoreCore.cs:118
- BasketSemaphoreCore.cs:130
- BasketSemaphoreCore.cs:136
- BasketSemaphoreCore.cs:146
- BasketSemaphoreCore.cs:151
- BasketSemaphoreCore.cs:152

### Trecho real
```csharp
if (newCapacity > maxDynamicCapacity)
{
    reason = $"K máximo suportado é {maxDynamicCapacity}.";
    return false;
}

EnsureReady();
...
int currentBasketCount = basketCount;
if (newCapacity < currentBasketCount)
{
    reason = $"K não pode ser menor que bolas atuais no cesto ({currentBasketCount}).";
    return false;
}

int currentFreeSlots = Math.Max(0, currentCapacity - currentBasketCount);
int newFreeSlots = newCapacity - currentBasketCount;
int deltaFreeSlots = newFreeSlots - currentFreeSlots;

if (deltaFreeSlots > 0)
{
    empty.Release(deltaFreeSlots);
}
else if (deltaFreeSlots < 0)
{
    int slotsToRemove = -deltaFreeSlots;
    for (int i = 0; i < slotsToRemove; i++)
    {
        if (empty.Wait(0))
            continue;

        reason = "Não foi possível reduzir K agora (há puts já reservando vagas). Tente novamente.";
        return false;
    }
}

capacity = newCapacity;
reason = $"K atualizado para {newCapacity}.";
```

---

## 7) Falha rapida quando nucleo nao foi inicializado

### Problema
Chamadas concorrentes antes da inicializacao podem levar a null-reference silenciosa e comportamento indefinido.

### Estrategia
`EnsureReady()` valida invariantes e dispara excecao explicita.

### Referencias
- BasketSemaphoreCore.cs:266
- BasketSemaphoreCore.cs:270
- BasketSemaphoreCore.cs:66
- BasketSemaphoreCore.cs:73

### Trecho real
```csharp
private static void EnsureReady()
{
    if (!initialized || mutex == null || empty == null || full == null)
        throw new InvalidOperationException("BasketSemaphoreCore não inicializado.");
}

public static void EnsureInitialized(int basketCapacity)
{
    ...
    lock (initLock)
    {
        if (initialized)
            return;
        ...
        initialized = true;
    }
}
```

---

## 8) Sincronizacao entre estado visual (Unity) e logica da thread

### Problema
A thread nao pode prosseguir para put/take/Tb/Td sem o NPC estar fisicamente no contexto visual correto.

### Estrategia
Ponte via flags de prontidao:
- Unity calcula readiness por posicao/fila
- Thread bloqueia em `WaitForFlag` ate readiness

### Referencias
- NPCController.cs:286
- NPCController.cs:314
- ChildThreadCore.cs:99
- ChildThreadCore.cs:151
- ChildThreadCore.cs:167
- ChildThreadCore.cs:179
- ChildThreadCore.cs:192
- ChildThreadCore.cs:270

### Trecho real
```csharp
private void PublishVisualReadiness(ChildThreadStatus status)
{
    ...
    bool playAreaReady = status == ChildThreadStatus.PlayingWithBall && area.Contains(transform.position);
    ...
    threadCore.SetVisualReadiness(playAreaReady, restSpotReady, putQueueTurnReady, takeQueueTurnReady);
}
```

```csharp
public void SetVisualReadiness(bool playAreaReady, bool restSpotReady, bool putQueueTurnReady, bool takeQueueTurnReady)
{
    Volatile.Write(ref playAreaReadyFlag, playAreaReady ? 1 : 0);
    Volatile.Write(ref restSpotReadyFlag, restSpotReady ? 1 : 0);
    Volatile.Write(ref putQueueTurnReadyFlag, putQueueTurnReady ? 1 : 0);
    Volatile.Write(ref takeQueueTurnReadyFlag, takeQueueTurnReady ? 1 : 0);
}

private static void WaitForFlag(ref int readinessFlag, CancellationToken cancellationToken)
{
    SpinWait spinner = new SpinWait();
    while (Volatile.Read(ref readinessFlag) == 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        spinner.SpinOnce();
    }
}
```

---

## 9) Consistencia de filas visuais e exclusividade de spot de descanso

### Problema
Sem manutencao explicita de fila/spot, surgem duplicidades, ocupacao invalida e ordem visual incoerente.

### Estrategia
- Gerenciamento explicito de pertencimento de fila
- Prune de referencias invalidas/inativas
- Reserva exclusiva de spots de descanso com round-robin

### Referencias
- NPCController.cs:338
- NPCController.cs:434
- NPCController.cs:468
- NPCController.cs:484
- NPCController.cs:507
- NPCController.cs:566
- NPCController.cs:602
- NPCController.cs:620

### Trecho real
```csharp
private bool TryGetCurrentQueueTarget(ChildThreadStatus status, out Vector3 waitingTarget)
{
    ...
    EnsureVisualQueueMembership(requiredQueue);
    PruneVisualQueue(requiredQueue);
    ...
    int queueIndex = queue.IndexOf(this);
    ...
    assignedQueueSlotIndex = Mathf.Clamp(queueIndex, 0, GetQueueMaxIndex(requiredQueue));
    waitingTarget = GetQueueTarget(requiredQueue, assignedQueueSlotIndex);
    return true;
}

private void LeaveVisualQueue()
{
    putBallVisualQueue.Remove(this);
    takeBallVisualQueue.Remove(this);
    activeVisualQueue = BasketQueueType.None;
    assignedQueueSlotIndex = -1;
}
```

```csharp
private bool TryGetRestSpotTarget(out Vector3 restSpotTarget)
{
    PruneRestSpotClaims();
    ...
    for (int offset = 0; offset < totalSpots; offset++)
    {
        int candidateIndex = (nextRestSpotPickIndex + offset) % totalSpots;
        NPCController owner = restSpotOccupants[candidateIndex];
        if (owner != null && owner != this)
            continue;

        restSpotOccupants[candidateIndex] = this;
        assignedRestSpotIndex = candidateIndex;
        nextRestSpotPickIndex = (candidateIndex + 1) % totalSpots;
        ...
        return true;
    }
    ...
}
```

---

## 10) Reset robusto de runtime (limpeza de estado estatico + threads)

### Problema
Reiniciar sem desligar corretamente pode deixar semaforos/threads/filas em estado sujo.

### Estrategia
- Desativa NPCs (dispara ciclo de desligamento)
- Espera um frame
- Reseta estados estaticos
- Opcionalmente recarrega cena

### Referencias
- GameStateResetter.cs:34
- GameStateResetter.cs:40
- GameStateResetter.cs:47
- GameStateResetter.cs:50
- GameStateResetter.cs:52
- BasketSemaphoreCore.cs:49
- BasketSemaphoreCore.cs:59
- BasketSemaphoreCore.cs:289

### Trecho real
```csharp
private IEnumerator ResetRoutine()
{
    isResetting = true;
    Time.timeScale = 1f;

    NPCController[] activeChildren = FindObjectsOfType<NPCController>();
    for (int i = 0; i < activeChildren.Length; i++)
    {
        NPCController child = activeChildren[i];
        if (child != null && child.gameObject.activeSelf)
            child.gameObject.SetActive(false);
    }

    yield return null;

    NPCController.ResetRuntimeStateNow();
    ChildBehaviour.ResetRuntimeStateNow();
    BasketSemaphoreCore.ResetRuntimeStateNow();
    ...
}
```

```csharp
private static void ResetRuntimeState()
{
    lock (initLock)
    {
        initialized = false;
        capacity = 0;
        basketCount = 0;
        totalSystemBalls = 0;
        lastBasketActionCompletedAtMs = 0;

        DisposeSemaphore(ref mutex);
        DisposeSemaphore(ref empty);
        DisposeSemaphore(ref full);
    }
}
```

---

## 11) Observabilidade e diagnostico de escalonamento

### Problema
Sem monitoracao, fica dificil distinguir thread executando, pronta ou bloqueada.

### Estrategia
Estados de escalonamento publicados pela thread e agregados no HUD.

### Referencias
- NPCController.Types.cs:32
- ChildThreadCore.cs:300
- ThreadMonitoringHUD.cs:105
- ThreadMonitoringHUD.cs:111
- ThreadMonitoringHUD.cs:131

### Trecho real
```csharp
public enum ChildThreadSchedulingState
{
    Running,
    Ready,
    Blocked
}
```

```csharp
private void MarkReadyCheckpoint(CancellationToken cancellationToken)
{
    SetSchedulingState(NPCController.ChildThreadSchedulingState.Ready);
    cancellationToken.ThrowIfCancellationRequested();
    Thread.Yield();
    SetSchedulingState(NPCController.ChildThreadSchedulingState.Running);
}
```

```csharp
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

statusBuilder.Append("Exec: ")
    .Append(executingCount)
    .Append(" | Prontas: ")
    .Append(readyCount)
    .Append(" | Bloqueadas: ")
    .Append(blockedCount);
```

---

## 12) Operacao de K em runtime pela interface

### Problema
Necessidade de alterar capacidade durante execucao com feedback claro e validacao.

### Estrategia
`BasketControl` valida entrada, chama `TrySetCapacity`, mostra resultado no monitor e log.

### Referencias
- BasketControl.cs:65
- BasketControl.cs:75
- BasketControl.cs:83
- BasketControl.cs:100
- BasketControl.cs:104
- BasketControl.cs:106

### Trecho real
```csharp
public void ApplyKFromInputField()
{
    ...
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
```

```csharp
basketMonitorText.text =
    $"K atual: {currentCapacity}\n" +
    $"Bolas no cesto: {basketCount}\n" +
    $"Total no sistema (M): {BasketSemaphoreCore.TotalSystemBalls}\n" +
    (!string.IsNullOrWhiteSpace(lastApplyFeedback) ? $"Última ação: {lastApplyFeedback}" : string.Empty);
```

---

## Conclusao tecnica
O sistema trata os principais riscos de concorrencia com uma arquitetura em camadas:
- Nucleo concorrente (semaforos, atomicos, cancelamento, compensacao)
- Ponte visual-thread (readiness por flags)
- Governanca de runtime (reset robusto e monitoracao)

Essa combinacao reduz:
- corrida de dados
- deadlocks e travamentos no encerramento
- inconsistencias de contagem do cesto
- descolamento entre estado visual e estado logico
- perda de observabilidade em depuracao
