

**PR LLM Code Review**  
Azure DevOps Pipeline Task

Project Specification for Claude Code

Version 1.0

# **1\. Project Overview**

An Azure DevOps pipeline task that automatically triggers on every pull request open or update event. The task fetches the PR diff via the Azure DevOps Server REST API, sends it to an onsite OpenAI-compatible LLM for code review, and posts the results back to the PR — both as an overall summary comment and as inline comments on specific changed lines.

The tool runs entirely within your on-premises environment. No code or diff content leaves the network.

**Review focus areas:** General code quality and best practices, security issues, C\#/.NET-specific concerns, Oracle SQL and database usage, and naming conventions and style.

# **2\. High-Level Flow**

PR opened or updated in Azure DevOps Server

        ↓

Branch policy triggers the PR pipeline

        ↓

Pipeline task starts — reads env vars (PR ID, repo, project, collection URL)

        ↓

Task calls ADO REST API → fetches PR diff (changed files \+ line ranges)

        ↓

Task filters diff (exclude binary, generated, lock files)

        ↓

Task sends diff chunks to onsite LLM via POST /v1/chat/completions

        ↓

LLM returns structured JSON — summary \+ array of inline comments

        → Task posts summary as a PR-level comment thread

        → Task posts each inline comment to its file \+ line position

# **3\. Tech Stack**

| Layer | Technology |
| :---- | :---- |
| Task runtime | Node.js (Azure DevOps pipeline tasks support Node 16/20 natively) |
| Language | TypeScript compiled to JS — strong typing helps with ADO API response shapes |
| ADO REST API | azure-devops-node-api (official Microsoft npm package) |
| LLM API | Standard fetch POST to /v1/chat/completions (OpenAI-compatible) |
| Task packaging | tfx-cli — packages and publishes the task to your ADO Server instance |
| Config | Pipeline variables \+ task.json inputs — no hardcoded values |

# **4\. Project Structure**

/pr-llm-review

  /task

    task.json                  ← ADO task manifest (name, inputs, version)

    package.json

    tsconfig.json

    /src

      index.ts                 ← task entry point

      adoClient.ts             ← Azure DevOps REST API wrapper

      diffParser.ts            ← parse unified diff into file+line chunks

      llmClient.ts             ← OpenAI-compatible API wrapper

      commentPoster.ts         ← post summary \+ inline comments to ADO

      promptBuilder.ts         ← build system \+ user prompts

      fileFilter.ts            ← exclude binary/generated/lock files

      models.ts                ← TypeScript interfaces

    /dist                      ← compiled JS (gitignored, built before publish)

  /pipeline-templates

    pr-review-pipeline.yml     ← example pipeline YAML to wire up the task

  README.md

# **5\. Task Manifest (task.json)**

The task.json defines how the task appears in Azure DevOps pipelines and what inputs it accepts.

**Required inputs:**

| Input Name | Type | Default | Purpose |
| :---- | :---- | :---- | :---- |
| llmBaseUrl | string | (required) | Base URL of onsite LLM e.g. http://llm-server/v1 |
| llmModel | string | (required) | Model name to pass in the API request |
| llmApiKey | secret | "none" | API key if required — stored as secret variable |
| maxFilesPerReview | int | 20 | Cap on files sent — avoids context overflow |
| maxLinesPerFile | int | 300 | Truncate large files with a warning comment |
| excludePatterns | string | see §8 | Glob patterns for files to skip |
| postInlineComments | bool | true | Toggle inline comment posting on/off |
| failOnSeverity | string | none | Optionally fail pipeline on critical/high findings |

# **6\. Azure DevOps REST API Usage**

The task uses the following ADO Server REST API endpoints. All calls use the built-in $(System.AccessToken) pipeline token — no personal access token setup needed if the pipeline service account is granted Contribute to pull requests permission on the repository.

## **6.1 Fetch PR Diff**

**Endpoint:** GET {collectionUrl}/{project}/\_apis/git/repositories/{repoId}/pullRequests/{prId}/iterations/{iterationId}/changes?api-version=6.0

This returns a list of all changed files in the PR iteration with their change type (add, edit, delete). For each changed file of interest, the task then fetches the file content at the PR source commit to get the actual diff text.

**Endpoint for file content:** GET {collectionUrl}/{project}/\_apis/git/repositories/{repoId}/items?path={filePath}\&version={commitSha}\&api-version=6.0

The task constructs a unified diff by comparing base commit content vs head commit content for each changed file using a lightweight diff library (diff npm package).

## **6.2 Post Summary Comment**

**Endpoint:** POST {collectionUrl}/{project}/\_apis/git/repositories/{repoId}/pullRequests/{prId}/threads?api-version=6.0

Posts a new comment thread at the PR level (no file/line position). The summary comment is formatted in Markdown and includes an overall assessment, a severity breakdown, and a list of the top findings.

{

  "comments": \[{

    "parentCommentId": 0,

    "content": "\#\# 🤖 LLM Code Review\\n\\n\*\*Summary:\*\* ...",

    "commentType": 1

  }\],

  "status": 1

}

## **6.3 Post Inline Comments**

**Endpoint:** POST {collectionUrl}/{project}/\_apis/git/repositories/{repoId}/pullRequests/{prId}/threads?api-version=6.0

Same endpoint as the summary, but with a threadContext block specifying the file path and line number. The rightFileStart/rightFileEnd positions correspond to the changed lines in the diff.

{

  "comments": \[{

    "parentCommentId": 0,

    "content": "\*\*\[Security\]\*\* Potential SQL injection...",

    "commentType": 1

  }\],

  "threadContext": {

    "filePath": "/src/Repositories/InvoiceRepo.cs",

    "rightFileStart": { "line": 42, "offset": 1 },

    "rightFileEnd":   { "line": 42, "offset": 1 }

  },

  "status": 1

}

# **7\. LLM Integration**

## **7.1 API Call**

Calls the onsite LLM using a standard OpenAI-compatible POST request. No external SDK required — plain fetch is sufficient.

POST {llmBaseUrl}/v1/chat/completions

{

  "model":       "{llmModel}",

  "max\_tokens":  4096,

  "temperature": 0.2,

  "messages": \[

    { "role": "system", "content": "{systemPrompt}" },

    { "role": "user",   "content": "{userPrompt}" }

  \]

}

Temperature is set low (0.2) to produce consistent, deterministic reviews rather than creative/varied output.

## **7.2 System Prompt**

The system prompt is fixed and establishes the LLM's role and output contract. It must instruct the model to return only valid JSON — no preamble, no markdown fences.

**System prompt template:**

You are an expert .NET and Oracle code reviewer embedded in a CI/CD pipeline.

Your job is to review code diffs and return structured feedback.

Focus on these areas in order of priority:

1\. Security vulnerabilities (SQL injection, hardcoded secrets, insecure deserialization)

2\. C\#/.NET correctness (null handling, async/await misuse, IDisposable, exception handling)

3\. Oracle/SQL concerns (unparameterised queries, missing indexes hint, cursor leaks)

4\. Code quality and best practices (SOLID, DRY, unnecessary complexity)

5\. Naming conventions and style (.NET naming standards, clarity)

Return ONLY a valid JSON object. No markdown. No explanation outside the JSON.

Schema:

{

  "summary": "string — 2-4 sentence overall assessment",

  "overallSeverity": "critical | high | medium | low | pass",

  "inlineComments": \[

    {

      "filePath":  "string — relative path matching the diff",

      "line":      "number — line number in the right (new) file",

      "severity":  "critical | high | medium | low | info",

      "category":  "security | correctness | sql | quality | style",

      "comment":   "string — specific actionable feedback for this line"

    }

  \]

}

## **7.3 User Prompt**

The user prompt is built per-request by promptBuilder.ts. It includes the PR title, description, and the filtered diff content.

PR Title: {prTitle}

PR Description: {prDescription}

Target Branch: {targetBranch}

Files changed ({fileCount} files, {lineCount} lines):

\--- {filePath1} \---

{unifiedDiffContent}

\--- {filePath2} \---

{unifiedDiffContent}

## **7.4 Chunking Strategy**

Large PRs are split into chunks to stay within the LLM context window. Each chunk is reviewed independently and results are merged before posting.

* Default chunk size: 3,000 lines of diff per LLM call  
* Each chunk includes the full system prompt  
* Results from all chunks are merged: summaries are concatenated; inline comments are combined  
* If total files exceed maxFilesPerReview, the task reviews the first N files and posts a notice comment listing the skipped files

# **8\. File Filtering**

The following file types are excluded from review by default. These defaults can be extended via the excludePatterns task input.

| Category | Patterns excluded |
| :---- | :---- |
| Binary / media | \*.png, \*.jpg, \*.gif, \*.ico, \*.pdf, \*.zip, \*.dll, \*.exe |
| Lock files | package-lock.json, yarn.lock, \*.lock |
| Generated code | \*.Designer.cs, \*.g.cs, \*Reference.cs, migrations/\* |
| Config / infra | \*.yml, \*.yaml, \*.json, \*.config, \*.csproj, \*.sln |
| Test snapshots | \_\_snapshots\_\_/\*, \*.snap |

Note: .yml/.yaml pipeline files are excluded by default since the task would be reviewing its own configuration. This can be overridden.

# **9\. Comment Formatting**

## **9.1 Summary Comment**

Posted as a single PR-level comment thread. Formatted in Markdown so it renders correctly in the ADO PR view.

\#\# 🤖 LLM Code Review

\> Reviewed by {llmModel} • {fileCount} files • {timestamp}

\#\#\# Overall Assessment

{summary text from LLM}

\#\#\# Findings by Severity

| Severity | Count |

|----------|-------|

| 🔴 Critical | 0 |

| 🟠 High     | 2 |

| 🟡 Medium   | 5 |

| 🟢 Low      | 3 |

| ℹ️  Info     | 4 |

Inline comments have been added to the changed lines above.

## **9.2 Inline Comment Format**

Each inline comment is prefixed with a severity badge and category label so developers can scan quickly.

\*\*\[🟠 High • Security\]\*\* Parameterise this Oracle query using bind variables

(:param syntax). Concatenating user input directly into SQL strings

risks SQL injection even on internal tools.

**Severity badge mapping:**

| Severity | Prefix |
| :---- | :---- |
| critical | 🔴 Critical |
| high | 🟠 High |
| medium | 🟡 Medium |
| low | 🟢 Low |
| info | ℹ️ Info |

# **10\. Pipeline Configuration**

## **10.1 Branch Policy Setup**

In Azure DevOps Server, navigate to: Project Settings \> Repositories \> {Repo} \> Policies \> Branch Policies \> {target branch} and add a Build Validation policy pointing to the PR review pipeline. This ensures the task runs automatically on every PR opened or updated against that branch.

## **10.2 Example Pipeline YAML**

\# pr-review-pipeline.yml

trigger: none

pr:

  branches:

    include:

      \- main

      \- develop

pool:

  name: Default          \# your on-prem agent pool

steps:

  \- task: PrLlmReview@1

    displayName: 'LLM Code Review'

    inputs:

      llmBaseUrl:          '$(LLM\_BASE\_URL)'

      llmModel:            '$(LLM\_MODEL)'

      llmApiKey:           '$(LLM\_API\_KEY)'

      maxFilesPerReview:   20

      maxLinesPerFile:     300

      postInlineComments:  true

      failOnSeverity:      'none'

    env:

      SYSTEM\_ACCESSTOKEN: $(System.AccessToken)

The LLM\_BASE\_URL, LLM\_MODEL, and LLM\_API\_KEY values should be stored as pipeline variables or variable group secrets — not hardcoded in the YAML.

## **10.3 Required Permissions**

| Permission | Where to grant it |
| :---- | :---- |
| Contribute to pull requests | Project Settings \> Repositories \> Security \> {Build Service account} |
| Read repository | Same location — needed to fetch file content at commits |
| Allow scripts to access OAuth token | Pipeline \> Edit \> Agent job \> tick the checkbox |

# **11\. Publishing the Task to ADO Server**

Azure DevOps pipeline tasks are packaged and published using the tfx-cli tool. This only needs to be done once (and again on version bumps).

1. **Install tfx-cli:** npm install \-g tfx-cli  
2. **Compile TypeScript:** cd task && npm run build  
3. **Login to your ADO Server:** tfx login \--service-url https://{your-ado-server}/tfs/DefaultCollection \--token {pat}  
4. **Upload the task:** tfx build tasks upload \--task-path ./task  
5. **Verify:** The task appears in pipeline task picker as 'LLM Code Review'

For updates, increment the version in task.json (patch/minor/major) and re-run the upload command. Existing pipelines pick up the new version automatically if they reference @1 (major only).

# **12\. Error Handling**

| Failure Scenario | Behaviour | Pipeline Impact |
| :---- | :---- | :---- |
| LLM unreachable / timeout | Post a warning comment: LLM review unavailable | Task succeeds with warning — PR not blocked |
| LLM returns invalid JSON | Post raw LLM response as a comment with a parse error notice | Task succeeds with warning |
| ADO API token insufficient | Log permission error, task fails with clear message | Task fails — check permissions (see §10.3) |
| PR diff too large after filtering | Review first N files, post notice listing skipped files | Task succeeds |
| failOnSeverity triggered | Post summary, then fail the task with exit code 1 | Pipeline fails — PR blocked by policy |

The default failOnSeverity is none, meaning the task never fails the pipeline regardless of findings. This is the recommended starting configuration — use it in advisory mode first before considering enforcement.

# **13\. Recommended Claude Code Build Order**

6. **Step 1 — Scaffold:** Create project structure, task.json manifest, tsconfig.json, package.json with azure-devops-node-api and typescript dependencies  
7. **Step 2 — models.ts:** Define all TypeScript interfaces: PrInfo, ChangedFile, DiffChunk, LlmReviewResult, InlineComment, TaskInputs  
8. **Step 3 — adoClient.ts:** Wrap azure-devops-node-api — fetch PR info, iterations, changed files, and file content at a given commit SHA  
9. **Step 4 — diffParser.ts:** Compare base vs head file content using the diff npm package, produce unified diff strings with line number mapping  
10. **Step 5 — fileFilter.ts:** Implement glob-based exclusion logic using micromatch, apply maxFilesPerReview and maxLinesPerFile caps  
11. **Step 6 — promptBuilder.ts:** Build system prompt (fixed) and user prompt (dynamic) with PR metadata and diff content. Implement chunking logic.  
12. **Step 7 — llmClient.ts:** POST to /v1/chat/completions, parse JSON response, handle timeout and malformed response gracefully  
13. **Step 8 — commentPoster.ts:** Post summary thread and inline comment threads to ADO REST API, format with severity badges  
14. **Step 9 — index.ts:** Wire all services together, read task inputs via azure-pipelines-task-lib, implement failOnSeverity logic, handle all errors  
15. **Step 10 — pr-review-pipeline.yml:** Write the example pipeline YAML and README with publishing instructions

