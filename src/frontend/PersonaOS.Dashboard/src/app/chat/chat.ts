import { Component, OnInit, effect, inject, signal, viewChild, ElementRef } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { BrandingService } from '../core/branding.service';
import { ChatService, ConversationSummary } from '../core/chat.service';

interface Bubble {
  role: 'user' | 'assistant';
  text: string;
  isError?: boolean;
  /** Tool currently running, shown while the assistant works. */
  tool?: string | null;
}

@Component({
  selector: 'app-chat',
  imports: [FormsModule],
  templateUrl: './chat.html',
  styleUrl: './chat.scss',
})
export class Chat implements OnInit {
  private readonly chat = inject(ChatService);
  protected readonly branding = inject(BrandingService);

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  protected readonly conversations = signal<ConversationSummary[]>([]);
  protected readonly bubbles = signal<Bubble[]>([]);
  protected readonly conversationId = signal<number | null>(null);
  protected readonly streaming = signal(false);

  draft = '';

  constructor() {
    // Keep the newest message in view as the reply streams in.
    effect(() => {
      this.bubbles();
      queueMicrotask(() => {
        const el = this.scroller()?.nativeElement;
        if (el) el.scrollTop = el.scrollHeight;
      });
    });
  }

  async ngOnInit(): Promise<void> {
    await this.refreshConversations();
  }

  protected async refreshConversations(): Promise<void> {
    try {
      this.conversations.set(await this.chat.listConversations());
    } catch {
      // A failed list shouldn't block composing a new message.
    }
  }

  protected startNew(): void {
    this.conversationId.set(null);
    this.bubbles.set([]);
  }

  protected async open(id: number): Promise<void> {
    const detail = await this.chat.getConversation(id);
    this.conversationId.set(detail.id);
    this.bubbles.set(detail.messages.map(m => ({ role: m.role, text: m.content })));
  }

  protected async send(): Promise<void> {
    const message = this.draft.trim();
    if (!message || this.streaming()) return;

    this.draft = '';
    this.streaming.set(true);
    this.bubbles.update(list => [
      ...list,
      { role: 'user', text: message },
      { role: 'assistant', text: '', tool: null },
    ]);

    const replyIndex = this.bubbles().length - 1;
    const patch = (change: Partial<Bubble>) =>
      this.bubbles.update(list =>
        list.map((bubble, i) => (i === replyIndex ? { ...bubble, ...change } : bubble))
      );

    try {
      for await (const event of this.chat.streamChat(message, this.conversationId())) {
        switch (event.type) {
          case 'start':
            this.conversationId.set(event.conversationId ?? null);
            break;
          case 'tool':
            patch({ tool: event.toolName ?? null });
            break;
          case 'delta':
            patch({ tool: null, text: this.bubbles()[replyIndex].text + (event.text ?? '') });
            break;
          case 'error':
            patch({ tool: null, isError: true, text: event.error ?? 'Something went wrong.' });
            break;
        }
      }
    } catch {
      patch({ tool: null, isError: true, text: 'The connection was interrupted.' });
    } finally {
      this.streaming.set(false);
      await this.refreshConversations();
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    // Enter sends; Shift+Enter adds a newline.
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }
}
