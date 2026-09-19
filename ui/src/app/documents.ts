import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Api, DocumentSummary } from './api';

@Component({
  selector: 'app-documents',
  imports: [DatePipe, DecimalPipe],
  template: `
    <section class="card">
      <h2>
        Ingested documents
        <button class="link" (click)="load()" [disabled]="loading()">
          {{ loading() ? 'loading…' : 'refresh' }}
        </button>
      </h2>

      @if (error()) {
        <pre class="error">{{ error() }}</pre>
      } @else if (documents().length === 0 && !loading()) {
        <p class="hint">No documents ingested yet.</p>
      } @else {
        <p class="hint">
          {{ documents().length }} document(s) · {{ totalChunks() | number }} chunks ·
          {{ totalChars() | number }} characters
        </p>

        <div class="table-scroll">
          <table class="docs">
            <thead>
              <tr>
                <th>File</th>
                <th>Source</th>
                <th class="num">Chars</th>
                <th class="num">Chunks</th>
                <th class="num">Chunk/Ovl</th>
                <th>Model</th>
                <th>Status</th>
                <th>Text</th>
                <th>Ingested</th>
              </tr>
            </thead>
            <tbody>
              @for (doc of documents(); track doc.id) {
                <tr>
                  <td>
                    <div [title]="doc.fileName">{{ doc.fileName }}</div>
                    @if (doc.description) {
                      <div class="hint">{{ doc.description }}</div>
                    }
                    <div class="hint mono">{{ doc.id }}</div>
                  </td>
                  <td>{{ doc.sourceSystem || '—' }}</td>
                  <td class="num">{{ doc.characterCount | number }}</td>
                  <td class="num">{{ doc.chunkCount | number }}</td>
                  <td class="num mono">{{ doc.chunkSize }}/{{ doc.chunkOverlap }}</td>
                  <td class="mono">{{ doc.embeddingModel }}</td>
                  <td>
                    <span class="badge" [class.yes]="doc.status === 'completed'">{{ doc.status }}</span>
                  </td>
                  <td>
                    <!-- Without full_text the extraction tab cannot run: the
                         document was ingested before that column existed. -->
                    <span class="badge" [class.yes]="doc.hasFullText" [class.no]="!doc.hasFullText">
                      {{ doc.hasFullText ? 'full text' : 'chunks only' }}
                    </span>
                  </td>
                  <td class="hint">{{ doc.createdAt | date: 'dd/MM HH:mm' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        @if (missingFullText() > 0) {
          <p class="hint" style="margin-top:12px">
            ⚠ {{ missingFullText() }} document(s) without full text. Re-ingest with
            <span class="mono">force</span> checked to extract master data from them.
          </p>
        }
      }
    </section>
  `,
})
export class Documents {
  private readonly api = inject(Api);

  readonly documents = signal<DocumentSummary[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');

  readonly totalChunks = computed(() => this.documents().reduce((sum, d) => sum + d.chunkCount, 0));
  readonly totalChars = computed(() => this.documents().reduce((sum, d) => sum + d.characterCount, 0));
  readonly missingFullText = computed(() => this.documents().filter((d) => !d.hasFullText).length);

  constructor() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set('');

    this.api.documents().subscribe({
      next: (docs) => {
        this.documents.set(docs);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(typeof err.error === 'string' ? err.error : (err.message ?? 'Failed to list.'));
        this.loading.set(false);
      },
    });
  }
}
