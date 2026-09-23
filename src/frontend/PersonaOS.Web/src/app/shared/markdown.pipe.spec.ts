import { MarkdownPipe } from './markdown.pipe';

describe('MarkdownPipe', () => {
  const render = (text: string | null | undefined) => new MarkdownPipe().transform(text);

  it('renders the Markdown the assistant actually writes', () => {
    const html = render('**SPRINT-1** has `TASK-1`:\n- File the tax return\n- Renew insurance');

    expect(html).toContain('<strong>SPRINT-1</strong>');
    expect(html).toContain('<code>TASK-1</code>');
    expect(html).toContain('<li>File the tax return</li>');
  });

  it('keeps single line breaks, as a chat reply means them', () => {
    expect(render('first\nsecond')).toContain('first<br>second');
  });

  it('shows raw HTML as text instead of rendering it', () => {
    const html = render('before <script>alert(1)</script> <b>bold</b>');

    expect(html).not.toContain('<script>');
    expect(html).not.toContain('<b>');
    expect(html).toContain('&lt;script&gt;');
  });

  it('opens links in a new tab without handing over the opener', () => {
    const html = render('[docs](https://example.com "Docs")');

    expect(html).toContain('href="https://example.com"');
    expect(html).toContain('target="_blank"');
    expect(html).toContain('rel="noopener noreferrer"');
  });

  it('escapes quotes in a link so the attribute cannot be broken out of', () => {
    const html = render('[x](https://example.com/"onmouseover="alert(1))');

    expect(html).not.toContain('" onmouseover=');
  });

  it('returns an empty string for nothing', () => {
    expect(render('')).toBe('');
    expect(render(null)).toBe('');
    expect(render(undefined)).toBe('');
  });
});
