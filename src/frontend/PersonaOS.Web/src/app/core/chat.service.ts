import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthService } from './auth.service';

/** What a tool actually did in a turn — the app's own record, not the model's claim. */
export interface ToolReceipt {
  tool: string;
  ok: boolean;
  summary?: string | null;
}

export interface ChatEvent {
  type: 'start' | 'delta' | 'tool' | 'done' | 'error';
  text?: string;
  conversationId?: number;
  inputTokens?: number;
  outputTokens?: number;
  error?: string;
  toolName?: string;
  actions?: ToolReceipt[];
}

export interface ConversationSummary {
  id: number;
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
}

export interface ConversationDetail {
  id: number;
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

  getConversation(id: number): Promise<ConversationDetail> {
    return firstValueFrom(this.http.get<ConversationDetail>(`/api/chat/conversations/${id}`));
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
