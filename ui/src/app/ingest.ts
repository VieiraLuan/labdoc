import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, IngestResponse } from './api';

@Component({
  selector: 'app-ingest',
  imports: [FormsModule],
  template: `
    <div class="grid">
      <section class="card">
        <h2>Upload document</h2>

        <label>Work Instruction (PDF)</label>
        <input type="file" accept=".pdf" (change)="onFile($event)" />

        <label>Description</label>
        <input type="text" [(ngModel)]="description" placeholder="Karl Fischer water content" />

        <label>Source system</label>
        <input type="text" [(ngModel)]="sourceSystem" placeholder="work-instructions" />

        <label class="inline">
          <input type="checkbox" [(ngModel)]="force" />
          Force reprocessing (bypass the content-hash cache)
        </label>

        <button (click)="send()" [disabled]="!file() || loading()">
          {{ loading() ? 'Processing…' : 'Ingest' }}
        </button>

        @if (loading()) {
          <p class="hint">Extracting text, chunking and generating embeddings. A few seconds per document.</p>
        }
      </section>

      <section class="card">
        <h2>Result</h2>

        @if (error()) {
          <pre class="error">{{ error() }}</pre>
        } @else if (result(); as r) {
          <dl>
            <dt>Document</dt><dd class="mono">{{ r.documentId }}</dd>
            <dt>File</dt><dd>{{ r.fileName }}</dd>
            <dt>Chunks</dt><dd>{{ r.chunkCount }}</dd>
            <dt>Points in Qdrant</dt><dd>{{ r.pointsCount }}</dd>
            <dt>Reused</dt>
            <dd>
              <span class="badge" [class.yes]="r.reused" [class.no]="!r.reused">
                {{ r.reused ? 'yes — nothing was reprocessed' : 'no — vectors generated now' }}
              </span>
            </dd>
            <dt>Time</dt><dd>{{ elapsed() }} s</dd>
          </dl>
        } @else {
          <p class="hint">No upload yet.</p>
        }
      </section>
    </div>
  `,
})
export class Ingest {
  private readonly api = inject(Api);

  readonly file = signal<File | null>(null);
  description = '';
  sourceSystem = '';
  force = false;

  readonly loading = signal(false);
  readonly result = signal<IngestResponse | null>(null);
  readonly error = signal<string | null>(null);
  readonly elapsed = signal('0.00');

  onFile(event: Event) {
    const input = event.target as HTMLInputElement;
    this.file.set(input.files?.[0] ?? null);
  }

  send() {
    const file = this.file();
    if (!file) return;

    this.loading.set(true);
    this.error.set(null);
    this.result.set(null);
    const started = performance.now();

    this.api.ingest(file, this.description, this.sourceSystem, this.force).subscribe({
      next: (response) => {
        this.elapsed.set(((performance.now() - started) / 1000).toFixed(2));
        this.result.set(response);
        this.loading.set(false);
      },
      error: (err) => {
        this.elapsed.set(((performance.now() - started) / 1000).toFixed(2));
        this.error.set(typeof err.error === 'string' ? err.error : JSON.stringify(err.error ?? err.message, null, 2));
        this.loading.set(false);
      },
    });
  }
}
