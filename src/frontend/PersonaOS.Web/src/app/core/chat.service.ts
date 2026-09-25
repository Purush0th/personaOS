import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthService } from './auth.service';

/** What a tool actually did in a turn — the app's own record, not the model's claim. */
export interface ToolReceipt {
  tool: string;
  /** What ran, in the user's words ("Created goal"); the tool name is for the code only. */
  label: string;
  ok: boolean;
  summary?: string | null;
}

/** A data-changing action the assistant proposed; nothing has run until it is confirmed. */
export interface PendingAction {
  id: string;
  tool: string;
  summary: string;
  status: "pending" | "confirmed" | "discarded";
  resultSummary?: string | null;
  resultOk?: boolean | null;
}

export interface ChatEvent {
  type: 'start' | 'delta' | 'tool' | 'done' | 'error';
  text?: string;
  conversationId?: number;
  inputTokens?: number;
  outputTokens?: number;
  error?: string;
  toolName?: string;
  /** What the running tool is doing, to show: "Reading goals…". */
  toolLabel?: string;
  actions?: ToolReceipt[];
  pending?: PendingAction[];
  /** True when the reply says a change was made but no tool made one. */
  unverifiedClaim?: boolean;
  /** Item keys the reply names that do not exist, e.g. ["TASK-6"]. */
  unknownItems?: string[] | null;
}

export interface ConversationSummary {
  id: number;
  /** Opaque 8-char id used in URLs. */
  publicId: string;
  title: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ChatMessageDto {
  id: number;
  role: 'user' | 'assistant';
  content: string;
  inputTokens: number | null;
  outputTokens: number | null;
  createdAtUtc: string;
  toolActions?: ToolReceipt[] | null;
  pendingActions?: PendingAction[] | null;
  unverifiedClaim?: boolean;
  unknownItems?: string[] | null;
}

export interface ConversationDetail {
  id: number;
  publicId: string;
  title: string;
  createdAtUtc: string;
  messages: ChatMessageDto[];
}

@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  listConversations(): Promise<ConversationSummary[]> {
    return firstValueFrom(this.http.get<ConversationSummary[]>('/api/chat/conversations'));
  }

  /** Permanently deletes a conversation, its messages and any pending proposals. */
  deleteConversation(ref: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`/api/chat/conversations/${encodeURIComponent(ref)}`)
    );
  }

  confirmAction(id: string): Promise<PendingAction> {
    return firstValueFrom(
      this.http.post<PendingAction>(`/api/chat/actions/${encodeURIComponent(id)}/confirm`, {})
    );
  }

  discardAction(id: string): Promise<PendingAction> {
    return firstValueFrom(
      this.http.post<PendingAction>(`/api/chat/actions/${encodeURIComponent(id)}/discard`, {})
    );
  }

  /** Accepts a public id, or a numeric id for links predating public ids. */
  getConversation(ref: string): Promise<ConversationDetail> {
    return firstValueFrom(
      this.http.get<ConversationDetail>(`/api/chat/conversations/${encodeURIComponent(ref)}`)
    );
  }

  /**
   * Streams a reply. HttpClient buffers the whole response, and EventSource
   * cannot POST, so this uses fetch and reads the body incrementally.
   */
  async *streamChat(
    message: string,
    conversationId: number | null,
    signal?: AbortSignal
  ): AsyncGenerator<ChatEvent> {
    const response = await fetch('/api/chat', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${this.auth.accessToken}`,
      },
      body: JSON.stringify({ message, conversationId }),
      signal,
    });

    if (response.status === 401) {
      this.auth.sessionExpired();
      yield { type: 'error', error: 'Your session expired. Please log in again.' };
      return;
    }
    if (!response.body) {
      yield { type: 'error', error: 'The server returned an empty response.' };
      return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });

        // SSE frames are separated by a blank line; a partial frame stays buffered.
        const frames = buffer.split('\n\n');
        buffer = frames.pop() ?? '';

        for (const frame of frames) {
          const dataLine = frame.split('\n').find(line => line.startsWith('data: '));
          if (!dataLine) continue;
          yield JSON.parse(dataLine.slice(6)) as ChatEvent;
        }
      }
    } finally {
      reader.releaseLock();
    }
  }
}

/**
 * The note under a reply that names items which do not exist, or null when it names none.
 * Small models invent keys, and an invented TASK-6 reads as confidently as a real one.
 */
export function unknownItemsNote(keys: readonly string[] | null | undefined): string | null {
  if (!keys?.length) return null;
  const list = keys.length === 1 ? keys[0] : `${keys.slice(0, -1).join(', ')} and ${keys[keys.length - 1]}`;
  return `This reply mentions ${list}, which ${keys.length === 1 ? 'does' : 'do'} not exist. Check before relying on it.`;
}
