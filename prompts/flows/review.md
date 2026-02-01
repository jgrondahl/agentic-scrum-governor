REVIEW FLOW — GOVERNED OUTPUT

Purpose:
- Determine whether a backlog item is actually Done.

Non-negotiable:
- Output MUST be a SINGLE JSON object.
- No markdown. No prose outside JSON.

Produce:
- itemId (integer)
- pass (boolean)
- failures (array of strings)
- evidenceRequired (array of strings)
- regressionRisks (array of strings)
- followUpTasks (array of strings)

If evidence is missing, pass must be false.
