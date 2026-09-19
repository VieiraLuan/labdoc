import { DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, AskSource } from './api';

interface Turn {
  question: string;
  answer: string;
  sources: AskSource[];
  topK: number | null;
  failed: boolean;
  seconds: string;
}

@Component({
  selector: 'app-chat',
  imports: [FormsModule, DecimalPipe],
  template: `
    <div class="chat">
      <div class="log">
        @for (turn of turns(); track $index) {
          <div class="turn">
            <p class="q">{{ turn.question }}</p>

            @if (turn.failed) {
              <pre class="error">{{ turn.answer }}</pre>
            } @else {
              <div class="a">{{ turn.answer }}</div>

              @if (turn.sources.length) {
                <details class="sources">
                  <summary>{{ turn.sources.length }} retrieved chunks</summary>

                  @for (source of turn.sources; track $index) {
                    <div class="source">
                      <div class="source-head">
                        <span class="cite">[{{ $index + 1 }}]</span>
                        <span class="file mono" [title]="source.fileName">{{ source.fileName }}</span>
                        <span class="hint">#{{ source.chunkIndex }}</span>
                        <span class="score mono">{{ source.score | number: '1.4-4' }}</span>
                      </div>
                      <!-- The bar is just a visual read of the cosine score (0 to 1).
                           The practical ceiling sits near 0.7, so it never fills. -->
                      <div class="bar"><i [style.width.%]="source.score * 100"></i></div>
                      <p class="excerpt">{{ source.excerpt }}</p>
                    </div>
                  }
                </details>
              }
            }

            <span class="hint">{{ turn.seconds }} s · topK {{ turn.topK }}</span>
          </div>
        } @empty {
          @if (!loading()) {
            <p class="hint">Ask a question about the ingested documents.</p>
          }
        }

        <!-- The local model takes tens of seconds: without this block the screen
             sits still and looks frozen. -->
        @if (loading()) {
          <div class="turn pending">
            <p class="q">{{ pending() }}</p>
            <div class="hint">searching the documents…</div>
          </div>
        }
      </div>

      <div class="composer">
        <input
          type="text"
          [(ngModel)]="question"
          (keyup.enter)="send()"
          placeholder="What PPE is required, and what is the cell drift limit?" />
        <input type="number" [(ngModel)]="topK" min="1" max="20" class="topk" title="TopK" />
        <button (click)="send()" [disabled]="loading() || !question.trim()">
          {{ loading() ? '…' : 'Ask' }}
        </button>
      </div>
    </div>
  `,
})
export class Chat {
  private readonly api = inject(Api);

  question = '';
  topK: number | null = 4;

  readonly loading = signal(false);
  readonly pending = signal('');
  readonly turns = signal<Turn[]>([]);

  send() {
    const question = this.question.trim();
    if (!question) return;

    const topK = this.topK;
    const started = performance.now();

    this.loading.set(true);
    this.pending.set(question);
    this.question = '';

    this.api.ask(question, topK).subscribe({
      next: (response) =>
        this.push({
          question,
          answer: response.answer,
          sources: response.sources ?? [],
          topK: response.topK,
          failed: false,
          seconds: this.elapsed(started),
        }),
      error: (err) =>
        this.push({
          question,
          answer: this.describe(err),
          sources: [],
          topK,
          failed: true,
          seconds: this.elapsed(started),
        }),
    });
  }

  private push(turn: Turn) {
    this.turns.update((turns) => [...turns, turn]);
    this.pending.set('');
    this.loading.set(false);
  }

  private elapsed(started: number) {
    return ((performance.now() - started) / 1000).toFixed(2);
  }

  // The API's ProblemDetails arrives in err.error as an object; an nginx 502
  // arrives as a string. Both have to become readable text.
  private describe(err: unknown): string {
    const error = (err as { error?: unknown; message?: string });

    if (typeof error.error === 'string') return error.error;
    if (error.error) return JSON.stringify(error.error, null, 2);

    return error.message ?? 'Failed to call the API.';
  }
}
