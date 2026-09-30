import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** The optional speech service, as the server reports it. The key itself never comes back. */
export interface SpeechStatus {
  speechToText: boolean;
  textToSpeech: boolean;
  baseUrl: string | null;
  sttModel: string | null;
  ttsModel: string | null;
  ttsVoice: string | null;
  hasApiKey: boolean;
}

/** Fields to change; omitted ones stay, an empty string clears one. */
export interface SpeechSettings {
  baseUrl?: string;
  sttModel?: string;
  ttsModel?: string;
  ttsVoice?: string;
  apiKey?: string;
}

@Injectable({ providedIn: 'root' })
export class SpeechService {
  private readonly http = inject(HttpClient);

  status(): Promise<SpeechStatus> {
    return firstValueFrom(this.http.get<SpeechStatus>('/api/speech'));
  }

  update(settings: SpeechSettings): Promise<SpeechStatus> {
    return firstValueFrom(this.http.put<SpeechStatus>('/api/speech', settings));
  }

  /** Tries the service with these (unsaved) settings; a failure comes back as ok=false with the reason. */
  test(settings: SpeechSettings): Promise<{ ok: boolean; message: string }> {
    return firstValueFrom(this.http.post<{ ok: boolean; message: string }>('/api/speech/test', settings));
  }
}
