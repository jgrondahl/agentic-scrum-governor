SPRINT PLANNING FLOW — GOVERNED OUTPUT

Purpose:
- Select a sprint goal and a small set of backlog items that can ship together.

Non-negotiable:
- Output MUST be a SINGLE JSON object.
- No markdown. No prose outside JSON.

Produce:
- sprintGoal (string)
- selectedItemIds (array of integers)
- rationale (array of strings)
- risks (array of strings)
- assumptions (array of strings)
- definitionOfDoneChecklist (array of strings)

Do not increase scope. Prefer fewer items with clear completion criteria.
