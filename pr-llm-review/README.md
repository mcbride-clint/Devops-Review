# PR LLM Review — Azure DevOps Pipeline Task

An Azure DevOps pipeline task that automatically reviews pull request diffs using an onsite OpenAI-compatible LLM and posts findings back to the PR as a summary comment and inline line-level comments.

## Overview

- Triggers on every PR open or update via a branch policy build validation
- Fetches the PR diff via the Azure DevOps Server REST API
- Filters out binary, generated, and lock files
- Sends diff content to an onsite LLM (`/v1/chat/completions`)
- Posts a Markdown summary comment and per-line inline comments to the PR
- Never sends code off-premises

## Prerequisites

- Node.js 20 on your build agents
- `tfx-cli` installed globally (`npm install -g tfx-cli`)
- An Azure DevOps Server instance (2019+)
- An onsite LLM exposing an OpenAI-compatible `/v1/chat/completions` endpoint

## Project Structure

```
pr-llm-review/
  task/
    task.json          – ADO task manifest
    package.json
    tsconfig.json
    src/
      index.ts         – task entry point
      adoClient.ts     – ADO REST API wrapper
      diffParser.ts    – unified diff builder
      llmClient.ts     – LLM API wrapper
      commentPoster.ts – posts comments to ADO
      promptBuilder.ts – system + user prompt builder
      fileFilter.ts    – file exclusion logic
      models.ts        – TypeScript interfaces
    dist/              – compiled JS (gitignored)
  pipeline-templates/
    pr-review-pipeline.yml
```

## Build & Publish

```bash
# 1. Install dependencies
cd task
npm install

# 2. Compile TypeScript
npm run build

# 3. Login to your ADO Server
tfx login --service-url https://{your-ado-server}/tfs/DefaultCollection --token {pat}

# 4. Upload the task
tfx build tasks upload --task-path ./task

# 5. Verify — the task appears in the pipeline task picker as "LLM Code Review"
```

For version bumps, increment the version in `task.json` and re-run the upload command.

## Pipeline Configuration

Add the example YAML from `pipeline-templates/pr-review-pipeline.yml` to your repository. Then in Azure DevOps Server:

1. **Project Settings > Repositories > {Repo} > Policies > Branch Policies > {branch}**
2. Add a **Build Validation** policy pointing to this pipeline

### Required Pipeline Variables

| Variable | Description |
|----------|-------------|
| `LLM_BASE_URL` | Base URL of your onsite LLM, e.g. `http://llm-server/v1` |
| `LLM_MODEL` | Model name, e.g. `codellama` |
| `LLM_API_KEY` | API key if required (mark as secret) |

### Required Permissions

| Permission | Where |
|-----------|-------|
| Contribute to pull requests | Project Settings > Repositories > Security > {Build Service account} |
| Read repository | Same location |
| Allow scripts to access OAuth token | Pipeline > Edit > Agent job > tick checkbox |

## Task Inputs

| Input | Default | Description |
|-------|---------|-------------|
| `llmBaseUrl` | (required) | Base URL of the onsite LLM |
| `llmModel` | (required) | Model name |
| `llmApiKey` | `none` | API key if required |
| `maxFilesPerReview` | `20` | Cap on files to review |
| `maxLinesPerFile` | `300` | Truncate files exceeding this limit |
| `excludePatterns` | see defaults | Comma-separated glob patterns to skip |
| `postInlineComments` | `true` | Toggle inline comment posting |
| `failOnSeverity` | `none` | Fail pipeline at: `none`, `low`, `medium`, `high`, `critical` |

## Error Handling

| Scenario | Behaviour |
|----------|-----------|
| LLM unreachable / timeout | Warning comment posted; task succeeds |
| LLM returns invalid JSON | Raw response posted as comment; task succeeds |
| ADO token insufficient | Task fails with clear permission error message |
| PR diff too large | First N files reviewed; skipped files listed in summary |
| `failOnSeverity` triggered | Summary posted, then task fails with exit code 1 |
