# Skill Pack Review Prompt

Please review this MonitorBaseClaude mini-skill pack as agent-facing workflow guidance.

Context:

- MonitorBaseClaude has two MCP surfaces:
  - Roslyn Tooling: semantic C# discovery, references, callers, diagnostics, impact.
  - System Monitor: watched-source staging, overlay validation, WinMerge review, hash-gated decisions.
- The agent should prefer Roslyn Tooling over grep/text search for C# symbol discovery.
- The agent must not directly edit watched source.
- Coupled multi-file C# edits should be staged into one monitor session so overlay compilation sees the whole proposed change before serial WinMerge review.
- The agent should not review the first file until all coupled staged candidates are present in the session.
- Review gates are real workflow stops: overlay validation cancel, Host unavailable, missing files, dirty-unexpected, or review-chain-blocked should stop later diffs.

Please assess:

1. Are the mini-skills small enough for practical agent loading?
2. Does `MonitorBaseClaudeSkillPack.md` give a good trimmed master model?
3. Does `SkillRouter.md` route to the right cards without encouraging the agent to load everything?
4. Are the session-overlay/review-queue rules clear enough to force the agent to stage all coupled files before review starts?
5. Are any rules ambiguous, too wordy, or likely to cause over-cautious behavior?
6. What should be promoted into MCP tool descriptions versus left as skill guidance?
7. What single missing skill card would be highest value next?

Return:

- Top 5 risks
- Suggested wording fixes
- Any redundant cards or merge candidates
- A revised compact master prompt if you think mine can be shorter
