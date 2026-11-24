# Configuração da página do GitHub do MB Extreme

O GitHub só deixa o dono mudar estas opções pelo navegador. Copie e cole os textos abaixo.

## 1. Sobre o repositório (engrenagem ⚙ ao lado de "About", na página inicial)

- **Description:**
  `MB Extreme: otimizador completo para Windows 10 e 11. Jogos, FPS, rede, limpeza, reparo, benchmark e estúdio de gravação, com tudo reversível. Da MB InfoArt.`
- **Website:** `https://mbinfoarte.github.io/MB-Extreme/`. Troque pelo domínio próprio quando ele existir.
- **Topics:**
  `windows` `windows-11` `windows-10` `optimizer` `pc-optimization` `gaming` `fps-boost` `benchmark` `network` `cleaner` `wpf` `dotnet` `csharp` `portuguese` `mb-extreme`
- Marque **Releases** e desmarque **Packages** e **Deployments**, se não for usar.

## 2. Nome

Em **Settings > General > Repository name**, o nome atual `MB-Extreme` já serve: os links e o site usam esse nome. Se trocar, os links de download mudam.

## 3. Deixar público (necessário para o download sem login)

Em **Settings > General > Danger Zone > Change repository visibility > Make public**.

Sem isso:
- os links de download pedem login
- o instalador pequeno não consegue baixar
- o site no GitHub Pages exige um plano pago

Antes de tornar público, confira se não há senhas no código. Os segredos ficam em **Settings > Secrets** e não aparecem para ninguém.

## 4. Site no ar (GitHub Pages)

1. Em **Settings > Pages > Build and deployment > Source**, escolha **GitHub Actions**.
2. Em **Actions > Site (GitHub Pages) > Run workflow**, rode na branch `main`. Depois disso, ele roda sozinho a cada mudança no site.
3. O endereço será `https://mbinfoarte.github.io/MB-Extreme/`.
4. O fluxo também roda na branch de desenvolvimento. Para ela publicar, inclua a branch em **Settings > Environments > github-pages > Deployment branches**. Outra opção é juntar a branch com a `main`.

### Domínio próprio (opcional)

1. Compre o domínio (ex.: `mbextreme.com.br`).
2. Em **Settings > Secrets and variables > Actions > Variables**, crie `MBX_SITE_DOMAIN` com o domínio.
3. No painel do domínio, crie um registro **CNAME** de `www` para `mbinfoarte.github.io`. Para o domínio sem "www", crie registros **A** apontando para 185.199.108.153, 185.199.109.153, 185.199.110.153 e 185.199.111.153.
4. Em **Settings > Pages**, escreva o domínio em **Custom domain** e marque **Enforce HTTPS**.

## 5. O que o site estático tem

As páginas Início, Planos, Download, Afiliados, Termos e Privacidade, com o mesmo visual do servidor. Login, conta e painel de afiliado precisam do servidor de licenças. Enquanto ele não estiver no ar, esses botões levam ao Download.

## 6. Links fixos de download (sempre a versão mais nova)

- Instalador: `https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Beta-Setup.exe`
- Instalador pequeno: `https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Instalar.exe`
- Portátil: `https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Beta-portatil-win-x64.zip`

Cada compilação publica a versão como **Latest** (não mais como pré-lançamento), para esses links funcionarem.
