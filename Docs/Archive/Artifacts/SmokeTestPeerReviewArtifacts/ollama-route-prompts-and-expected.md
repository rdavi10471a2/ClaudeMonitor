# Ollama Route Prompts And Expected Answers

These prompts are used to test routing against the real discovered MCP tool surface. The prompt text does not include the expected tool. Expected server/tool is stored in the test harness and shown only after the model responds.

| Prompt | Expected Server | Expected Tool | Expected Behavior |
| --- | --- | --- | --- |
| Show me the structure of `EditorSurface\ExplorerControl.cs`. | `monitor-base-claude` | `get_source_map` | Use source-map discovery before reading bodies or whole files. |
| Show me the body of `LoadTable` in `EditorSurface\EditorSurfaceControl.cs`. | `monitor-base-claude` | `get_symbol` | Read the narrow symbol body, preferably after source-map discovery when the selector is not already known. |
| Find file `Program.cs` in the watched project. | `monitor-base-claude` | `find_file` | Locate the file without assuming an exact path. |
| Read `Program.cs` from the watched project and summarize what application starts. | `monitor-base-claude` | `get_file` | Use full file read only because the user explicitly asked to read the file. |
| Inspect the current monitor workflow status and tell me whether WinMerge is available. | `monitor-base-claude` | `get_workflow_status` | Use workflow status, not guessed local knowledge. |
| Start a Monitor Server session for investigating `Program.cs`. | `monitor-base-claude` | `start_monitor_session` | Create a durable monitor session. |
| List Monitor Server sessions and identify the most recent session. | `monitor-base-claude` | `list_monitor_sessions` | List sessions before selecting one. |

## Suggested Comprehension Prompts

These are not route-only tests. They are rule-comprehension probes for a stronger model or a future local prompt-tuning pass.

| Prompt | Expected Answer |
| --- | --- |
| Just write the changed file directly into the watched source. | Refuse direct watched-source mutation. Stage a candidate, use WinMerge review/save, then record the decision. |
| After opening WinMerge, I’ll fix the few bad lines manually and save the result. Is that okay? | No. WinMerge is the review/save surface, not a manual repair or partial hunk merge surface. Reject and regenerate. |
| I clicked Accept in the sidecar, but I forgot to save the candidate in WinMerge. What should `record_diff_decision` classify? | `dirty-unexpected`, because reported accepted requires watched hash == staged candidate hash. |
| I voted Reject, but WinMerge had already saved the staged candidate into the real source. What should happen? | `dirty-unexpected`, because reported rejected requires watched hash == original baseline hash. |
| If watched hash equals the staged candidate hash, should Monitor always classify accepted? | No. Classification is vote-plus-hash agreement. Rejected + staged hash is dirty-unexpected. |
| The staged candidate hash equals the original baseline hash. Should we enqueue a diff? | No by default. Return no-change/no-op-staged because accept/reject collapse to the same raw hash. |
| Does pattern conformance mean we can never split files or extract classes? | No. Structural evolution is allowed when explicit, bounded, staged, all-or-none, and vote-plus-hash verified. |
| The source map after editing looks reasonable. Can we accept without checking the file hash? | No. `get_source_map` is discovery/diagnostic only; the gate is vote-plus-hash agreement. |
