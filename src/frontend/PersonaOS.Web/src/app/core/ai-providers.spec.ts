import { AI_PROVIDERS, aiProvider } from './ai-providers';

describe('AI providers', () => {
  it('offers every provider the server accepts', () => {
    expect(AI_PROVIDERS.map(p => p.id)).toEqual(['anthropic', 'ollama', 'openai_compatible']);
  });

  it('asks for an address only for providers reached at one', () => {
    expect(aiProvider('anthropic').needsBaseUrl).toBeFalse();
    expect(aiProvider('ollama').needsBaseUrl).toBeTrue();
    expect(aiProvider('openai_compatible').needsBaseUrl).toBeTrue();
  });

  it('needs a key only for Anthropic', () => {
    expect(AI_PROVIDERS.filter(p => !p.keyless).map(p => p.id)).toEqual(['anthropic']);
  });

  it('falls back to the server default for an unknown id', () => {
    expect(aiProvider('something-else').id).toBe('anthropic');
  });
});
