# Plano de Projeto: Automação Instagram - Odd Oddities

Este documento serve como guia e contexto para o desenvolvimento de uma Prova de Conceito (POC) de automação de postagens no Instagram utilizando **.NET 8/9**, **Docker**, **Banco de Dados Relacional** e **APIs de Inteligência Artificial**.

---

## 1. Visão Geral do Projeto
* **Nome do Perfil:** Odd Oddities
* **Frequência de Postagem:** 3 posts por semana (aprox. 12 posts por mês).
* **Conceito:** Gerar uma curiosidade aleatória (texto) e uma ilustração visual abstrata/poética correspondente (imagem).
* **Custo Estimado de IA:** Menos de **$0.40 USD / mês** (considerando modelos de imagem de ~$0.03/geração e modelos de texto gratuitos ou em frações de centavos via OpenRouter).

---

## 2. Requisitos de Infraestrutura & Plataforma
* **Instagram Business / Creator:** A conta do Instagram deve ser profissional e estar obrigatoriamente vinculada a uma Página do Facebook.
* **Instagram Graph API:** Acesso oficial e 100% gratuito (limite de até 100 posts por dia).
* **Hospedagem:** VPS Própria rodando a aplicação em **Containers Docker**.

---

## 3. Arquitetura da Aplicação (.NET + Docker)

### Componentes Sugeridos
* **Worker Service (.NET):** Um serviço em segundo plano executando um `CronJob` ou `PeriodicTimer` três vezes por semana.
* **Banco de Dados (PostgreSQL ou SQLite no Docker):** Armazenar chaves, textos e hashes/vetores das curiosidades para evitar duplicidade.
* **Refit ou HttpClient:** Para comunicação com a API do OpenRouter e os endpoints da Graph API da Meta.
* **ImageSharp (SixLabors):** Biblioteca .NET para manipulação de imagem (adicionar marca d'água, moldura ou o título "Odd Oddities" na imagem gerada antes do upload).

---

## 4. Fluxo Lógico do Script (Pipeline)

```
[Cron Trigger]
       │
       ▼
 1. Gerar Texto ──────> 2. Validar Ineditismo ──────> 3. Gerar Imagem
 (OpenRouter API)           (Busca no DB)              (OpenRouter / Flux)
                                                             │
                                                             ▼
 6. Postar no Feed <─── 5. Criar Container Meta <─── 4. Processar Imagem
  (/media_publish)             (/media)                 (ImageSharp / Texto)
```

1. **Geração de Texto:** Solicitar ao LLM uma curiosidade aleatória e bizarra dentro do escopo "Odd Oddities".
2. **Validação:** Verificar no banco de dados se a curiosidade ou o tema central já foram abordados recentemente. Se duplicado, rejeitar e gerar outro.
3. **Geração de Imagem:** Enviar o texto aprovado junto com o prompt estético fixo (Ex: *"...in the style of a poetic textured painting, fine art surrealism"*).
4. **Processamento Visual:** Utilizar o `ImageSharp` para formatar a imagem em 1:1 (quadrado) ou 4:5, aplicando elementos visuais da identidade da página.
5. **Upload & Container (Meta):** Hospedar temporariamente a imagem na VPS e enviar a URL pública para o endpoint `POST /{instagram-business-account-id}/media`.
6. **Publicação Final:** Obter o `creation_id` gerado no passo anterior e chamar o endpoint `POST /{instagram-business-account-id}/media_publish`.

---

## 5. Próximos Passos para o Coding Agent
1. Criar a estrutura do projeto Worker Service em .NET.
2. Configurar o `Dockerfile` multi-stage otimizado para .NET Linux-x64.
3. Modelar o banco de dados (Tabela `Curiosities` com campos `Id`, `TextContent`, `CreatedAt`, `ImageUrl`).
4. Implementar o cliente HTTP para a API do OpenRouter utilizando as credenciais corretas.
5. Implementar o fluxo de autenticação OAuth / Token de Acesso de Longa Duração da Meta Graph API.