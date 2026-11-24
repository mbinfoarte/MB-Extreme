# Código privado + downloads públicos (dois repositórios)

Objetivo: esconder o código-fonte e, ao mesmo tempo, manter o link de download aberto e a atualização dentro do programa funcionando. No GitHub, o download de uma Release usa a mesma visibilidade do repositório, então a única forma é separar em dois:

- **mbinfoarte/MB-Extreme** — o código-fonte. Fica **privado**. É onde o GitHub Actions compila.
- **mbinfoarte/MB-Extreme-Download** — **público**, só com os instaladores. É de onde o programa baixa e atualiza. Não tem código.

O programa já aponta para o repositório de downloads (atualização, instalador pequeno e o `canal.json`). Falta você fazer 4 passos no GitHub:

## 1. Criar o repositório público de downloads

- Novo repositório: **MB-Extreme-Download**, **Public**, marque "Add a README".
- Descrição sugerida: "Downloads oficiais do MB Extreme (instaladores e versão portátil). O código-fonte é privado."

## 2. Colocar o canal.json no repositório de downloads

O programa lê as redes sociais e o suporte de `config/canal.json`. Crie esse arquivo no **MB-Extreme-Download**, no caminho `config/canal.json`, com o conteúdo que está em `config/canal.json` deste projeto (o mesmo de hoje). Sem ele, o programa usa os valores embutidos.

## 3. Criar o token e guardar como segredo

Para o Actions publicar a Release no outro repositório, ele precisa de um token:

1. **Settings do seu perfil → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token.**
2. Dê um nome (ex.: "MB Extreme releases"), validade a seu critério.
3. **Repository access:** Only select repositories → **MB-Extreme-Download**.
4. **Permissions → Repository permissions → Contents: Read and write**.
5. Gere e **copie** o token.
6. No repositório **MB-Extreme** (o do código) → **Settings → Secrets and variables → Actions → New repository secret**:
   - Nome: `RELEASES_TOKEN`
   - Valor: o token copiado.

A partir daí, cada compilação publica os instaladores no **MB-Extreme-Download**. Enquanto o segredo não existir, o Actions continua publicando no próprio repositório (para nada quebrar na transição).

## 4. Tornar o código privado

No **MB-Extreme** → **Settings → General → Danger Zone → Change repository visibility → Make private**.

Pronto: o código some do público, mas os links de download e a atualização seguem funcionando pelo **MB-Extreme-Download**.

## Links de download (sempre a versão mais nova)

- Instalador: `https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Beta-Setup.exe`
- Instalador pequeno: `https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Instalar.exe`
- Portátil: `https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Beta-portatil-win-x64.zip`

## Observações

- **Site (GitHub Pages):** se você ligar o site, ele é gerado pelo repositório do código. Com o código privado, o GitHub Pages exige um plano pago; por isso, se quiser o site, o melhor é hospedá-lo no repositório de downloads (dá para mover depois). Os endereços de download no site já apontam para o repositório público.
- **A atualização do programa já está apontando** para o repositório de downloads nesta versão. Instale esta versão (ou mais nova) nos PCs para a troca valer.
