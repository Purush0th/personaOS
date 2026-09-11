import { Component, OnInit, effect, inject, signal, viewChild, ElementRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { BrandingService } from '../core/branding.service';
import { ChatService, ConversationSummary, ToolReceipt } from '../core/chat.service';
import { conversationRefFromSlug, conversationSlug } from '../core/conversation-slug';

interface Bubble {
  role: 'user' | 'assistant';
  text: string;
  isError?: boolean;
  /** Tool currently running, shown while the assistant works. */
  tool?: string | null;
  /** What the tools actually did — shown so the reply can be checked against it. */
  actions?: ToolReceipt[] | null;
}

@Component({
  selector: 'app-chat',
  imports: [FormsModule],
  templateUrl: './chat.html',
  styleUrl: './chat.scss',
})
export class Chat implements OnInit {
  private readonly chat = inject(ChatService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly branding = inject(BrandingService);

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  protected readonly conversations = signal<ConversationSummary[]>([]);
  protected readonly bubbles = signal<Bubble[]>([]);
  protected readonly conversationId = signal<number | null>(null);
  /** Public id of the open conversation; what appears in the URL. */
  private readonly conversationPublicId = signal<string | null>(null);
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
    // Subscribe BEFORE any await. Awaiting first leaves a window in which a message can be
    // sent and answered, and this subscription then fires late with no slug and wipes the
    // conversation that was just created — losing the thread on screen.
    // The URL is the source of truth for which conversation is open, so a link like
    // /chat/k3n9x2qp restores the thread and Back/Forward work.
    this.route.paramMap.subscribe(params => {
      const slug = params.get('slug');
      const ref = conversationRefFromSlug(slug);
      if (ref === null) {
        // Never clear a thread that is mid-reply: a late or spurious emission must not
        // throw away the conversation the user is currently talking to.
        if (this.streaming()) return;

        this.showEmptyThread();
        // A segment that cannot be a reference is a typo or a dead link; don't leave the
        // user sitting on a nonsense URL.
        if (slug) void this.router.navigate(['/chat'], { replaceUrl: true });
      } else if (ref !== this.conversationPublicId()) {
        void this.load(ref);
      }
    });

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
    void this.router.navigate(['/chat']);
  }

  /** Opening a conversation is a navigation; the route subscription does the loading. */
  protected open(conversation: ConversationSummary): void {
    void this.router.navigate(['/chat', conversationSlug(conversation.publicId, conversation.id)]);
  }

  private showEmptyThread(): void {
    this.conversationId.set(null);
    this.conversationPublicId.set(null);
    this.bubbles.set([]);
  }

  private async load(ref: string): Promise<void> {
    try {
      const detail = await this.chat.getConversation(ref);
      this.conversationId.set(detail.id);
      this.conversationPublicId.set(detail.publicId);
      this.bubbles.set(
        detail.messages.map(m => ({ role: m.role, text: m.content, actions: m.toolActions ?? null }))
      );

      // An old numeric link still resolves; quietly upgrade the address bar to the public id
      // so the title-free, non-sequential form is what gets copied or bookmarked next.
      if (detail.publicId && ref !== detail.publicId) {
        void this.router.navigate(['/chat', detail.publicId], { replaceUrl: true });
      }
    } catch {
      // A link to a conversation that no longer exists shouldn't strand the user on a
      // broken page — fall back to a fresh thread.
      this.showEmptyThread();
      void this.router.navigate(['/chat'], { replaceUrl: true });
    }
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
          case 'done':
            // Text on 'done' means the stored reply differs from the deltas we streamed
            // (the server stripped a tool call the model wrote as prose). Replace the
            // bubble so the view matches history instead of showing raw internals.
            // Actions are what the tools actually did, so the reply can be checked.
            patch({
              tool: null,
              actions: event.actions ?? null,
              ...(event.text ? { text: event.text } : {}),
            });
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
      this.syncUrlToConversation();
    }
  }

  /**
   * After the first message of a new thread the server has assigned an id and auto-titled it,
   * so put the slug in the URL — replacing history rather than pushing, since the user did not
   * navigate. Until this runs the address bar still says /chat, which is not linkable.
   */
  private syncUrlToConversation(): void {
    const id = this.conversationId();
    if (id === null) return;

    // The list was just refreshed, so the new conversation is in it with its public id.
    const conversation = this.conversations().find(c => c.id === id);
    const slug = conversationSlug(conversation?.publicId, id);
    if (!slug) return;

    this.conversationPublicId.set(conversation?.publicId ?? null);
    if (conversationRefFromSlug(this.route.snapshot.paramMap.get('slug')) === slug) return;

    void this.router.navigate(['/chat', slug], { replaceUrl: true });
  }

  protected onKeydown(event: KeyboardEvent): void {
    // Enter sends; Shift+Enter adds a newline.
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }
}
