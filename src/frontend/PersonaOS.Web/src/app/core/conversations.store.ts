import { Injectable, Injector, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { ChatService, ConversationSummary } from './chat.service';
import { conversationSlug } from './conversation-slug';

/**
 * The conversation list, shared by the nav (which shows it as chat history) and the chat page
 * (which adds to it by sending). One copy, so a new thread appears in the nav the moment the
 * server has titled it.
 */
@Injectable({ providedIn: 'root' })
export class ConversationsStore {
  private readonly chat = inject(ChatService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);

  readonly conversations = signal<ConversationSummary[]>([]);

  async refresh(): Promise<void> {
    try {
      this.conversations.set(await this.chat.listConversations());
    } catch {
      // A failed list shouldn't block composing a new message.
    }
  }

  slug(conversation: ConversationSummary): string {
    return conversationSlug(conversation.publicId, conversation.id);
  }

  /**
   * Permanently removes a conversation after asking. If it is the one on screen, the URL returns
   * to /chat, which the chat page reads as "show an empty thread".
   */
  async remove(conversation: ConversationSummary): Promise<void> {
    // Loaded on first use: this store ships with the nav on every page, and the dialog code
    // would otherwise add ~100 kB to the first load for a button most visits never press.
    const confirm = this.injector.get((await import('./confirm')).Confirm);
    const ok = await confirm.ask({
      title: 'Delete this conversation?',
      message: `“${conversation.title}” and everything in it. This cannot be undone.`,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;

    try {
      await this.chat.deleteConversation(conversation.publicId);
    } catch {
      confirm.error('Could not delete that conversation.');
      return; // Leave the list untouched; the conversation is still there.
    }

    await this.refresh();
    if (this.router.url.split(/[?#]/)[0] === `/chat/${this.slug(conversation)}`) {
      void this.router.navigate(['/chat'], { replaceUrl: true });
    }
  }
}
