import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, DocumentSummary, ExtractionResponse } from './api';

// Mirrors the dependency order of the target model.
const PASSES = [
  'source',
  'units',
  'parameters',
  'limitTypes',
  'parameterLists',
  'specifications',
  'testMethods',
  'references',
  'unresolved',
];

@Component({
  selector: 'app-extract',
  imports: [FormsModule],
  template: `
    <div class="grid">
      <section class="card">
        <h2>Documento</h2>

        @if (documents().length) {
          <label>Work Instruction ingerida</label>
          <select [(ngModel)]="documentId">
            @for (doc of documents(); track doc.id) {
              <option [value]="doc.id">{{ doc.fileName }} ({{ doc.characterCount }} chars)</option>
            }
          </select>
        } @else {
          <p class="hint">Nenhum documento ingerido ainda. Use a aba Ingest primeiro.</p>
        }

        <label>Passadas</label>
        <p class="hint">
          Cada passada e uma chamada ao LLM que extrai uma entidade. Rodar uma so
          e o jeito de estudar o resultado sem esperar o documento inteiro.
        </p>

        <div class="passes">
          @for (pass of allPasses; track pass) {
            <label class="inline">
              <input type="checkbox" [checked]="selected().has(pass)" (change)="toggle(pass)" />
              {{ pass }}
            </label>
          }
        </div>

        <button (click)="run()" [disabled]="!documentId || loading() || !selected().size">
          {{ loading() ? 'Extraindo…' : 'Extrair' }}
        </button>

        @if (loading()) {
          <p class="hint">
            Uma chamada ao modelo por passada, com o schema amarrado. Modelo local:
            conte dezenas de segundos por passada.
          </p>
        }
      </section>

      <section class="card">
        <h2>Resultado</h2>

        @if (error()) {
          <pre class="error">{{ error() }}</pre>
        } @else if (result(); as r) {
          <dl>
            <dt>Arquivo</dt><dd>{{ r.fileName }}</dd>
            <dt>Secoes</dt><dd>{{ r.sectionCount }}</dd>
            <dt>Schema</dt>
            <dd>
              <span class="badge" [class.yes]="r.schemaValid" [class.no]="!r.schemaValid">
                {{ r.schemaValid ? 'valido' : r.schemaErrors.length + ' erro(s)' }}
              </span>
            </dd>
            <dt>Tempo</dt><dd>{{ r.seconds.toFixed(1) }} s</dd>
          </dl>

          <h2 style="margin-top:18px">Passadas</h2>
          <table class="passes-table">
            @for (pass of r.passes; track pass.name) {
              <tr [class.error]="!!pass.error">
                <td class="mono">{{ pass.name }}</td>
                <td>{{ pass.error ? '—' : pass.itemCount + ' itens' }}</td>
                <td class="hint">{{ pass.seconds.toFixed(1) }} s</td>
              </tr>
              @if (pass.error) {
                <tr><td colspan="3" class="excerpt error">{{ pass.error }}</td></tr>
              }
            }
          </table>

          @if (r.schemaErrors.length) {
            <h2 style="margin-top:18px">Erros de schema</h2>
            <pre class="error">{{ r.schemaErrors.join('\n') }}</pre>
          }

          <h2 style="margin-top:18px">Payload</h2>
          <pre>{{ pretty(r.payload) }}</pre>
        } @else {
          <p class="hint">Nenhuma extracao ainda.</p>
        }
      </section>
    </div>
  `,
})
export class Extract {
  private readonly api = inject(Api);

  readonly allPasses = PASSES;
  readonly documents = signal<DocumentSummary[]>([]);
  readonly selected = signal(new Set(PASSES));
  readonly loading = signal(false);
  readonly result = signal<ExtractionResponse | null>(null);
  readonly error = signal('');

  documentId = '';

  constructor() {
    this.api.documents().subscribe({
      next: (docs) => {
        this.documents.set(docs);
        if (docs.length) this.documentId = docs[0].id;
      },
      error: () => this.error.set('Falha ao listar os documentos.'),
    });
  }

  toggle(pass: string) {
    this.selected.update((current) => {
      const next = new Set(current);
      next.has(pass) ? next.delete(pass) : next.add(pass);
      return next;
    });
  }

  run() {
    this.loading.set(true);
    this.error.set('');
    this.result.set(null);

    // Mantem a ordem canonica, nao a ordem em que o usuario clicou.
    const passes = this.allPasses.filter((pass) => this.selected().has(pass));

    this.api.extract(this.documentId, passes).subscribe({
      next: (response) => {
        this.result.set(response);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(
          typeof err.error === 'string' ? err.error : JSON.stringify(err.error ?? err.message, null, 2),
        );
        this.loading.set(false);
      },
    });
  }

  pretty(payload: unknown) {
    return JSON.stringify(payload, null, 2);
  }
}
