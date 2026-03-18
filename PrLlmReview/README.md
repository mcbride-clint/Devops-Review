# PR LLM Review — Standalone Webhook Service

An ASP.NET Core 8 web service hosted on an internal Windows Server under IIS. Azure DevOps Server sends a service hook (webhook) to this service whenever a PR is created or updated. The service fetches the diff, reviews it via an onsite LLM, and posts the results back to the PR.

## Why this approach?

- No pipeline task publishing or ADO admin rights needed
- No build agent involvement — runs independently on any Windows Server
- Optional review history stored in SQLite with a Razor Pages UI

## Prerequisites

- .NET 8 Runtime + ASP.NET Core Hosting Bundle on the Windows Server
- IIS with the ASP.NET Core Module (ANCM) installed
- Network access: ADO Server → this machine (inbound) and this machine → LLM server (outbound)
- A PAT with **Code (Read)** + **Pull Request Threads (Read & Write)** scopes

## Project Structure

```
PrLlmReview/
  PrLlmReview.Web/
    Controllers/
      WebhookController.cs         – POST /api/review endpoint
    BackgroundServices/
      ReviewQueue.cs               – Channel<ReviewJob> wrapper
      ReviewQueueService.cs        – BackgroundService drain loop
    Services/
      AdoClientService.cs          – ADO REST API calls
      DiffParserService.cs         – DiffPlex diff builder
      FileFilterService.cs         – Glob exclusion + line/file caps
      PromptBuilderService.cs      – System + user prompt construction
      LlmClientService.cs          – /v1/chat/completions wrapper
      CommentPosterService.cs      – Posts summary + inline comments
      ReviewOrchestratorService.cs – End-to-end coordination
    History/
      HistoryRepository.cs         – SQLite read/write
      Pages/
        Index.cshtml               – List/search past reviews
        Detail.cshtml              – Full review detail
    Models/                        – Shared model classes
    appsettings.json
    Program.cs
  PrLlmReview.Tests/               – xUnit tests
  PrLlmReview.sln
```

## Configuration

Set these via IIS environment variables — **never commit secrets to source control**:

| Environment Variable | Description |
|----------------------|-------------|
| `ADO__PERSONALACCESSTOKEN` | PAT with Code Read + PR Threads scopes |
| `ADO__WEBHOOKSECRET` | Shared secret validated on every webhook |
| `LLM__APIKEY` | LLM API key (if required) |

All other settings live in `appsettings.json` — update `Ado:CollectionUrl`, `Llm:BaseUrl`, `Llm:Model`, and `History:DbPath` for your environment.

### Custom CA Bundles (internal / self-signed certificates)

If your ADO Server or LLM endpoint uses an internal certificate authority, provide a PEM-formatted CA bundle:

| Setting | Scope | Description |
|---------|-------|-------------|
| `CaBundlePath` | ADO + LLM fallback | Path to a PEM CA bundle used for all HTTP clients. If `Llm:CaBundlePath` is not set, the LLM client uses this bundle too. |
| `Llm:CaBundlePath` | LLM only | Path to a PEM CA bundle used exclusively for LLM requests. Overrides the global `CaBundlePath` for the LLM client. |

Leave either value empty to skip custom certificate validation for that client.

## Build & Deploy

```bash
# Build and run locally
dotnet run --project PrLlmReview.Web

# Publish for IIS
dotnet publish PrLlmReview.Web -c Release -o C:\inetpub\PrLlmReview

# Run tests
dotnet test PrLlmReview.Tests
```

### IIS Setup

1. Install ASP.NET Core Hosting Bundle on the server
2. Create a new IIS site pointing to `C:\inetpub\PrLlmReview`
3. Set the application pool to **No Managed Code**
4. Add environment variables in IIS → Site → Configuration Editor → `system.webServer/aspNetCore/environmentVariables`
5. Open the chosen port inbound from the ADO Server IP only

## ADO Service Hook Configuration

1. **Project Settings > Service Hooks > Create subscription**
2. Select **Web Hooks** as the service
3. **Trigger:** `Pull request created`
4. **Action URL:** `http://{your-server}/api/review`
5. **HTTP headers:** `X-ADO-Secret: {your-shared-secret}`
6. Repeat for `Pull request updated`

## Review History UI

When `History:Enabled` is `true` in `appsettings.json`, completed reviews are stored in SQLite and browsable at:

- `/history` — paginated list with search/filter by repo, title, severity, date
- `/history/{id}` — full review detail with inline comments grouped by file

Secure with Windows Authentication or IIS IP restrictions if needed.

## Error Handling

| Scenario | Behaviour |
|----------|-----------|
| LLM unreachable / timeout | Warning comment posted to PR; service continues |
| LLM returns invalid JSON | Raw response posted as comment |
| ADO PAT expired | Error logged; no comment posted |
| Duplicate webhook (ADO retry) | Checks for existing review thread; skips if found |
| Service restart with queued jobs | In-memory queue is lost; acceptable for advisory use |

## Security

- Webhook secret validated on every request — unauthorized callers receive `401`
- PAT scopes minimised to Code Read + PR Threads only
- No secrets in source control — set via IIS environment variables
- LLM timeout configured to prevent indefinite blocking
- History UI is internal-only; add IIS authentication if required
