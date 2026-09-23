import { Pipe, PipeTransform } from '@angular/core';
import { Marked, Renderer, Tokens } from 'marked';

/**
 * The assistant writes Markdown (bold, lists, inline code), which read as literal asterisks and
 * backticks until this rendered it.
 *
 * Output is bound with [innerHTML], so Angular's sanitizer still strips scripts, event handlers
 * and javascript: URLs. On top of that, raw HTML in the text is escaped rather than passed
 * through: a model that writes `<b>` or `<script>` gets it shown as text, never as markup.
 */
const renderer = new Renderer();
renderer.html = ({ text }: Tokens.HTML | Tokens.Tag) => escapeHtml(text);
// Links leave the app in a new tab; a reply must not navigate the chat away mid-conversation.
renderer.link = function ({ href, title, tokens }: Tokens.Link) {
  const text = this.parser.parseInline(tokens);
  const titleAttr = title ? ` title="${escapeHtml(title)}"` : '';
  return `<a href="${escapeHtml(href)}"${titleAttr} target="_blank" rel="noopener noreferrer">${text}</a>`;
};

const markdown = new Marked({ gfm: true, breaks: true, async: false, renderer });

@Pipe({ name: 'markdown' })
export class MarkdownPipe implements PipeTransform {
  transform(text: string | null | undefined): string {
    return text ? (markdown.parse(text) as string) : '';
  }
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
