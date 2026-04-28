// Centraliza o modelo produtor/consumidor do cesto usando semáforos (empty/full/mutex),
// garantindo exclusão mútua e o espaçamento global de 2s entre operações put/take.
using System;
using System.Threading;
using UnityEngine;

public static class BasketSemaphoreCore
{
    // Tempo mínimo (ms) entre duas operações concluídas no cesto (put/take).
    // Esse atraso é global: vale para todas as crianças.
    private const int interActionDelayMilliseconds = 2000;
    // Limite superior de K aceito pelo sistema em runtime.
    private const int maxDynamicCapacity = 64;
    // Lock único para proteger inicialização/reset dos campos estáticos.
    private static readonly object initLock = new object();

    // Semáforo binário para seção crítica do cesto.
    private static SemaphoreSlim mutex;
    // Semáforo contador de vagas livres no cesto.
    private static SemaphoreSlim empty;
    // Semáforo contador de bolas disponíveis no cesto.
    private static SemaphoreSlim full;
    // Capacidade atual do cesto (K).
    private static int capacity;
    // Quantidade atual de bolas no cesto.
    private static int basketCount;
    // Quantidade total de bolas no sistema (bolas nas crianças + no cesto).
    private static int totalSystemBalls;
    // Timestamp da última operação concluída no cesto, para impor o gap global.
    private static long lastBasketActionCompletedAtMs;
    // Flag de inicialização para evitar recriar semáforos indevidamente.
    private static bool initialized;

    // Exposição thread-safe dos principais contadores globais.
    public static int BasketCount => Volatile.Read(ref basketCount);
    public static int Capacity => Volatile.Read(ref capacity);
    public static int TotalSystemBalls => Volatile.Read(ref totalSystemBalls);
    public static int MaxCapacity => maxDynamicCapacity;

    // Permite reset explícito do estado global durante a execução.
    public static void ResetRuntimeStateNow()
    {
        ResetRuntimeState();
    }

    // Unity chama este método em recarga de domínio/subsistema para limpar estado estático entre execuções.
    // Reseta o estado estático quando o Unity reinicializa o subsistema (Play/Stop, reload de cena/domínio).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    { // resetar o estado global de forma thread-safe, liberando recursos nativos dos semáforos antigos.
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

    // Inicializa os semáforos e contadores uma única vez, com capacidade mínima de 1.
    public static void EnsureInitialized(int basketCapacity)
    {
        if (basketCapacity <= 0)
            basketCapacity = 1;
        basketCapacity = Math.Min(basketCapacity, maxDynamicCapacity);

        // lock em objeto dedicado evita corrida de inicialização em acesso concorrente.
        lock (initLock)
        {
            if (initialized)
                return;

            capacity = basketCapacity;
            basketCount = 0;
            totalSystemBalls = 0;
            lastBasketActionCompletedAtMs = Environment.TickCount - interActionDelayMilliseconds;

            mutex = new SemaphoreSlim(1, 1);
            // O max fixo permite ajustar K em runtime sem recriar os semáforos.
            empty = new SemaphoreSlim(capacity, maxDynamicCapacity);
            full = new SemaphoreSlim(0, maxDynamicCapacity);
            initialized = true;
        }
    }

    // Ajusta K em tempo de execução mantendo consistência do estado do cesto.
    public static bool TrySetCapacity(int newCapacity, out string reason)
    {
        if (newCapacity <= 0)
        {
            reason = "K deve ser maior que zero.";
            return false;
        }

        if (newCapacity > maxDynamicCapacity)
        {
            reason = $"K máximo suportado é {maxDynamicCapacity}.";
            return false;
        }
        
        EnsureReady(); // Garante que os semáforos estão prontos para uso antes de tentar ajustar a capacidade. 
        
        bool acquiredMutex = false;
        // Ajusta os semáforos empty/full para refletir a nova capacidade
        // garantindo que não haja inconsistências (ex: mais bolas do que capacidade).
        try 
        {
            mutex.Wait(); // Exclusão mútua para evitar condições de corrida durante o ajuste de capacidade.
            acquiredMutex = true; // Marca que o mutex foi adquirido para garantir liberação no final

            int currentCapacity = capacity;
            int currentBasketCount = basketCount;
            if (newCapacity < currentBasketCount)
            {
                reason = $"K não pode ser menor que bolas atuais no cesto ({currentBasketCount}).";
                return false;
            }

            if (newCapacity == currentCapacity)
            {
                reason = $"K já está em {newCapacity}.";
                return true;
            }

            int currentFreeSlots = Math.Max(0, currentCapacity - currentBasketCount); // Vagas livres atuais no cesto.
            int newFreeSlots = newCapacity - currentBasketCount; // Vagas livres desejadas no cesto com a nova capacidade.
            int deltaFreeSlots = newFreeSlots - currentFreeSlots; // Diferença de vagas livres que precisamos ajustar nos semáforos.

            if (deltaFreeSlots > 0)
            {
                empty.Release(deltaFreeSlots);
            }
            else if (deltaFreeSlots < 0)
            {
                int slotsToRemove = -deltaFreeSlots; // Número de vagas a serem removidas.
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
            return true;
        }
        finally
        {
            if (acquiredMutex)
                mutex.Release();
        }
    }

    // Registra uma criança no sistema; crianças que já começam com bola contam no total global.
    public static void RegisterChild(bool startsWithBall)
    {
        EnsureReady();
        if (startsWithBall)
            Interlocked.Increment(ref totalSystemBalls);
    }

    // Remove uma criança do total global de bolas caso ela ainda esteja carregando uma.
    public static void UnregisterChild(bool currentlyHasBall)
    {
        if (!currentlyHasBall)
            return;

        while (true)
        {
            int current = Volatile.Read(ref totalSystemBalls);
            if (current <= 0)
                return;

            if (Interlocked.CompareExchange(ref totalSystemBalls, current - 1, current) == current)
                return;
        }
    }

    // Operação produtora: espera espaço, entra na seção crítica, deposita a bola e libera o consumidor.
    public static void PutBall(CancellationToken cancellationToken)
    {
        EnsureReady();

        bool acquiredEmpty = false;
        bool acquiredMutex = false;
        bool actionCompleted = false;
        try
        {
            // Produtor: espera vaga (empty) e entra na seção crítica (mutex).
            empty.Wait(cancellationToken);
            acquiredEmpty = true;

            mutex.Wait(cancellationToken);
            acquiredMutex = true;

            // Impõe espaçamento temporal entre acessos consecutivos ao recurso crítico.
            WaitForInterActionGap(cancellationToken);
            Interlocked.Increment(ref basketCount);
            MarkBasketActionCompleted();
            actionCompleted = true;
        }
        finally
        {
            if (acquiredMutex)
                mutex.Release();

            if (acquiredEmpty)
            {
                // Se a ação concluiu, converte a vaga consumida em item disponível.
                // Se não concluiu (cancelamento/falha), devolve a vaga para evitar vazamento de token.
                if (actionCompleted)
                    full.Release();
                else
                    empty.Release();
            }
        }
    }

    // Operação consumidora: espera bola disponível, entra no ponto crítico e retira uma bola do cesto.
    public static void TakeBall(CancellationToken cancellationToken)
    {
        EnsureReady();

        bool acquiredFull = false;
        bool acquiredMutex = false;
        bool actionCompleted = false;
        try
        {
            // Consumidor: espera item (full) e entra na seção crítica (mutex).
            full.Wait(cancellationToken); // cancelattion token 
            acquiredFull = true;

            mutex.Wait(cancellationToken);
            acquiredMutex = true;

            WaitForInterActionGap(cancellationToken);
            actionCompleted = TryDecrementNonNegative(ref basketCount);
            if (actionCompleted)
                MarkBasketActionCompleted();
        }
        finally
        {
            if (acquiredMutex)
                mutex.Release();

            if (acquiredFull)
            {
                // Se concluiu take, libera uma vaga (empty); caso contrário, devolve o item reservado (full).
                if (actionCompleted)
                    empty.Release();
                else
                    full.Release();
            }
        }
    }

    // Falha cedo se alguém chamar o núcleo concorrente antes da inicialização correta.
    private static void EnsureReady()
    {
        // Falha explícita aqui evita comportamento silencioso com semáforos nulos.
        if (!initialized || mutex == null || empty == null || full == null)
            throw new InvalidOperationException("BasketSemaphoreCore não inicializado.");
    }

    // Decremento atômico sem permitir valor negativo.
    private static bool TryDecrementNonNegative(ref int value)
    {
        while (true)
        {
            int current = Volatile.Read(ref value);
            if (current <= 0)
                return false;

            // CompareExchange: decremento atômico sem lock (só aplica se o valor não mudou).
            if (Interlocked.CompareExchange(ref value, current - 1, current) == current)
                return true;
        }
    }

    // Libera recursos nativos dos semáforos ao resetar o estado global.
    private static void DisposeSemaphore(ref SemaphoreSlim semaphore)
    {
        if (semaphore == null)
            return;

        semaphore.Dispose();
        semaphore = null;
    }

    // Espera o espaçamento global entre operações do cesto sem bloquear a thread de forma infinita.
    // Bloqueia até completar o intervalo mínimo desde a última ação bem-sucedida do cesto.
    private static void WaitForInterActionGap(CancellationToken cancellationToken)
    {
        while (true)
        {
            // Usa TickCount (monotônico para medição de intervalo) e espera bloqueante cancelável.
            long elapsed = Environment.TickCount - Volatile.Read(ref lastBasketActionCompletedAtMs);
            int remaining = (int)(interActionDelayMilliseconds - elapsed);
            if (remaining <= 0)
                return;

            cancellationToken.WaitHandle.WaitOne(Math.Max(1, remaining));
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    // Atualiza o timestamp do último acesso bem-sucedido ao cesto.
    // Registra o instante da última operação concluída para impor o espaçamento de 2s.
    private static void MarkBasketActionCompleted()
    {
        Volatile.Write(ref lastBasketActionCompletedAtMs, Environment.TickCount);
    }
}
