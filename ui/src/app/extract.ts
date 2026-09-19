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
        <h2>Document</h2>

        @if (documents().length) {
          <label>Ingested Work Instruction</label>
          <select [(ngModel)]="documentId">
            @for (doc of documents(); track doc.id) {
              <option [value]="doc.id">{{ doc.fileName }} ({{ doc.characterCount }} chars)</option>
            }
          </select>
        } @else {
          <p class="hint">No documents ingested yet. Use the Ingest tab first.</p>
        }

        <label>Passes</label>
        <p class="hint">
          Each pass is one LLM call that extracts a single entity. Running just one
          is the way to inspect a result without waiting for the whole document.
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
          {{ loading() ? 'Extracting…' : 'Extract' }}
        </button>

        @if (loading()) {
          <p class="hint">
            One model call per pass, each bound to its schema. On a local model,
            expect tens of seconds per pass.
          </p>
        }
      </section>

      <section class="card">
        <h2>Result</h2>

        @if (error()) {
          <pre class="error">{{ error() }}</pre>
        } @else if (result(); as r) {
          <dl>
            <dt>File</dt><dd>{{ r.fileName }}</dd>
            <dt>Sections</dt><dd>{{ r.sectionCount }}</dd>
            <dt>Schema</dt>
            <dd>
              <span class="badge" [class.yes]="r.schemaValid" [class.no]="!r.schemaValid">
                {{ r.schemaValid ? 'valid' : r.schemaErrors.length + ' error(s)' }}
              </span>
            </dd>
            <dt>Time</dt><dd>{{ r.seconds.toFixed(1) }} s</dd>
          </dl>

          <h2 style="margin-top:18px">Passes</h2>
          <table class="passes-table">
            @for (pass of r.passes; track pass.name) {
              <tr [class.error]="!!pass.error">
                <td class="mono">{{ pass.name }}</td>
                <td>{{ pass.error ? '—' : pass.itemCount + ' items' }}</td>
                <td class="hint">{{ pass.seconds.toFixed(1) }} s</td>
              </tr>
              @if (pass.error) {
                <tr><td colspan="3" class="excerpt error">{{ pass.error }}</td></tr>
              }
            }
          </table>

          @if (r.schemaErrors.length) {
            <h2 style="margin-top:18px">Schema errors</h2>
            <pre class="error">{{ r.schemaErrors.join('\n') }}</pre>
          }

          <h2 style="margin-top:18px">Payload</h2>
          <pre>{{ pretty(r.payload) }}</pre>
        } @else {
          <p class="hint">No extraction yet.</p>
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
      error: () => this.error.set('Failed to list documents.'),
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

    // Keeps the canonical order, not the order the user clicked in.
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
