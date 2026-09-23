/** An AI backend the server can talk to. Mirrors InstanceConfig.Providers on the server. */
export interface AiProvider {
  id: string;
  label: string;
  /** Reached at an address the user gives, rather than a fixed service. */
  needsBaseUrl: boolean;
  baseUrlPlaceholder?: string;
  modelPlaceholder: string;
  /** Works without an API key. */
  keyless: boolean;
}

/**
 * The providers offered on the settings page and in the setup wizard. Ollama comes first among
 * the local ones: its own API is told the context size and to skip a model's thinking, which the
 * OpenAI-compatible endpoint cannot be.
 */
export const AI_PROVIDERS: readonly AiProvider[] = [
  {
    id: 'anthropic',
    label: 'Anthropic (Claude)',
    needsBaseUrl: false,
    modelPlaceholder: 'e.g. claude-sonnet-5, claude-haiku-4-5',
    keyless: false,
  },
  {
    id: 'ollama',
    label: 'Ollama (local, recommended)',
    needsBaseUrl: true,
    baseUrlPlaceholder: 'http://localhost:11434',
    modelPlaceholder: 'e.g. qwen2.5:3b-instruct',
    keyless: true,
  },
  {
    id: 'openai_compatible',
    label: 'OpenAI-compatible (OpenAI, Groq, OpenRouter, LM Studio…)',
    needsBaseUrl: true,
    baseUrlPlaceholder: 'https://api.openai.com/v1',
    modelPlaceholder: 'e.g. gpt-4o-mini, llama3.1',
    keyless: true,
  },
];

/** The provider with this id, falling back to Anthropic (the server's default) for anything else. */
export function aiProvider(id: string): AiProvider {
  return AI_PROVIDERS.find(p => p.id === id) ?? AI_PROVIDERS[0];
}
