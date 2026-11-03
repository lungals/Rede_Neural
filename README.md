# RedeNeural

## Integrantes 

Gabriel Torres.
Lucas Bragança.

## Instruções para abrir a cena principal

Abra o projeto e vá até a pasta Scenes e clique na SampleScene.

Depois selecione o cenário desejado.

## Tabela com inspector com configurações usadas

![Tabela do inspector] (./Tela_Inspector.png).

## Respostas e registros dos testes da Parte D.

### O melhor fitness correspondeu à rota mais eficiente? Mostre um caso ou contraexemplo com números.

O melhor fitness foi 406,92. Nele é possível ver que o objeto chega até seu destino depois de algumas gerações como mostrado no vídeo. Mas apesar disso é possível observar que a HUD indica chegou=False e eficiência=inválida. Mostrando um possível erro na HUD. Já que o objetivo foi sim atingido.

### Em qual cenário de validação o agente teve pior desempenho? Por quê?

O Cenário 2 apresentou o pior desempenho entre os cenários avaliados.

Acreditamos que isso ocorreu principalmente devido à disposição dos obstáculos próximos à região inicial, dificultando a movimentação do agente e a descoberta de uma trajetória adequada.

Esse cenário demonstrou como pequenas alterações na configuração do ambiente podem influenciar significativamente o resultado da evolução.

### Qual alteração no fitness poderia induzir um comportamento indesejado? Explique o possível atalho.

Aumentar excessivamente o peso do progresso poderia incentivar o agente a tentar atravessar obstáculos em linha reta, buscando atalhos em vez de encontrar uma rota válida.

### O genoma salvo reproduziu o comportamento esperado após fechar e reabrir a cena? Informe seed, geração e resultado.

Sim, o carregamento funcionou pelo modo Replay.

Seed: 16767;

Geração: 77;

Fitness: 406,92.

OBS: Ambos os valores foram usados para o mesmo teste, pois alguns problemas foram encontrados em caso de alteração. Como por exemplo o atraso até pensar numa rota adequada e o travamento nos obstáculos.

