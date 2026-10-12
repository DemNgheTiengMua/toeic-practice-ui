# Two AI providers, both behind interfaces

**Azure AI Speech** transcribes speaking responses and scores pronunciation, fluency and prosody —
including unscripted speech. A transcript alone cannot carry pronunciation, and `speaking-review`
grades it, so speech-to-text without assessment is not enough.

**Claude** does the language work: rubric grading of writing and speaking (structured JSON, one call
per response) and the aggregate insights of ADR-0004. Calls go through the HH-tech proxy endpoint, with
`BaseUrl`, `ApiKey` and `Model` all in configuration — the default model is `claude-sonnet-5-5`. Totals,
scaled scores and bands are computed by our code from the returned per-criterion scores; the model is
never asked to do arithmetic that the schema already defines.

Learner writing and transcripts pass through a third-party proxy, which can therefore read them. The
owner accepted this for a coursework project.

## Consequences

- `IAiGrader` and `IAiInsightGenerator` hide the provider; a deterministic fake implementation backs
  automated tests and offline demos, and the real endpoint is one config change away.
- The exact model string sent is stored on each `AiGradings` / `AiInsights` row, so a result stays
  attributable after the configured model changes.
- "AI mentor" bookings are speaking-practice attempts through this same pipeline, not a new real-time
  conversation feature (that stays backlog) — which is what the prototype's card already promises:
  instant feedback and pronunciation analysis.
