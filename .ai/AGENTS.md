# AGENTS.md

Shared, project-agnostic instructions for coding agents. This file is consumed as a **git submodule
across multiple repositories** — keep it portable. Anything specific to one project (stack, solution
names, framework conventions, and the project's **memory identity**) belongs in that repo's own root
`CLAUDE.md` / agent file, which includes this one and takes precedence for that repo.

## Mandatory startup workflow

Run this before any response in a new session, regardless of the task:

1. Read this file fully, then the repo's root `CLAUDE.md` (project-specific rules + the project's
   **memory identity**).
2. Resolve the **memory identity** for this project (see *Vector Memory › Memory identity*).
3. **Recall before acting.** From the user's first prompt:
   - `search_memory` — one broad query, scoped to the project entity — to surface prior decisions,
     gotchas, and context. Add one or two focused queries (by service, issue key, or primary topic)
     when useful.
   - `recall_about_user` — load durable facts about the user (preferences, environment, working
     style). This profile follows the user across every project.
   - When the prompt describes a self-contained, transferable task (a tool, procedure, or migration
     that isn't specific to this codebase), also `search_memory(entityType: knowledgebase,
     entity: shared, …)` for an existing playbook before working it out from scratch.
   - If a `memory-bank/` directory exists at the repo root or under `.ai/`, read it too.

Treat recalled memory as contextual leads to validate, not canonical truth. Runtime- or
behavior-critical details must still be verified in code, configuration, or runtime before acting.
If memory tooling is unavailable, continue from code/config/docs — startup is not blocked.

## Source-of-truth and precedence

- This file defines how agents work; the repo's root `CLAUDE.md` adds project-specific rules and
  overrides this file where they conflict.
- Code, configuration, and runtime behavior are the primary truth for system behavior.
- Documentation (Confluence, local docs) is secondary context for intent and conventions.
- Vector memory is a secondary semantic-recall layer for evolving context and cross-session
  continuity. If it conflicts with code/config/runtime, prefer the verified sources and treat the
  recalled context as stale until revalidated.

## Guardrails (portable)

- Respect `.editorconfig`.
- Verify runtime-critical behavior in code before changing it.
- Prefer small, reviewable changes; reuse existing patterns and abstractions before adding new ones.
- Preserve established folder and project-family conventions unless there is a strong reason to change them.
- Git writes only when the user explicitly asks.
- Never push to any branch named `main` or `master` (including namespaced `*/main`, `*/master`).
- Never delete any branch, even if asked.
- Before merges or other writes affecting `main`, `master`, or `dev`, confirm source and target with the user.
- Only connect to external systems on the local development machine unless the user permits otherwise.
- Ask permission before data-modifying operations on external systems.

Project-specific guardrails (frameworks, ORMs, generated files, logging conventions) live in the
repo's root `CLAUDE.md`.

## Vector Memory (Qdrant / MCP)

The preferred store for evolving implementation context, rationale, and cross-session continuity. It
complements code- and doc-first understanding; it does not replace runtime verification.

### Memory identity (entityType + entity)

Every project maps to one stable entity. Resolve it the **same way every session** so a project's
memory never fragments across near-duplicate entities:

1. If the repo's root `CLAUDE.md` declares a memory identity (`entityType` + `entity`), use it verbatim.
2. Otherwise derive it: `entityType = project`, and `entity =` the repository/solution name slugged
   to lowercase `[a-z0-9-]` (e.g. `Inferlytics` → `inferlytics`).
3. Before writing a new entity, call `list_entities` and reuse the **exact** name of any existing
   matching entity instead of creating a variant.

There is no connection-level default in the shared setup, so **every** generic memory call
(`save_memory`, `search_memory`, `list_memories`, `get_memory`, `update_memory`, `delete_memory`)
MUST pass `entityType` and `entity` explicitly. (A project may set `X-Inferlytics-Entity-Type` /
`X-Inferlytics-Entity` headers in its own `.mcp.json` to make this automatic; passing the args is
always safe and overrides the header.)

Use `entityType: project` for software/app/planning work; `entityType: person` only for **other**
people (colleagues, family, friends). Facts about the user themselves use the about-the-user tools
(below) — never a `person` entity. Reusable task know-how that isn't tied to any one project or
person — a procedure you could repeat elsewhere — goes to the cross-cutting knowledge base
(`entityType: knowledgebase`, `entity: shared`; see *The cross-cutting knowledge base* below).

### Tools

| Tool | Purpose |
|------|---------|
| `save_memory` / `search_memory` | Save / semantically recall a project (or person) memory |
| `list_memories` / `get_memory` | Browse newest-first / fetch one by id |
| `update_memory` / `delete_memory` | Correct-or-enrich / remove a memory |
| `list_entities` / `create_entity` / `delete_entity` | List entities (reuse exact names) / pre-create / drop an entity |
| `remember_about_user` / `recall_about_user` | Save / recall facts about the user themselves — their own profile, shared across all of the user's projects and clients |
| `list_about_user` / `update_about_user` / `forget_about_user` | Browse / edit / delete facts in the user's profile |

`search_memory` with no `entity` fans out across the owner's entities; pass `entity` to target one.

### The about-the-user profile

- Save durable facts about the user — preferences, role, environment, working style, standing
  instructions, corrections — with `remember_about_user`; recall with `recall_about_user`.
- This profile is keyed to the user, not the project, so what's learned in one repo applies in all
  of them. Recall it at session start (step 3 above).
- Reserve `person` entities for OTHER people. If you accumulate substantial information about a
  specific colleague/friend, move it out of the user profile into that person's own `person` entity.
- Examples: *"prefers tabs over spaces"*, *"always run the build before claiming done"* →
  `remember_about_user`. *"Colleague Alice owns the billing service"* →
  `save_memory(entityType=person, entity=alice)`.

### The cross-cutting knowledge base

- A per-user store for **how a self-contained task was done** — reusable procedures, tooling recipes,
  and gotchas that aren't tied to any one codebase or person (e.g. migrating an SVN repo to Git,
  bootstrapping a tool, a non-obvious environment fix). Like the about-the-user profile it is keyed
  to the user, so a playbook saved while working in one repo is recallable from all of them.
- Identity: `entityType: knowledgebase`, `entity: shared` — one searchable bucket; partition by
  `topic:` tags, not by separate entities. Pass both args explicitly like any other memory call.
- What goes where (a real procedure often splits across all three):
  - *How* a transferable task is performed → `knowledgebase`.
  - Work specific to one codebase (decisions, fixes, architecture) → that `project` entity.
  - The user's own durable preferences about such tasks → about-the-user profile.
  - Worked example — the SVN→Git migration split into: the migration recipe (git-svn under WSL, the
    auth workaround, the fetch resume loop, the author-map scheme) → `knowledgebase`; the standing
    preferences (keep git-svn-id trailers, all binaries to LFS, WSL is fine) → about-the-user;
    anything repo-specific → that project entity.
- Recall it when a task looks like it has prior art (startup step 3); save to it when you finish a
  task whose method you'd want to reuse next time (save policy below).

### Save policy

Save when:
- significant implementation work was completed,
- an important explanation of why something works or changed was discovered,
- a validated investigation produced reusable cross-session context,
- useful ticket/PR/doc context was distilled into a concise reusable summary,
- the user revealed a durable preference or standing instruction (→ about-the-user profile),
- a self-contained task was completed whose method is reusable beyond this project (→ the
  cross-cutting `knowledgebase`).

Do not save: hypotheses, dead ends, raw logs, TODO/plan lists, unresolved placeholders, secrets or
credentials, or large copies of code/docs/transcripts. Verify a finding from code/config/trusted
docs first, then save only a concise retrieval summary.

### Tagging

- Include a `kind:` tag on every memory — one of `kind:implementation-summary`, `kind:investigation`,
  `kind:rationale`, `kind:integration-note`.
- The entity is already stored structurally; add an `entity:<entity>` tag too only when you rely on
  tag-filtered cross-entity search.
- Optional when they aid retrieval: `issue:<key>`, `service:<name>`, `topic:<name>` (primary topics
  only — don't tag every incidental one). Mention the services/issues/topics that mattered in the
  content itself.

### Content shape

- One memory = one completed task, one validated investigation, or one narrow reusable finding.
- Concise and entity-focused, ~80–250 words; split anything past ~500–700 words into a summary plus
  focused detail memories. Summarize discussions — never store raw transcripts.
- Prefer this structure (use fitting field names for `person` / about-the-user memories):

```text
Title: <short entity-focused title>

Why it mattered:
- <why this work or finding mattered>

What was found or changed:
- <main verified outcome 1>
- <main verified outcome 2>

Main context:
- services: <names or none>
- issue: <key or none>
- topics: <primary topics or none>

Source refs:
- <file paths, PRs, tickets, docs>

Verification status:
- <code-verified | config-verified | doc-supported | mixed>
```

### Update and delete hygiene

- Prefer concise entity-focused memories over chronological narratives.
- `update_memory` to correct or enrich rather than create contradictory duplicates; `delete_memory`
  for invalid, duplicate, obsolete, or sensitive entries.
- When better verification changes your understanding, update or delete the affected entry.

## Documentation hygiene

- Keep this file portable and focused on agent workflow + durable cross-project rules.
- Prefer code/config verification over broad in-repo prose summaries.
- Use vector memory for evolving context, but never let it replace code-first understanding; prefer
  rediscovering durable truth from code/config/trusted docs over relying on stale recalled context.
