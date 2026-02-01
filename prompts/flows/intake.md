INTAKE FLOW — GOVERNED OUTPUT

Purpose:
- Convert a raw idea into a single backlog candidate.

Non-negotiable:
- Output MUST be a SINGLE JSON object.
- No markdown. No prose outside JSON.

Produce:
- title (string)
- story (string)
- acceptance_criteria (array of strings)
- non_goals (array of strings)
- dependencies (array of strings)
- risks (array of strings)
- owner (one of: PO, SAD, SASD, QA, MIBS)
- size (one of: S, M, L)
- priority (integer >= 1)

If uncertain, include a best-effort placeholder rather than omitting fields.
