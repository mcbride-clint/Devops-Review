

**PR LLM Code Review**  
Standalone Webhook Service

Project Specification for Claude Code

Version 1.0  —  IIS Hosted, On-Premises

# **1\. Project Overview**

A standalone ASP.NET Core web service hosted on an internal Windows Server under IIS. Azure DevOps Server is configured to send a service hook (webhook) to this service whenever a pull request is created or updated. The service receives the webhook, fetches the PR diff via the ADO REST API, sends it to an onsite OpenAI-compatible LLM for review, and posts the results back to the PR as a summary comment and inline line-level comments.

This approach requires no changes to Azure DevOps pipeline permissions, no task publishing, and no build agent involvement. It operates entirely as an external HTTP service that ADO calls outbound.

**Review focus areas:** General code quality and best practices, security issues, C\#/.NET-specific concerns, Oracle SQL and database usage, and naming conventions and style.

**Review history:** Optional — a lightweight SQLite database stores past review results, queryable via a simple internal web UI.

# **2\. High-Level Flow**

Developer opens or updates a PR in Azure DevOps Server

        ↓

ADO Server fires a configured Service Hook → POST to https://{internal-server}/api/review

        ↓

Webhook controller validates the shared secret in the request header

        ↓

Controller returns HTTP 202 Accepted immediately (async processing)

        ↓

Background job: fetch PR diff via ADO REST API using a PAT

        ↓

Filter diff (exclude binary, generated, lock files)

        ↓

Send diff chunks to onsite LLM via POST /v1/chat/completions

        ↓

Parse structured JSON response from LLM

        ↓

Post summary \+ inline comments back to ADO PR via REST API

        ↓

Optionally persist review record to SQLite history store

**Why return 202 immediately:** ADO service hooks time out after 10 seconds if they do not receive a response. LLM processing may take longer than this. The controller accepts the request immediately and processes asynchronously in the background using a hosted service queue.

# **3\. Tech Stack**

| Layer | Technology |
| :---- | :---- |
| Framework | ASP.NET Core 8 Web API (C\#) |
| Hosting | IIS on internal Windows Server via ASP.NET Core Module (ANCM) |
| Async processing | IHostedService \+ Channel\<T\> in-memory queue — no external queue needed |
| ADO REST calls | HttpClient with Bearer PAT token — no SDK dependency |
| LLM API | HttpClient POST to /v1/chat/completions (OpenAI-compatible) |
| Review history (optional) | SQLite via Microsoft.Data.Sqlite — zero infrastructure, single file DB |
| History UI (optional) | Razor Pages — simple read-only browse/search interface |
| Config | appsettings.json \+ environment variable overrides — no secrets in source |

# **4\. Project Structure**

/PrLlmReview

  /PrLlmReview.Web                    ← ASP.NET Core project

    /Controllers

      WebhookController.cs             ← receives ADO service hook POST

    /BackgroundServices

      ReviewQueueService.cs            ← IHostedService, drains the queue

      ReviewQueue.cs                   ← Channel\<ReviewJob\> wrapper

    /Services

      AdoClientService.cs              ← ADO REST API calls (diff, comments)

      DiffParserService.cs             ← builds unified diff from file versions

      FileFilterService.cs             ← glob exclusion \+ line/file caps

      PromptBuilderService.cs          ← system \+ user prompt construction

      LlmClientService.cs              ← /v1/chat/completions wrapper

      CommentPosterService.cs          ← posts summary \+ inline to ADO

      ReviewOrchestratorService.cs     ← coordinates all services end-to-end

    /History                           ← optional review history feature

      HistoryRepository.cs             ← SQLite read/write

      /Pages                           ← Razor Pages for history UI

        Index.cshtml                   ← list/search past reviews

        Detail.cshtml                  ← full review detail view

    /Models

      AdoWebhookPayload.cs             ← deserialised ADO service hook body

      ReviewJob.cs                     ← queued work item

      ParsedColumn.cs

      LlmReviewResult.cs

      InlineComment.cs

      ReviewRecord.cs                  ← history DB model

    appsettings.json

    Program.cs

  /PrLlmReview.Tests

# **5\. Configuration (appsettings.json)**

All sensitive values should be overridden via environment variables or IIS application settings rather than committed to source control. The structure below shows every configurable value.

{

  "Ado": {

    "CollectionUrl":  "https://{ado-server}/tfs/DefaultCollection",

    "PersonalAccessToken": "",        // set via env var ADO\_\_PERSONALACCESSTOKEN

    "WebhookSecret":  ""              // set via env var ADO\_\_WEBHOOKSECRET

  },

  "Llm": {

    "BaseUrl":   "http://{llm-server}/v1",

    "Model":     "codellama",

    "ApiKey":    "",                  // set via env var LLM\_\_APIKEY if required

    "TimeoutSeconds":    30,

    "MaxTokens":         4096,

    "Temperature":       0.2,

    "ChunkSizeLines":    3000

  },

  "Review": {

    "MaxFilesPerReview": 20,

    "MaxLinesPerFile":   300,

    "ExcludePatterns": \[

      "\*\*/\*.png", "\*\*/\*.jpg", "\*\*/\*.dll", "\*\*/\*.exe",

      "\*\*/package-lock.json", "\*\*/yarn.lock",

      "\*\*/\*.Designer.cs", "\*\*/\*.g.cs", "\*\*/Migrations/\*"

    \]

  },

  "History": {

    "Enabled":  true,

    "DbPath":   "C:\\\\PrLlmReview\\\\history.db"

  }

}

# **6\. Azure DevOps Service Hook Configuration**

This is the one-time setup required in ADO Server to point events at the webhook service. No pipeline changes are needed.

## **6.1 Steps to Configure**

1. **Navigate:** Project Settings \> Service Hooks \> Create subscription  
2. **Service:** Select Web Hooks from the service list  
3. **Trigger (first hook):** Select Pull request created  
4. **Filters:** Set repository filter to target repo(s). Leave branch filter blank to cover all branches.  
5. **Action URL:** Enter http://{internal-server}/api/review (or https if you configure a cert)  
6. **HTTP headers:** Add a custom header: X-ADO-Secret: {your-shared-secret}  
7. **Repeat:** Create a second identical subscription for Pull request updated trigger

ADO Server will POST a JSON payload to the configured URL on every matching event. The payload contains the PR ID, repository ID, project name, and collection URL — everything the service needs to fetch the diff.

## **6.2 ADO Webhook Payload Shape**

The service deserialises the following fields from the ADO service hook POST body:

{

  "eventType": "git.pullrequest.created",   // or git.pullrequest.updated

  "resource": {

    "pullRequestId": 42,

    "title":         "My PR title",

    "description":   "...",

    "sourceRefName": "refs/heads/feature/my-branch",

    "targetRefName": "refs/heads/main",

    "repository": {

      "id":   "{repo-guid}",

      "name": "MyRepo"

    },

    "url": "{self-link to PR}"

  },

  "resourceContainers": {

    "project": { "id": "{project-guid}", "name": "MyProject" },

    "collection": { "baseUrl": "https://{ado-server}/tfs/DefaultCollection" }

  }

}

## **6.3 Webhook Secret Validation**

The WebhookController must validate the shared secret before processing any request. This prevents arbitrary external callers from triggering reviews.

// WebhookController.cs

\[HttpPost\]

public IActionResult Receive(\[FromBody\] AdoWebhookPayload payload)

{

    var secret \= Request.Headers\["X-ADO-Secret"\].FirstOrDefault();

    if (secret \!= \_config\["Ado:WebhookSecret"\])

        return Unauthorized();

    \_queue.Enqueue(new ReviewJob(payload));

    return Accepted();   // Return 202 immediately — process async

}

# **7\. Async Background Processing**

Because LLM processing can take 15-60 seconds, the webhook endpoint must return immediately. Work is passed to a background hosted service via an in-memory queue.

## **7.1 Queue Implementation**

// ReviewQueue.cs — thin wrapper around System.Threading.Channels

public class ReviewQueue

{

    private readonly Channel\<ReviewJob\> \_channel \=

        Channel.CreateUnbounded\<ReviewJob\>();

    public void Enqueue(ReviewJob job) \=\>

        \_channel.Writer.TryWrite(job);

    public IAsyncEnumerable\<ReviewJob\> ReadAllAsync(CancellationToken ct) \=\>

        \_channel.Reader.ReadAllAsync(ct);

}

## **7.2 Hosted Service**

// ReviewQueueService.cs

public class ReviewQueueService : BackgroundService

{

    protected override async Task ExecuteAsync(CancellationToken ct)

    {

        await foreach (var job in \_queue.ReadAllAsync(ct))

        {

            try   { await \_orchestrator.RunAsync(job, ct); }

            catch (Exception ex) { \_logger.LogError(ex, "Review failed"); }

        }

    }

}

Errors in individual review jobs are logged but do not crash the service. The queue continues processing subsequent jobs normally.

# **8\. Azure DevOps REST API Usage**

All ADO API calls are made using a Personal Access Token (PAT) with the minimum required scopes. The PAT is stored in appsettings and never exposed to callers.

## **8.1 Required PAT Scopes**

| Scope | Purpose |
| :---- | :---- |
| Code (Read) | Fetch file content at commit SHAs |
| Pull Request Threads (Read & Write) | Post summary and inline comments to PRs |

## **8.2 API Calls Made**

| Purpose | Endpoint |
| :---- | :---- |
| Get PR iterations | GET .../pullRequests/{prId}/iterations?api-version=6.0 |
| Get changed files | GET .../iterations/{id}/changes?api-version=6.0 |
| Get file at base commit | GET .../items?path={p}\&versionType=commit\&version={sha} |
| Get file at head commit | GET .../items?path={p}\&versionType=commit\&version={sha} |
| Post summary comment | POST .../pullRequests/{prId}/threads?api-version=6.0 |
| Post inline comment | POST .../pullRequests/{prId}/threads?api-version=6.0 |

The diff is constructed in code by comparing base and head file content using a .NET diff library (DiffPlex — lightweight, no native dependencies, suitable for air-gapped environments).

# **9\. LLM Integration**

Identical prompt design to the pipeline task variant. Repeated here for completeness.

## **9.1 System Prompt**

You are an expert .NET and Oracle code reviewer.

Your job is to review code diffs and return structured feedback.

Focus on these areas in order of priority:

1\. Security vulnerabilities (SQL injection, hardcoded secrets, insecure deserialization)

2\. C\#/.NET correctness (null handling, async/await misuse, IDisposable, exceptions)

3\. Oracle/SQL concerns (unparameterised queries, cursor leaks, missing bind variables)

4\. Code quality and best practices (SOLID, DRY, unnecessary complexity)

5\. Naming conventions and style (.NET naming standards, clarity)

Return ONLY a valid JSON object. No markdown. No explanation outside the JSON.

{

  "summary": "string — 2-4 sentence overall assessment",

  "overallSeverity": "critical | high | medium | low | pass",

  "inlineComments": \[

    {

      "filePath":  "string",

      "line":      "number",

      "severity":  "critical | high | medium | low | info",

      "category":  "security | correctness | sql | quality | style",

      "comment":   "string"

    }

  \]

}

## **9.2 Chunking**

* Default chunk size: 3,000 lines of diff per LLM call  
* Large PRs are split across multiple calls; results merged before posting  
* Files exceeding maxLinesPerFile are truncated with a notice in the diff  
* Files exceeding maxFilesPerReview cap: first N reviewed, skipped files listed in the summary comment

# **10\. Comment Formatting**

## **10.1 Summary Comment**

\#\# 🤖 LLM Code Review

\> Reviewed by {model} • {fileCount} files • {timestamp}

\#\#\# Overall Assessment

{summary}

\#\#\# Findings by Severity

| Severity | Count |

|----------|-------|

| 🔴 Critical | {n} |

| 🟠 High     | {n} |

| 🟡 Medium   | {n} |

| 🟢 Low      | {n} |

| ℹ️  Info     | {n} |

## **10.2 Inline Comment Format**

\*\*\[🟠 High • Security\]\*\* Parameterise this Oracle query using bind variables

(:param syntax). Concatenating user input directly risks SQL injection.

# **11\. Review History (Optional Feature)**

When History:Enabled is true, each completed review is persisted to a SQLite database. A simple Razor Pages UI allows developers to browse and search past reviews on the internal server.

## **11.1 Database Schema**

CREATE TABLE ReviewRecord (

  Id             INTEGER PRIMARY KEY AUTOINCREMENT,

  ReviewedAt     TEXT NOT NULL,          \-- ISO8601 UTC

  ProjectName    TEXT NOT NULL,

  RepositoryName TEXT NOT NULL,

  PrId           INTEGER NOT NULL,

  PrTitle        TEXT NOT NULL,

  AuthorName     TEXT,

  TargetBranch   TEXT,

  FilesReviewed  INTEGER,

  OverallSeverity TEXT,

  SummaryText    TEXT,

  FullResultJson TEXT                    \-- full LLM response stored as JSON

);

## **11.2 History UI Pages**

| Page | Features |
| :---- | :---- |
| /history | Paginated list of past reviews — PR title, repo, date, overall severity badge, link to detail |
| /history/{id} | Full review detail — summary, all inline comments grouped by file, severity badges |
| Search | Filter by repo name, PR title keyword, severity, or date range |

# **12\. IIS Deployment**

## **12.1 Prerequisites on the Windows Server**

* IIS installed with the ASP.NET Core Hosting Bundle (installs ANCM automatically)  
* .NET 8 Runtime (included in Hosting Bundle)  
* Network access from the ADO Server to this machine on the chosen port  
* Network access from this machine to the onsite LLM server

## **12.2 Deployment Steps**

8. **Publish:** dotnet publish \-c Release \-o C:\\inetpub\\PrLlmReview  
9. **IIS Site:** Create a new IIS site pointing to C:\\inetpub\\PrLlmReview. Set application pool to No Managed Code (ANCM handles the runtime).  
10. **App pool identity:** Use a service account or ApplicationPoolIdentity. Ensure it has read access to the publish folder and write access to the SQLite DB folder if history is enabled.  
11. **Environment variables:** Set ADO\_\_PERSONALACCESSTOKEN, ADO\_\_WEBHOOKSECRET, and LLM\_\_APIKEY in IIS \> Site \> Configuration Editor \> system.webServer/aspNetCore \> environmentVariables.  
12. **HTTPS (recommended):** Bind a self-signed or internal CA certificate to the site. ADO service hooks can use HTTPS with self-signed certs if Allow untrusted SSL is checked in the subscription settings.  
13. **Firewall:** Open the chosen port inbound from the ADO Server IP only.  
14. **Test:** POST a test payload to /api/review with the correct X-ADO-Secret header and verify a 202 response and a review comment appearing on a test PR.

## **12.3 web.config**

A web.config is auto-generated by dotnet publish for ANCM. No manual configuration is needed beyond the IIS site setup above. Ensure stdoutLogEnabled is set to false in production and logs are directed to a file for troubleshooting.

\<aspNetCore processPath="dotnet"

            arguments=".\\PrLlmReview.Web.dll"

            stdoutLogEnabled="false"

            stdoutLogFile=".\\logs\\stdout"

            hostingModel="inprocess" /\>

# **13\. Security**

| Guardrail | Implementation |
| :---- | :---- |
| Webhook secret | Validate X-ADO-Secret header on every request. Return 401 immediately if missing or wrong. |
| PAT scope minimisation | PAT has Code Read \+ PR Threads Read/Write only. No admin, no identity, no build scopes. |
| No secrets in source | PAT, webhook secret, LLM API key set via IIS environment variables only. |
| SELECT-only LLM content | No user-supplied SQL is executed. The service only reads diffs and posts comments. |
| Network isolation | Firewall rule restricts inbound webhook port to ADO Server IP only. |
| LLM timeout | HttpClient timeout set to LLM TimeoutSeconds config value (default 30s). |
| History UI access | History UI is internal-only. Add Windows Authentication or IP restriction in IIS if needed. |

# **14\. Error Handling**

| Failure Scenario | Behaviour | Impact |
| :---- | :---- | :---- |
| ADO webhook times out waiting for response | 202 returned immediately; processing is async so this never occurs | None |
| LLM unreachable / timeout | Post warning comment to PR: LLM review unavailable, retry manually | PR not blocked |
| LLM returns invalid JSON | Post raw LLM response as comment with parse error notice | PR not blocked |
| ADO PAT expired or insufficient | Log error; no comment posted; alert visible in server logs | Silent failure — monitor logs |
| Duplicate webhook (ADO retries) | Check for existing review thread on PR before posting; skip if already reviewed for this iteration | No duplicate comments |
| Service restart with pending jobs | In-memory queue is lost on restart. Jobs in flight at restart time are not retried. Acceptable for advisory use. | Occasional missed review on restart |

# **15\. Key Differences from the Pipeline Task Variant**

| Aspect | Pipeline Task | This Webhook Service |
| :---- | :---- | :---- |
| ADO permissions needed | Contribute to PR on build service account \+ pipeline task publish rights | PAT with Code Read \+ PR Threads only. No ADO admin rights. |
| Trigger mechanism | Branch policy build validation | ADO Service Hook (Project Settings) |
| Runs on | ADO build agent | Dedicated IIS server — independent of build agents |
| Deployment | tfx-cli publish to ADO | dotnet publish \+ IIS site |
| Review history | Not included | Optional SQLite \+ Razor Pages UI |
| Per-repo opt-in | Add pipeline to each repo | One service hook subscription per repo in Project Settings |

# **16\. Recommended Claude Code Build Order**

15. **Step 1 — Scaffold:** ASP.NET Core 8 Web API project, appsettings.json with full config structure, Program.cs registering all services and hosted service  
16. **Step 2 — Models:** AdoWebhookPayload, ReviewJob, LlmReviewResult, InlineComment, ReviewRecord  
17. **Step 3 — WebhookController:** POST /api/review endpoint with secret validation, 202 response, and queue enqueue  
18. **Step 4 — ReviewQueue \+ ReviewQueueService:** Channel\<ReviewJob\> wrapper and BackgroundService drain loop with error handling  
19. **Step 5 — AdoClientService:** HttpClient with PAT auth, all six ADO REST calls, DiffPlex diff construction  
20. **Step 6 — FileFilterService:** Glob exclusion via Microsoft.Extensions.FileSystemGlobbing, line and file caps  
21. **Step 7 — PromptBuilderService:** System prompt constant, dynamic user prompt builder with PR metadata and chunking logic  
22. **Step 8 — LlmClientService:** POST /v1/chat/completions, timeout handling, JSON parse with fallback to raw text  
23. **Step 9 — CommentPosterService:** Summary thread post, inline comment threads with threadContext, duplicate detection  
24. **Step 10 — ReviewOrchestratorService:** Coordinate steps 5-9, merge multi-chunk results, call history repository if enabled  
25. **Step 11 — History (optional):** HistoryRepository with SQLite, Razor Pages Index and Detail views with search/filter  
26. **Step 12 — IIS artifacts:** Confirm web.config output, document environment variable setup, write deployment README

