import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { DocumentDto, DocumentsService } from '../core/documents.service';

@Component({
  selector: 'app-documents',
  imports: [FormsModule],
  templateUrl: './documents.html',
  styleUrl: './documents.scss',
})
export class Documents implements OnInit {
  private readonly documents = inject(DocumentsService);

  protected readonly items = signal<DocumentDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly uploading = signal(false);
  protected readonly error = signal<string | null>(null);

  search = '';

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    try {
      this.items.set(await this.documents.list(this.search.trim() || undefined));
      this.error.set(null);
    } catch {
      this.error.set('Could not load documents.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.uploading.set(true);
    try {
      await this.documents.upload(file);
      await this.reload();
    } catch (e: unknown) {
      this.error.set(this.messageFrom(e, 'Could not upload that file.'));
    } finally {
      this.uploading.set(false);
      input.value = ''; // let the same file be re-picked
    }
  }

  protected async download(doc: DocumentDto): Promise<void> {
    try {
      await this.documents.download(doc);
    } catch {
      this.error.set('Could not download that file.');
    }
  }

  protected async remove(doc: DocumentDto): Promise<void> {
    try {
      await this.documents.delete(doc.id);
      await this.reload();
    } catch {
      this.error.set('Could not delete that file.');
    }
  }

  protected formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  private messageFrom(error: unknown, fallback: string): string {
    return (error as { error?: { error?: string } })?.error?.error ?? fallback;
  }
}
