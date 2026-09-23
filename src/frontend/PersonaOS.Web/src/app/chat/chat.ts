import { Component, OnInit, effect, inject, signal, viewChild, ElementRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

import { BrandingService } from '../core/branding.service';
import { ChatService, PendingAction, ToolReceipt, unknownItemsNote } from '../core/chat.service';
import { ConversationsStore } from '../core/conversations.store';
import { conversationRefFromSlug, conversationSlug } from '../core/conversation-slug';
import { MarkdownPipe } from '../shared/markdown.pipe';

interface Bubble {
  role: 'user' | 'assistant';
  text: string;
  isError?: boolean;
  /** Tool currently running, shown while the assistant works. */
  tool?: string | null;
  /** Data-changing actions awaiting the user's confirmation. */
  pending?: PendingAction[] | null;
  /** What the tools actually did — shown so the reply can be checked against it. */
  actions?: ToolReceipt[] | null;
  /** The reply says something was changed, but no tool changed anything. */
  unverifiedClaim?: boolean;
  /** Item keys the reply names that do not exist. */
  unknownItems?: string[] | null;
}

@Component({
  selector: 'app-chat',
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MarkdownPipe,
  ],
  templateUrl: './chat.html',
  styleUrl: './chat.scss',
})
export class Chat implements OnInit {
  private readonly chat = inject(ChatService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly branding = inject(BrandingService);
  /** The history lives in the nav; this page only adds to it. */
  private readonly store = inject(ConversationsStore);

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  protected readonly bubbles = signal<Bubble[]>([]);
  protected readonly conversationId = signal<number | null>(null);
  /** Public id of the open conversation; what appears in the URL. */
  private readonly conversationPublicId = signal<string | null>(null);
  protected readonly streaming = signal(false);
  /** Action ids with a confirm/discard in flight, so the buttons can't be double-tapped. */
  private readonly resolving = signal(new Set<string>());

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

  ngOnInit(): void {
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
        detail.messages.map(m => ({
          role: m.role,
          text: m.content,
          actions: m.toolActions ?? null,
          pending: m.pendingActions ?? null,
          unverifiedClaim: m.unverifiedClaim ?? false,
          unknownItems: m.unknownItems ?? null,
        }))
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
              pending: event.pending ?? null,
              unverifiedClaim: event.unverifiedClaim ?? false,
              unknownItems: event.unknownItems ?? null,
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
      await this.store.refresh();
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
    const conversation = this.store.conversations().find(c => c.id === id);
    const slug = conversationSlug(conversation?.publicId, id);
    if (!slug) return;

    this.conversationPublicId.set(conversation?.publicId ?? null);
    if (conversationRefFromSlug(this.route.snapshot.paramMap.get('slug')) === slug) return;

    void this.router.navigate(['/chat', slug], { replaceUrl: true });
  }

  /**
   * Confirm or discard a proposed change. Nothing has touched the database until Confirm —
   * the assistant cannot write on its own say-so.
   */
  protected async resolveAction(action: PendingAction, confirm: boolean): Promise<void> {
    if (action.status !== 'pending' || this.resolving().has(action.id)) return;

    this.resolving.update(set => new Set(set).add(action.id));
    try {
      const updated = confirm
        ? await this.chat.confirmAction(action.id)
        : await this.chat.discardAction(action.id);

      // Replace in place so the card becomes a receipt without reloading the thread.
      this.bubbles.update(list =>
        list.map(bubble => ({
          ...bubble,
          pending: bubble.pending?.map(p => (p.id === action.id ? updated : p)) ?? null,
        }))
      );
    } catch {
      // Leave the card pending so the user can try again rather than losing the action.
    } finally {
      this.resolving.update(set => {
        const next = new Set(set);
        next.delete(action.id);
        return next;
      });
    }
  }

  protected readonly unknownItemsNote = unknownItemsNote;

  protected isResolving(id: string): boolean {
    return this.resolving().has(id);
  }

  protected onKeydown(event: KeyboardEvent): void {
    // Enter sends; Shift+Enter adds a newline.
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }
}
