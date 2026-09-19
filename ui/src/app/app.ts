import { Component, signal } from '@angular/core';
import { Chat } from './chat';
import { Documents } from './documents';
import { Extract } from './extract';
import { Ingest } from './ingest';

type Tab = 'ingest' | 'documents' | 'chat' | 'extract';

@Component({
  imports: [Ingest, Documents, Chat, Extract],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  readonly tab = signal<Tab>('ingest');

  select(tab: Tab) {
    this.tab.set(tab);
  }
}
