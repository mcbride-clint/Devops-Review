# PR LLM Review

Automated pull request code review using an onsite OpenAI-compatible LLM. Fetches PR diffs from Azure DevOps Server, sends them to your on-premises LLM, and posts findings back to the PR as a Markdown summary comment and inline line-level comments.

**No code or diff content leaves your network.**

## Two Deployment Options

| | Pipeline Task | Standalone Service |
|---|---|---|
| **Location** | `pr-llm-review/` | `PrLlmReview/` |
| **Runtime** | Node.js 20 (TypeScript) | ASP.NET Core 8 (.NET) |
| **Trigger** | ADO branch policy build validation | ADO service hook (webhook) |
| **Auth** | `$(System.AccessToken)` — no PAT needed | PAT with Code Read + PR Threads scopes |
| **ADO admin rights** | Required (task publishing via `tfx-cli`) | Not required |
| **Review history UI** | No | Optional (SQLite + Razor Pages) |
| **Best for** | Teams already using ADO pipelines | Teams wanting a zero-pipeline approach |

---

## Option A — Azure DevOps Pipeline Task (`pr-llm-review/`)

A custom ADO pipeline task packaged and published to your ADO Server instance. Triggered automatically on every PR open or update via a branch policy.

See [`pr-llm-review/README.md`](pr-llm-review/README.md) for full setup instructions.

### Quick Start

```bash
cd pr-llm-review/task
npm install
npm run build
tfx login --service-url https://{your-ado-server}/tfs/DefaultCollection --token {pat}
tfx build tasks upload --task-path ./task
```

Then add a **Build Validation** branch policy pointing to `pipeline-templates/pr-review-pipeline.yml`.

### Pipeline Variables Required

| Variable | Description |
|----------|-------------|
| `LLM_BASE_URL` | Base URL of your onsite LLM, e.g. `http://llm-server/v1` |
| `LLM_MODEL` | Model name, e.g. `codellama` |
| `LLM_API_KEY` | API key if required (mark as secret) |

---

## Option B — Standalone Webhook Service (`PrLlmReview/`)

An ASP.NET Core 8 web service hosted on an internal Windows Server under IIS. ADO Server calls it via a configured service hook whenever a PR is created or updated.

See [`PrLlmReview/README.md`](PrLlmReview/README.md) for full setup instructions.

### Quick Start

```bash
dotnet run --project PrLlmReview/PrLlmReview.Web

# Publish for IIS
dotnet publish PrLlmReview/PrLlmReview.Web -c Release -o C:\inetpub\PrLlmReview

# Run tests
dotnet test PrLlmReview/PrLlmReview.Tests
```

Set secrets via IIS environment variables — never in source control:

| Variable | Description |
|----------|-------------|
| `ADO__PERSONALACCESSTOKEN` | PAT with Code Read + PR Threads scopes |
| `ADO__WEBHOOKSECRET` | Shared secret validated on every webhook request |
| `LLM__APIKEY` | LLM API key if required |

---

## How It Works

1. A PR is opened or updated in Azure DevOps Server
2. The task/service fetches the PR diff via the ADO REST API
3. Binary, generated, and lock files are filtered out
4. The diff is sent to your onsite LLM (`POST /v1/chat/completions`)
5. The LLM returns a structured JSON response — summary + array of inline comments
6. A Markdown summary comment is posted at the PR level
7. Individual inline comments are posted to their exact file and line positions

## Review Focus Areas

The LLM is prompted to review in priority order:

1. **Security** — SQL injection, hardcoded secrets, insecure deserialization
2. **C#/.NET correctness** — null handling, async/await misuse, IDisposable, exception handling
3. **Oracle/SQL** — unparameterised queries, cursor leaks
4. **Code quality** — SOLID, DRY, unnecessary complexity
5. **Naming and style** — .NET naming standards, clarity

## Comment Format

### Summary Comment

Posted as a PR-level Markdown thread:

```
## 🤖 LLM Code Review
> Reviewed by {model} • {n} files • {timestamp}

### Overall Assessment
{summary from LLM}

### Findings by Severity
| Severity   | Count |
|------------|-------|
| 🔴 Critical | 0    |
| 🟠 High     | 2    |
| 🟡 Medium   | 5    |
| 🟢 Low      | 3    |
| ℹ️ Info      | 4    |
```

### Inline Comments

Posted directly on the changed lines:

```
**[🟠 High • Security]** Parameterise this Oracle query using bind variables...
```

## File Filtering

The following are excluded from review by default (configurable):

| Category | Patterns |
|----------|----------|
| Binary / media | `*.png`, `*.jpg`, `*.gif`, `*.ico`, `*.pdf`, `*.zip`, `*.dll`, `*.exe` |
| Lock files | `package-lock.json`, `yarn.lock`, `*.lock` |
| Generated code | `*.Designer.cs`, `*.g.cs`, `*Reference.cs`, `migrations/*` |
| Config / infra | `*.yml`, `*.yaml`, `*.json`, `*.config`, `*.csproj`, `*.sln` |
| Test snapshots | `__snapshots__/*`, `*.snap` |

## Repository Layout

```
Devops-Review/
  pr-llm-review/              ← Option A: ADO Pipeline Task (Node.js/TypeScript)
    task/
      task.json               – ADO task manifest
      package.json
      tsconfig.json
      src/
        index.ts              – entry point
        adoClient.ts          – ADO REST API wrapper
        diffParser.ts         – unified diff builder
        llmClient.ts          – LLM API wrapper
        commentPoster.ts      – posts comments to ADO
        promptBuilder.ts      – system + user prompt builder
        fileFilter.ts         – file exclusion logic
        models.ts             – TypeScript interfaces
    pipeline-templates/
      pr-review-pipeline.yml  – example pipeline YAML
    README.md

  PrLlmReview/                ← Option B: Standalone Webhook Service (ASP.NET Core)
    PrLlmReview.Web/
      Controllers/            – webhook endpoint
      BackgroundServices/     – async review queue
      Services/               – ADO client, diff parser, LLM client, comment poster
      History/                – SQLite storage + Razor Pages UI
      Program.cs
      appsettings.json
    PrLlmReview.Tests/        – xUnit tests
    PrLlmReview.sln
    README.md
```

## Security Notes

- All LLM communication happens on-premises — diffs never leave your network
- Webhook secret validated on every inbound request (Option B)
- OAuth token / PAT scopes are minimised to what is strictly required
- Secrets are injected via environment variables, never committed to source control
