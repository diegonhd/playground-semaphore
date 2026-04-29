# playground-semaphore

Simulação em **Unity** do problema de concorrência tipo **produtor/consumidor**, em que cada criança (thread) alterna entre: brincar (**Tb**), colocar bola no cesto (**put**), descansar (**Td**) e pegar bola (**take**).

## Árvore do repositório (estrutura útil)

```text
/playground-semaphore
├── .vscode/                          # Configuração de execução/editor
├── docs/
│   └── projeto.png                   # Referência visual do projeto (base do enunciado)
├── src/                              # Projeto Unity completo
│   ├── Assets/
│   │   ├── Art/
│   │   │   ├── Buttons and Tilesets/
│   │   │   ├── Tiles/
│   │   │   └── Player/
│   │   │       ├── Buneary, Chinchar, Piplup, Riolu
│   │   │       └── *SemPokebola      # Versões sem bola (campo "Bola?" = false)
│   │   ├── Images/                   # Imagens de UI (botões, molduras e fundo)
│   │   ├── Prefabs/                  # Prefabs de personagens e componentes de interface
│   │   ├── Scenes/
│   │   │   └── Game.unity            # Cena principal
│   │   ├── Scripts/                  # Lógica concorrente + controle visual/UI
│   │   └── Settings/                 # Configurações locais de render/cena
│   ├── Docs/
│   │   └── analise-tratamento-problemas-concorrencia.md
│   ├── Packages/
│   ├── ProjectSettings/              # Configurações de edição do Unity
│   └── UserSettings/
├── .gitignore
└── README.md
```

## Orientações sobre o projeto (projeto.png)
![Orientações](docs\projeto.png)

## Interseções entre as orientações do projeto e a implementação Unity

| Conceito do enunciado (PDF) | Onde isso aparece no Unity |
|---|---|
| **Campo "Bola?"** (criança inicia com bola ou sem) | `ChildBehaviour.cs` lê o toggle `hasBallToggle`; `NPCController.ConfigureSpawnHasBall(...)` aplica o estado inicial; `ChildThreadCore` registra `startsWithBall`; arte separada em `Assets/Art/Player/*` e `*SemPokebola`. |
| **Tb** (tempo de brincar) | Entrada via UI em `ChildBehaviour.cs` (`tbInputField`) e execução CPU-bound em `ChildThreadCore` durante `PlayingWithBall`. |
| **Td** (tempo de descanso) | Entrada via UI em `ChildBehaviour.cs` (`tdInputField`) e execução CPU-bound em `ChildThreadCore` durante `Resting`; animações específicas em pastas `Td/td` dos sprites. |
| **K** (capacidade do cesto) | Controle em `BasketControl.cs` (input + botão), aplicado por `BasketSemaphoreCore.TrySetCapacity(...)`, com feedback no monitor (`K atual`, `Bolas no cesto`, `Total no sistema (M)`). |
| **Produtor/Consumidor com semáforos** | Núcleo em `BasketSemaphoreCore.cs` com `mutex`, `empty`, `full`; operações `PutBall(...)` e `TakeBall(...)` sincronizam o acesso ao cesto. |
| **Ordem de acesso ao cesto (filas)** | `NPCController.cs` mantém filas visuais de `put` e `take` (`putQueuePositions` / `takeQueuePositions`) e só libera a thread quando o NPC está na vez correta. |
| **Observação de estados de execução** | `ThreadMonitoringHUD.cs` exibe estados de escalonamento (`Running`, `Ready`, `Blocked`) e logs de eventos por criança. |

## Organização de Assets (resumo)

1. **Art**: botões/tilesets e sprites dos personagens (Pokémons), incluindo versões com e sem pokébola para refletir o campo **"Bola?"**.
2. **Images**: imagens complementares de interface (molduras, fundo e elementos visuais).
3. **Prefabs**: prefabs dos personagens e de componentes de UI.
4. **Scenes**: contém a cena principal do jogo (`Game.unity`).
5. **Settings**: configurações internas do projeto/cena no Unity.

## Documentação técnica complementar

- `docs/projeto.png`: referência visual do escopo/fluxo do projeto.
- `src/Docs/analise-tratamento-problemas-concorrencia.md`: detalhamento técnico de concorrência, semáforos, cancelamento, monitoramento e reset de runtime.
