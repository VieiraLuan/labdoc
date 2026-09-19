import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Api, DocumentSummary } from './api';

@Component({
  selector: 'app-documents',
  imports: [DatePipe, DecimalPipe],
  template: `
    <section class="card">
      <h2>
        Documentos ingeridos
        <button class="link" (click)="load()" [disabled]="loading()">
          {{ loading() ? 'carregando…' : 'atualizar' }}
        </button>
      </h2>

      @if (error()) {
        <pre class="error">{{ error() }}</pre>
      } @else if (documents().length === 0 && !loading()) {
        <p class="hint">Nenhum documento ingerido ainda.</p>
      } @else {
        <p class="hint">
          {{ documents().length }} documento(s) · {{ totalChunks() | number }} chunks ·
          {{ totalChars() | number }} caracteres
        </p>

        <div class="table-scroll">
          <table class="docs">
            <thead>
              <tr>
                <th>Arquivo</th>
                <th>Origem</th>
                <th class="num">Chars</th>
                <th class="num">Chunks</th>
                <th class="num">Chunk/Ovl</th>
                <th>Modelo</th>
                <th>Status</th>
                <th>Texto</th>
                <th>Ingerido</th>
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
                    <!-- Sem full_text a aba de extracao nao consegue rodar:
                         o documento foi ingerido antes da coluna existir. -->
                    <span class="badge" [class.yes]="doc.hasFullText" [class.no]="!doc.hasFullText">
                      {{ doc.hasFullText ? 'completo' : 'so chunks' }}
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
            ⚠ {{ missingFullText() }} documento(s) sem texto completo. Re-ingira com
            <span class="mono">force</span> marcado para poder extrair master data deles.
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
        this.error.set(typeof err.error === 'string' ? err.error : (err.message ?? 'Falha ao listar.'));
        this.loading.set(false);
      },
    });
  }
}
