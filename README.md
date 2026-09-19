# LabDoc

Two LLM pipelines over the same lab documents, in .NET 10 — no RAG framework, no paid API.

Upload a laboratory procedure (PDF). Then either **ask questions about it**, or **turn it into
structured master data**. Those look like the same problem. They are not, and the difference is
the point of this project.

---

## Why two pipelines

|  | Ask (RAG) | Extract |
|---|---|---|
| Input | a question | **the whole document** |
| Strategy | retrieve the few relevant chunks | iterate over all of it |
| Output | prose with citations | JSON validated against a schema |
| Success | correct answer | **coverage** — no parameter silently lost |
| Pattern | retrieval-augmented generation | map-reduce |

RAG retrieves because the corpus is large and the question is narrow. Extraction cannot retrieve:
if the search returns the top 4 chunks of a work instruction, you **lose parameters without
noticing**, and a plausible-but-incomplete payload is worse than an obviously broken one.

So the extraction pipeline uses no embeddings and no vector search at all. Same ingest, same
storage, different orchestration.

```
                    ┌─ PDF ─→ text ─→ full_text + sections ─→ Postgres
   ingest ──────────┤
                    └─ chunks ─→ bge-m3 ─→ Qdrant (deterministic ids)

   ask       question ─→ bge-m3 ─→ Qdrant top-K ─→ numbered context ─→ LLM ─→ answer + sources

   extract   document ─→ sections ─┬─→ pass 1 (schema) ─→ LLM ─┐
                                   ├─→ pass 2 (schema) ─→ LLM ─┼─→ envelope ─→ JSON Schema ─→ payload
                                   └─→ pass N (schema) ─→ LLM ─┘
```

## Stack

| | |
|---|---|
| API | .NET 10 minimal API, Scalar for OpenAPI |
| Embeddings | `bge-m3` (1024-d, multilingual) via Ollama |
| Generation | `qwen2.5:7b` via Ollama, OpenAI-compatible endpoint |
| Vector store | Qdrant, gRPC, cosine |
| Metadata | PostgreSQL 17 |
| UI | Angular 22, standalone components + signals |
| Validation | JsonSchema.Net |

Everything runs locally. No API keys, no cost.

## Run it

Requires Docker and [Ollama](https://ollama.com).

```bash
ollama pull bge-m3
ollama pull qwen2.5:7b

docker compose --profile api up -d --build
```

| | |
|---|---|
| UI | http://localhost:4301 |
| API + Scalar docs | http://localhost:8081/scalar |
| Qdrant dashboard | http://localhost:6335/dashboard |

A sample work instruction is in [`samples/`](samples/). Ingest it, then try both tabs.

```bash
curl -F "file=@samples/WI-CHM-001.pdf" -F "sourceSystem=WI" \
     http://localhost:8081/api/v1/ingest

curl -X POST http://localhost:8081/api/v1/ask \
     -H 'Content-Type: application/json' \
     -d '{"question":"What PPE is required, and what is the cell drift limit?","topK":3}'
```

## Measured

On an M-series Mac, 24 GB, `qwen2.5:7b` and `bge-m3` running locally.

| | |
|---|---|
| Ingest, 135-page manual | 231,668 chars → 343 chunks → 343 vectors, ~60 s |
| Re-ingest of the same file | **60.10 s → 0.04 s** (content-hash identity) |
| Cross-lingual retrieval | Portuguese question, English manual → 0.6798 on the correct chunk |
| Full extraction, sample WI | 9 passes, 804 s |

## Design decisions worth reading

**Ingest identity is a composite key.** `content_hash + embedding model + collection + chunk size
+ chunk overlap`. Re-uploading the same file is free; changing anything that affects the vectors
reprocesses it. [`RagIngestService.cs`](src/LabDoc.Api/Services/RagIngestService.cs)

**Point ids are deterministic** — `SHA256(documentId:chunkIndex)` — so an upsert overwrites
instead of duplicating. [`QdrantVectorStore.cs`](src/LabDoc.Api/Services/QdrantVectorStore.cs)

**Guard rails come in two kinds.** Empty search result → short-circuit in C#, never call the LLM:
that is a *guarantee*. "Say you don't know" in the system prompt is a *request*. Never rely on the
prompt for something you can check in code. [`RagAskService.cs`](src/LabDoc.Api/Services/RagAskService.cs)

**Extraction passes are data, not code.** Each entity of the contract is one entry: what to
extract, the JSON Schema fragment that constrains it, which sections feed it. Adding an entity
means adding a list item. [`MasterDataPasses.cs`](src/LabDoc.Api/Services/MasterDataPasses.cs)

**Structured output is grammar-constrained, not prompted.** `response_format: json_schema` makes
tokens outside the schema impossible to emit — a different guarantee from asking for JSON politely.
[`ChatService.cs`](src/LabDoc.Api/Services/ChatService.cs)

**Chunking differs per pipeline.** RAG chunks by character count; extraction splits on numbered
sections, because the target model is addressed by section.
[`WorkInstructionParser.cs`](src/LabDoc.Api/Services/WorkInstructionParser.cs)

**There is a human review queue.** `unresolved[]` is where the model puts what it recognised but
could not map. Somewhere to put uncertainty is what stops a model from inventing a field.

## Known limitations

Measured, not hidden:

- **Identifiers drift between passes** — `KF_CELL_DRIFT` in one, `CELL_DRIFT` in another.
  Independent map calls share no vocabulary. The fix is to chain earlier results into later
  prompts, or reconcile in the reduce step.
- **The `specifications` pass is the weak one** — deeply nested schema, 397 s of the 804, and the
  worst accuracy of the nine. Schema depth costs latency *and* quality.
- **No evaluation harness yet.** Without a golden set, prompt changes are guesswork. This is the
  next thing to build.
- **A 7B model is the ceiling here.** It correctly modelled replicate statistics in one pass and
  got them wrong in another — the same rule, a different prompt.
- **Extraction is a synchronous HTTP request** that can run for minutes. It should be
  `202 Accepted` + a job id.
- **Cosine scores are not probabilities.** In one corpus the ceiling was 0.68 and pure gibberish
  scored 0.45 — higher than a coherent out-of-domain question. Score thresholds are a trap; a
  reranker is the real answer.

## Layout

```
src/LabDoc.Api/
  Services/          ingest, chunking, embeddings, vector store, ask, extraction
  Schemas/           the master data contract (JSON Schema)
  Interfaces/        one per capability, swappable
ui/                  Angular SPA: ingest, documents, ask, extract
samples/             a synthetic work instruction to try it on
```

## License

MIT
