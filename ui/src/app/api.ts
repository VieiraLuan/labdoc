import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

// Caminho relativo: o nginx (ou o proxy do ng serve) encaminha /api para a API .NET.
// Assim o browser nunca fala com outra origem e nao existe problema de CORS.
const BASE = '/api/v1';

export interface AskSource {
  fileName: string;
  chunkIndex: number;
  score: number;
  excerpt: string;
}

export interface AskResponse {
  answer: string;
  sources: AskSource[];
  topK: number;
}

export interface DocumentSummary {
  id: string;
  fileName: string;
  description: string | null;
  sourceSystem: string | null;
  characterCount: number;
  chunkCount: number;
  embeddingModel: string;
  collectionName: string;
  chunkSize: number;
  chunkOverlap: number;
  status: string;
  hasFullText: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ExtractionPassResult {
  name: string;
  itemCount: number;
  seconds: number;
  error: string | null;
}

export interface ExtractionResponse {
  documentId: string;
  fileName: string;
  sectionCount: number;
  payload: unknown;
  passes: ExtractionPassResult[];
  schemaValid: boolean;
  schemaErrors: string[];
  seconds: number;
}

export interface IngestResponse {
  documentId: string;
  fileName: string;
  chunkCount: number;
  pointsCount: number;
  reused: boolean;
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  ingest(file: File, description: string, sourceSystem: string, force: boolean) {
    const form = new FormData();
    form.append('file', file);
    if (description) form.append('description', description);
    if (sourceSystem) form.append('sourceSystem', sourceSystem);
    form.append('force', String(force));

    return this.http.post<IngestResponse>(`${BASE}/ingest`, form);
  }

  ask(question: string, topK: number | null) {
    const body: Record<string, unknown> = { question };
    if (topK) body['topK'] = topK;

    return this.http.post<AskResponse>(`${BASE}/ask`, body);
  }

  documents() {
    return this.http.get<DocumentSummary[]>(`${BASE}/documents`);
  }

  extract(documentId: string, passes: string[]) {
    return this.http.post<ExtractionResponse>(`${BASE}/extract`, { documentId, passes });
  }
}
