<p align="center">
  <img src="assets/brand/png/logo-128.png" alt="SwissTechKnife" width="96" />
</p>

<h1 align="center">SwissTechKnife</h1>

<p align="center">
  Seus utilitários para Windows num só lugar.<br />
  Instale só o que você usa, ligue e desligue quando quiser.
</p>

<p align="center">
  <a href="https://github.com/luingry/SwissTechKnife/releases/latest"><b>Baixar para Windows</b></a>
</p>

<p align="center">
  <img src="docs/preview.png" alt="Tela de módulos do SwissTechKnife" width="780" />
</p>

## O que é

O SwissTechKnife é um app leve que reúne várias ferramentas, chamadas de **módulos**. Em vez de instalar e manter um programa para cada coisa, você escolhe na lista os módulos que quer usar, e eles passam a funcionar dentro do SwissTechKnife, com o mesmo visual e as mesmas configurações.

## Módulos

| Módulo | Para que serve |
|---|---|
| **SmoothMice** | Deixa a rolagem do mouse suave em todo o Windows, com ajustes por aplicativo e sem interferir nos jogos. |
| **Tamarillo** | Segure Ctrl + Alt + Shift enquanto arrasta arquivos e escolha numa roda o que fazer com eles: converter, redimensionar, comprimir, reconhecer texto e mais. |
| **Monitor do Sistema** | Mostra o uso do processador, da memória e há quanto tempo o PC está ligado. |
| **Hash** | Calcula MD5, SHA-1, SHA-256 e SHA-512 de textos e arquivos. |
| **Winput LAN** | *Em breve:* um mouse e um teclado para vários PCs da rede. |
| **AQ Tracker** | *Em breve.* |

Novos módulos aparecem sozinhos na lista quando são publicados.

## Como usar

1. Baixe e execute o instalador `SwissTechKnife-<versão>-setup.exe`.
2. Abra o SwissTechKnife e vá em **Módulos**.
3. Clique no módulo que quer usar e depois em **Instalar**.
4. Ele aparece na barra lateral, em **Seus módulos**. Clique para abrir.

Use o switch no card para **ativar ou desativar** um módulo sem desinstalá-lo. Se o módulo tiver opções, elas ficam na aba **Configurações** da janela dele, e a aba **Notas da versão** mostra o que mudou em cada versão.

## Configurações

Clique na engrenagem no canto inferior da barra lateral:

- **Verificar atualizações:** diário, semanal, mensal ou nunca. Também dá para verificar na hora.
- **Iniciar com o Windows:** o SwissTechKnife abre sozinho quando você entra no Windows.
- **Esconder na área de notificação ao fechar:** fechar a janela mantém os módulos funcionando. Para sair de vez, clique com o botão direito no ícone perto do relógio e escolha **Sair**.

## Atualizações

O SwissTechKnife e os módulos se atualizam pelo próprio app. Quando há versão nova, você vê o que mudou e decide quando atualizar; o resto é automático.

## Privacidade e segurança

- Tudo roda no seu computador. O app só acessa a internet para ver se há módulos e atualizações novos e para baixá-los.
- Cada módulo e cada atualização é conferido antes de instalar; se o arquivo baixado não for exatamente o publicado, ele é recusado e nada muda.
- Desinstalar um módulo remove os arquivos dele na hora. As preferências do módulo ficam guardadas, caso você o instale de novo.

## Requisitos

Windows 10 ou 11, 64 bits.

## Para desenvolvedores

- [Desenvolvimento, build e releases](docs/DEVELOPMENT.md)
- [Como criar um módulo](docs/MODULES.md)
- [Design system](docs/DESIGN_SYSTEM.md)
