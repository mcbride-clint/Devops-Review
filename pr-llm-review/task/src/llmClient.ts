import { LlmReviewResult, TaskInputs } from './models';

const REQUEST_TIMEOUT_MS = 120_000;

export class LlmClient {
  constructor(private readonly inputs: TaskInputs) {}

  async review(userPrompt: string, systemPrompt: string): Promise<LlmReviewResult> {
    const url = `${this.inputs.llmBaseUrl.replace(/\/$/, '')}/chat/completions`;

    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
    };

    if (this.inputs.llmApiKey && this.inputs.llmApiKey !== 'none') {
      headers['Authorization'] = `Bearer ${this.inputs.llmApiKey}`;
    }

    const body = JSON.stringify({
      model: this.inputs.llmModel,
      max_tokens: 4096,
      temperature: 0.2,
      messages: [
        { role: 'system', content: systemPrompt },
        { role: 'user', content: userPrompt },
      ],
    });

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

    let responseText: string;
    try {
      const response = await fetch(url, {
        method: 'POST',
        headers,
        body,
        signal: controller.signal,
      });

      if (!response.ok) {
        const errText = await response.text().catch(() => '');
        throw new Error(`LLM API returned HTTP ${response.status}: ${errText}`);
      }

      const json = await response.json() as { choices?: Array<{ message?: { content?: string } }> };
      responseText = json.choices?.[0]?.message?.content ?? '';
    } catch (err: unknown) {
      if (err instanceof Error && err.name === 'AbortError') {
        throw new Error(`LLM request timed out after ${REQUEST_TIMEOUT_MS / 1000}s`);
      }
      throw err;
    } finally {
      clearTimeout(timeout);
    }

    return parseLlmResponse(responseText);
  }
}

function parseLlmResponse(text: string): LlmReviewResult {
  // Strip markdown fences if the model returned them despite instructions
  const cleaned = text
    .replace(/^```(?:json)?\s*/i, '')
    .replace(/\s*```\s*$/, '')
    .trim();

  let parsed: Partial<LlmReviewResult>;
  try {
    parsed = JSON.parse(cleaned);
  } catch {
    // Return a fallback result containing the raw response
    return {
      summary: `LLM returned a response that could not be parsed as JSON. Raw response:\n\n${text}`,
      overallSeverity: 'pass',
      inlineComments: [],
    };
  }

  return {
    summary: parsed.summary ?? '',
    overallSeverity: parsed.overallSeverity ?? 'pass',
    inlineComments: parsed.inlineComments ?? [],
  };
}

export function mergeResults(results: LlmReviewResult[]): LlmReviewResult {
  if (results.length === 0) {
    return { summary: '', overallSeverity: 'pass', inlineComments: [] };
  }
  if (results.length === 1) return results[0];

  const severityOrder: LlmReviewResult['overallSeverity'][] = ['critical', 'high', 'medium', 'low', 'pass'];
  let worstSeverity: LlmReviewResult['overallSeverity'] = 'pass';

  for (const r of results) {
    if (severityOrder.indexOf(r.overallSeverity) < severityOrder.indexOf(worstSeverity)) {
      worstSeverity = r.overallSeverity;
    }
  }

  return {
    summary: results.map((r, i) => `**Chunk ${i + 1}:** ${r.summary}`).join('\n\n'),
    overallSeverity: worstSeverity,
    inlineComments: results.flatMap(r => r.inlineComments),
  };
}
