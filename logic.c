#include <stdio.h>
#include <stdbool.h>
#include <pthread.h>

#define N 10
#define K 4


typedef int semaphore;

semaphore mutex = 1;
semaphore empty = K;
semaphore full = 0;

struct Crianca {
    int id;
    int has_ball;
    int tb;
    int td;
};

void *tarefa_crianca(void *arg) 
{
    struct Crianca *c = (struct Crianca *)arg;

    while (true) {
        if (c->has_ball) {
            brincar(c->id, c->tb); 

            down(&empty);
            down(&mutex);
            
            c->has_ball = false;
            
            up(&mutex);
            up(&full);

            descansar(c->id, c->td);
        } 
        else {
            down(&full);
            down(&mutex);
            
            c->has_ball = true;
            
            up(&mutex);
            up(&empty);
        }
    }
}

int main() 
{
    struct Crianca criancas[N];
    pthread_t threads[N];
    int M = 3; 

    for (int i = 0; i < N; i++) {
        criancas[i].id = i;
        criancas[i].has_ball = (i < M) ? true : false;
        criancas[i].tb = 2; 
        criancas[i].td = 1;

        pthread_create(&threads[i], NULL, tarefa_crianca, &criancas[i]);
    }

    for (int i = 0; i < N; i++) {
        pthread_join(threads[i], NULL);
    }

    return 0;
}