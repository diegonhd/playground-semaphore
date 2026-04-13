using System;
using System.Threading;

class Program
{
    const int N = 10; // Número de instâncias (na aplicação será uma entrada do usuário com botão "Criar criança")
    const int K = 4; // Espaço na cesta

    static readonly SemaphoreSlim mutex = new SemaphoreSlim(1, 1); // semaphore mutex (gerenciar ponto crítico)
    static readonly SemaphoreSlim empty = new SemaphoreSlim(K, K); // semaphore que indica quantas posições livres tem na cesta
    static readonly SemaphoreSlim full = new SemaphoreSlim(0, K); // semaphore que indica quantas posições ocupadas tem a cesta

    class Crianca
    {
        public int Id;
        public bool HasBall; // Flag para indicar se a criança começa com bola ou não
        public int Tb;
        public int Td;
    }

    static void Brincar(int id, int tb) // tb é o "tempo de brincar"
    // Aqui deve vir outra task cpu-bound
    {
        Console.WriteLine($"Criança {id} brinca por {tb}s");
        Thread.Sleep(tb * 1000);
    }

    static void Descansar(int id, int td) // td é o "tempo de descanso"
    // Aqui deve vir a task cpu-bound
    {
        Console.WriteLine($"Criança {id} descansa por {td}s");
        Thread.Sleep(td * 1000);
    }

    static void TarefaCrianca(object obj)
    {
        var c = (Crianca)obj!;

        while (true)
        {
            if (c.HasBall)
            {
                Brincar(c.Id, c.Tb); // chama a função com o determinado tb da criança

                empty.Wait(); // Wait é o mesmo que Down()
                mutex.Wait();

                c.HasBall = false;

                mutex.Release(); // Release é o mesmo que Up()
                full.Release();
                
                Descansar(c.Id, c.Td); // chama a função com o determinado td da criança
            }
            else
            {
                full.Wait();
                mutex.Wait();

                c.HasBall = true;

                mutex.Release();
                empty.Release();
            }
        }
    }

    static void Main()
    {
        var criancas = new Crianca[N];
        var threads = new Thread[N];
        int M = 3;

        for (int i = 0; i < N; i++)
        {
            criancas[i] = new Crianca // Criando N objetos
            {
                Id = i,
                HasBall = i < M,
                Tb = 2,
                Td = 1
            };

            threads[i] = new Thread(TarefaCrianca); // Cria N instâncias
            threads[i].Start(criancas[i]); 
        }

        for (int i = 0; i < N; i++)
        {
            threads[i].Join();
        }
    }
}