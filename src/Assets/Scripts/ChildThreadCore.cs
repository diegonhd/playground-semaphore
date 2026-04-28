// Executa o ciclo de vida concorrente de uma criança em uma thread de SO:
// Tb (CPU-bound) -> putBall -> Td (CPU-bound) -> takeBall, com cancelamento seguro.
using System;
using System.Diagnostics;
using System.Threading;

public sealed class ChildThreadCore : IDisposable
{
    // Identificador lógico da criança (usado em logs/nome da thread).
    private readonly int childId;

    // Duração alvo de Tb (tempo de brincar CPU-bound), em segundos.
    private readonly float playSecondsTb;

    // Duração alvo de Td (tempo de descanso CPU-bound), em segundos.
    private readonly float restSecondsTd;

    // serve para encerrar o objeto/cena sem travar o processo caso algo dê errado,
    //  e para garantir que a thread encerre de forma segura
    // serve tambem para encerrar as threads quando eu quiser resetar o estado.
    private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

    // Referência da thread real do sistema operacional.
    private Thread workerThread;
    // Acumulador para impedir otimizações agressivas no loop CPU-bound.
    private double cpuWorkSink;
    // Flags/estados compartilhados entre thread core e Unity (0/1 ou enum como int).
    private int hasBallFlag;
    private int statusFlag;
    private int playAreaReadyFlag;
    private int restSpotReadyFlag;
    private int putQueueTurnReadyFlag;
    private int takeQueueTurnReadyFlag;
    private int playSecondsRemainingFlag;
    private int restSecondsRemainingFlag;
    private int schedulingStateFlag;
    // estado de ciclo de vida da thread: 0=criado, 1=rodando, 2=parado.
    private int lifecycleState; // 

    // propriedades públicas thread-safe para leitura pelo lado visual (hud de monitoramento).
    public bool HasBall => Volatile.Read(ref hasBallFlag) == 1; // get em tempo real (volatile) se a criança tem a bola.
    public NPCController.ChildThreadStatus CurrentStatus => (NPCController.ChildThreadStatus)Volatile.Read(ref statusFlag);
    // pega o valor das flags de "pronto" usadas para decidir se a thread pode prosseguir para a próxima etapa
    public int CurrentPlaySecondsRemaining => Volatile.Read(ref playSecondsRemainingFlag);
    // pega o valor da flag de tempo restante de tb volatilmente
    public int CurrentRestSecondsRemaining => Volatile.Read(ref restSecondsRemainingFlag);
    // mesma coisa para a flag de tempo restante de td
    public NPCController.ChildThreadSchedulingState CurrentSchedulingState =>
        (NPCController.ChildThreadSchedulingState)Volatile.Read(ref schedulingStateFlag);

    // monta a thread de uma criança, registra seu estado inicial e conecta com o núcleo do cesto.
    public ChildThreadCore(int childId, float tb, float td, bool startsWithBall, int basketCapacity)
    {

        // inicialização de campos iniciais
        this.childId = childId; // id da criança
        playSecondsTb = Math.Max(0f, tb); 
        restSecondsTd = Math.Max(0f, td); 
        playSecondsRemainingFlag = (int)Math.Ceiling(playSecondsTb);
        // inicializa o flag de tb restante arredondando para cima o tempo de descanso (calculado em ms)
        restSecondsRemainingFlag = (int)Math.Ceiling(restSecondsTd);
        // mesmo para td restante
        schedulingStateFlag = (int)NPCController.ChildThreadSchedulingState.Ready;
        // registra criança como pronta para iniciar o ciclo antes da thread começar a rodar de fato, 
        // para que o controlador visual já possa ler o estado inicial correto

        BasketSemaphoreCore.EnsureInitialized(basketCapacity);
        // inicializa o semaforo do cesto com a capacidade configurada
        BasketSemaphoreCore.RegisterChild(startsWithBall);
        // registra a criança com um arg que indica se ela tem bola ou nao no inicio
        // para que o semaforo contabilize corretamente o número de bolas disponíveis no início do jogo

        SetHasBall(startsWithBall);
        // atualiza a flag de "tem bola" de acordo com o estado inicial configurado
        // essa função ficará sendo chamada toda vez que a instancia pegar ou soltar a bola 

        SetStatus(startsWithBall
            ? NPCController.ChildThreadStatus.PlayingWithBall
            : NPCController.ChildThreadStatus.WaitingBallInBasket);
    }

    // Inicia a thread de execução real; só pode ser chamada uma vez.
    public void Start()
    {
        // CompareExchange evita iniciar a mesma thread mais de uma vez.
        if (Interlocked.CompareExchange(ref lifecycleState, 1, 0) != 0)
            return;

        // Uma thread de SO por criança para simular concorrência real fora da main thread do Unity
        workerThread = new Thread(RunLoop) // runloop é o método que contém o ciclo de vida da criança, 
        // rodando em uma thread separada para simular concorrência real
        {
            IsBackground = true,
            // Define a thread como background para que ela não impeça o encerramento do processo se algo der errado.
            Name = $"ChildThread-{childId:00}"
            // nomeia a thread para facilitar identificação em logs e depuração
        };
        workerThread.Start();
    }

    // Atualiza as "travas visuais": a thread só prossegue quando o NPC está no lugar certo (play area ou rest spot) 
    // ou na vez certa (put/take queue).

    public void SetVisualReadiness(bool playAreaReady, bool restSpotReady, bool putQueueTurnReady, bool takeQueueTurnReady)
    { // Atualiza as "travas visuais": a thread só prossegue quando o NPC está no lugar certo (play area ou rest spot) 
    // ou na vez certa (put/take queue).
        Volatile.Write(ref playAreaReadyFlag, playAreaReady ? 1 : 0); 
        Volatile.Write(ref restSpotReadyFlag, restSpotReady ? 1 : 0);
        Volatile.Write(ref putQueueTurnReadyFlag, putQueueTurnReady ? 1 : 0);
        Volatile.Write(ref takeQueueTurnReadyFlag, takeQueueTurnReady ? 1 : 0);

        // essas flags estão sendo atualizadas pelo lado visual (NPCController) 
        // para indicar quando a criança pode prosseguir para a próxima etapa do ciclo
    }

    // Encerramento padrão do recurso: cancela, aguarda finalização e libera referências.
    public void Dispose()
    {
        Stop(); // garante que a thread seja encerrada de forma segura
        cancellationTokenSource.Dispose(); //   libera a fonte de cancelamento
    }

    // Centraliza o término seguro da thread sem matar o processo abruptamente.
    private void Stop()
    {
        if (Interlocked.Exchange(ref lifecycleState, 2) == 2)
        // usa-se o interlocked.exchange para gravar 2 em lifecycleState e retorna o valor antigo, 
        // para garantir que o processo de parada só seja executado uma vez
            return;

        cancellationTokenSource.Cancel();
        // Solicita cancelamento da thread, que deve encerrar 
        // seu ciclo de vida de forma cooperativa.   

        // Join com timeout evita travar encerramento caso a thread não finalize imediatamente.
        if (workerThread != null && workerThread.IsAlive && Thread.CurrentThread != workerThread)
            workerThread.Join(2000);

        BasketSemaphoreCore.UnregisterChild(HasBall);
    }

    // Loop principal da thread: alterna entre brincar, put, descansar e take conforme o estado.
    private void RunLoop()
    {
        CancellationToken cancellationToken = cancellationTokenSource.Token;
        try
        {
            // Após criação/start, a thread entra como pronta antes do primeiro despacho real na CPU.
            MarkReadyCheckpoint(cancellationToken);

            while (!cancellationToken.IsCancellationRequested) // o ciclo de vida da criança continua 
            // até que seja solicitado cancelamento
            {
                if (HasBall)
                {
                    SetStatus(NPCController.ChildThreadStatus.PlayingWithBall);
                    // Tb só começa quando o avatar já está visualmente na área de brincar.
                    SetSchedulingState(NPCController.ChildThreadSchedulingState.Blocked);
                    // bloqueia a thread até que a condição visual de estar na área de brincar seja satisfeita,
                    WaitForFlag(ref playAreaReadyFlag, cancellationToken);
                    // quando a condição visual é satisfeita, a thread é desbloqueada e pode começar a 
                    // executar o trabalho de CPU-bound de brincar (Tb).
                    SetSchedulingState(NPCController.ChildThreadSchedulingState.Running);
                    // simula o trabalho de CPU-bound de brincar por Tb segundos executando um loop que consome CPU, 
                    // e atualizando a flag de tempo restante de forma thread-safe para o HUD ler.
                    RunCpuBoundForSeconds(playSecondsTb, ref playSecondsRemainingFlag, cancellationToken);
                    // Ao terminar Tb, a thread volta a disputar CPU para a próxima etapa.
                    MarkReadyCheckpoint(cancellationToken); // marca um checkpoint de "pronto" 
                    // para que o controlador visual saiba que a thread terminou Tb 
                    // e pode tentar despachá-la para a próxima etapa (putBall).

                    SetStatus(NPCController.ChildThreadStatus.WaitingBasketSpace);
                    // putBall só pode iniciar quando a criança estiver no primeiro lugar da fila de put.
                    SetSchedulingState(NPCController.ChildThreadSchedulingState.Blocked);
                    // bloqueia a thread até que a condição visual de estar na vez de put seja satisfeita,
                    WaitForFlag(ref putQueueTurnReadyFlag, cancellationToken);
                    // quando a condição visual é satisfeita, a thread é desbloqueada 
                    // e pode tentar pegar o semáforo do cesto para colocar a bola.
                    SetSchedulingState(NPCController.ChildThreadSchedulingState.Blocked);
                    BasketSemaphoreCore.PutBall(cancellationToken);
                    // Após desbloqueio/retorno do semáforo, a thread volta a ficar pronta.
                    MarkReadyCheckpoint(cancellationToken);
                    SetHasBall(false);

                    SetStatus(NPCController.ChildThreadStatus.Resting);
                    // Td só começa quando a criança já alcançou o spot de descanso.
                    SetSchedulingState(NPCController.ChildThreadSchedulingState.Blocked);
                    WaitForFlag(ref restSpotReadyFlag, cancellationToken);
                    // espera bloqueada até a condição visual de estar no spot de descanso ser satisfeita,
                    SetSchedulingState(NPCController.ChildThreadSchedulingState.Running);
                    RunCpuBoundForSeconds(restSecondsTd, ref restSecondsRemainingFlag, cancellationToken);
                    // Ao terminar Td, a thread volta a disputar CPU para a próxima etapa.
                    MarkReadyCheckpoint(cancellationToken);
                }
                else
                {
                    SetStatus(NPCController.ChildThreadStatus.WaitingBallInBasket);
                    // takeBall só pode iniciar quando a criança estiver no primeiro lugar da fila de take.
                    SetSchedulingState(NPCController.ChildThreadSchedulingState.Blocked);
                    // bloqueia a thread até que a condição visual de estar na vez de take seja satisfeita,
                    WaitForFlag(ref takeQueueTurnReadyFlag, cancellationToken);
                    // quando a condição visual é satisfeita, a thread é desbloqueada
                    // e pode tentar pegar o semáforo do cesto para pegar a bola.
                    SetSchedulingState(NPCController.ChildThreadSchedulingState.Blocked);
                    // tenta pegar o semáforo do cesto para pegar a bola, bloqueando a thread até que a bola esteja disponível,
                    BasketSemaphoreCore.TakeBall(cancellationToken);
                    // Após desbloqueio/retorno do semáforo, a thread volta a ficar pronta.
                    MarkReadyCheckpoint(cancellationToken);
                    SetHasBall(true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento esperado.
        }
    }

    // Simula trabalho CPU-bound real para Tb/Td em vez de apenas suspender a thread 
    // para representar a nivel do SO a concorrencia das threads e o impacto real no scheduler, 
    // e para que o HUD possa mostrar o tempo restante de forma realista.
    private void RunCpuBoundForSeconds(float durationSeconds, ref int remainingSecondsFlag, CancellationToken cancellationToken)
    {
        if (durationSeconds <= 0f)
        {
            Volatile.Write(ref remainingSecondsFlag, 0);
            return;
        }
        // Loop intencionalmente CPU-bound para representar Tb/Td como trabalho de CPU.

        Stopwatch stopwatch = Stopwatch.StartNew();
        // cronometro para medir o tempo decorrido de forma precisa
        // já que queremos simular um trabalho que consome CPU por um certo número de segundos.

        double localSink = cpuWorkSink;
        // variável local para acumular resultados de cálculos e evitar otimizações agressivas do compilador
        // que poderiam eliminar o loop se ele não tivesse efeitos colaterais observáveis
        // fazendo com que o loop realmente consuma CPU como pretendido.

        int lastReportedRemainingSeconds = int.MaxValue;
        // variável para rastrear o último valor de segundos restantes que foi reportado na flag,
        // para evitar escrever na flag a cada iteração do loop e causar sobrecarga de escrita Volatile
        // desnecessária, já que o HUD só precisa de atualizações quando o número inteiro de segundos restantes muda.

        // O loop continua rodando até que o tempo decorrido atinja a duração alvo, que
        // é o que simula o trabalho de CPU-bound de brincar ou descansar por um certo número de segundos
        while (stopwatch.Elapsed.TotalSeconds < durationSeconds)
        { 
            int remainingSeconds = Math.Max(0, (int)Math.Ceiling(durationSeconds - stopwatch.Elapsed.TotalSeconds));
            // calcula o número de segundos restantes arredondando para cima, 
            // para que a flag mostre o tempo restante de forma mais intuitiva (ex: 5, 4, 3, 2, 1, 0)
            if (remainingSeconds != lastReportedRemainingSeconds)
            {
                Volatile.Write(ref remainingSecondsFlag, remainingSeconds);
                lastReportedRemainingSeconds = remainingSeconds;
                // isso evita escrever na flag a cada iteração do loop, 
                // o que poderia causar sobrecarga de escrita Volatile desnecessária
                // já que o HUD só precisa de atualizações quando o número inteiro de segundos restantes muda.
            }

            for (int i = 1; i <= 4096; i++)
            {
                // Simula trabalho de CPU fazendo cálculos matemáticos que 
                // dependem de iterações anteriores para evitar otimizações.
                localSink += Math.Sqrt(i + (localSink % 37.0));
                if (localSink > 1_000_000_000d)
                    localSink *= 0.000001d;

                // é importante pois se o compilador reduzir demais o curto
                // pois, por exemplo, o loop nao afeta o estado, ele pode tratar como trabalho mortl

            }

            cancellationToken.ThrowIfCancellationRequested();
            // verifica se foi solicitado cancelamento para encerrar o loop 
            // de forma cooperativa, o que é importante para garantir que a 
            // thread possa ser encerrada de forma segura quando for pedido
        }

        Volatile.Write(ref remainingSecondsFlag, 0); // garante que a flag de 
        // segundos restantes mostre 0 ao final do trabalho, 
        // mesmo que o loop termine um pouco depois da duração
        cpuWorkSink = localSink; // outra atribuição usada para que
        // seja evitadas as otimizações do compilador.
    }


    private static void WaitForFlag(ref int readinessFlag, CancellationToken cancellationToken)
    { // esta função é usada para fazer a thread esperar de forma ativa (spin wait) até que uma 
    // condição visual seja satisfeita,
        // SpinWait faz espera ativa curta (baixa latência) enquanto a condição visual não chega
        // spinwait != wait() porque o wait() suspende a thread e depende de um sinal para acordar, 
        // o que é mais pesado e tem latência maior. o spinwait mantem a thread ativa e verificando a 
        // condição (flag)
        SpinWait spinner = new SpinWait();
        while (Volatile.Read(ref readinessFlag) == 0) // readinessFlag é a flag de "pronto" que 
        // indica se a condição visual foi satisfeita
        {
            cancellationToken.ThrowIfCancellationRequested();
            // verifica se foi solicitado cancelamento para evitar ficar preso no loop de espera 
            spinner.SpinOnce();
            //  faz uma iteração de espera ativa, que pode incluir uma breve 
            // pausa ou yield para permitir que outras threads sejam agendadas,
            // mas mantém a thread ativa para que possa reagir rapidamente quando a condição visual 
            // for satisfeita.
        }
    }

    // Atualiza a flag compartilhada do estado "com bola" como falado no inicio do codigo
    private void SetHasBall(bool value)
    {
        Volatile.Write(ref hasBallFlag, value ? 1 : 0);
    }

    // Publica o status corrente para o controlador visual e HUD.
    private void SetStatus(NPCController.ChildThreadStatus status)
    {
        Volatile.Write(ref statusFlag, (int)status);
    }

    // Atualiza o estado de escalonamento publicado para o HUD.
    private void SetSchedulingState(NPCController.ChildThreadSchedulingState state)
    {
        Volatile.Write(ref schedulingStateFlag, (int)state);
    }

    // Publica um checkpoint de "pronto" antes da próxima execução na CPU.
    private void MarkReadyCheckpoint(CancellationToken cancellationToken)
    {
        SetSchedulingState(NPCController.ChildThreadSchedulingState.Ready);
        cancellationToken.ThrowIfCancellationRequested();
        Thread.Yield();
        SetSchedulingState(NPCController.ChildThreadSchedulingState.Running);
    }
}
