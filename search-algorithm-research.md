# File Search Algorithm Research

Research into how popular file search tools handle fuzzy/flexible file matching, conducted for the Wade file finder rewrite.

## Key Finding: None Split Paths Into Segments

All three tools match against the **full path string** and use **subsequence matching** (characters in order, gaps allowed) as the default. Scoring bonuses at boundary positions (after `/`, `.`, `_`, camelCase transitions) naturally make segment-start matches rank higher — without explicit segmentation.

## How They Handle "pdf" Matching "report.pdf"

The query characters `p`, `d`, `f` are found in order within `report.pdf`. The `p` gets a **boundary bonus** because it follows `.` (a non-word character). This makes it rank well without any special substring or extension logic.

---

## fzf (junegunn)

### Core Algorithm

Two fuzzy matching algorithms, selectable via `--algo=v1` or `--algo=v2` (default):

**FuzzyMatchV1** — O(n) greedy:
1. Forward scan: walk input left-to-right, matching pattern characters in order
2. Backward scan: from end position, walk backward to find shortest matching substring
3. Score the found substring

Fast but not optimal — finds the first occurrence, not the highest-scoring one.

**FuzzyMatchV2** (default) — Modified Smith-Waterman, O(nm):
1. ASCII fast-path index scan to determine search bounds
2. Compute bonus values for each character position
3. Fill score matrix H[m][n] — every pattern character must match somewhere
4. Backtrace from maximum score position

Falls back to V1 if `N * M > slab capacity` or `M > 1000`.

### Scoring Constants

| Constant | Value | Purpose |
|---|---|---|
| `scoreMatch` | 16 | Base score per matching character |
| `scoreGapStart` | -3 | Penalty for starting a gap |
| `scoreGapExtension` | -1 | Penalty per additional gap character |
| `bonusBoundary` | 8 | Match at a word boundary |
| `bonusBoundaryWhite` | 10 | Match after whitespace |
| `bonusBoundaryDelimiter` | 9 | Match after delimiter (/, :, ;, etc.) |
| `bonusCamel123` | 7 | camelCase transition (lower->upper) or non-number->number |
| `bonusNonWord` | 8 | Match at non-word characters |
| `bonusConsecutive` | 4 | Minimum bonus for consecutive matches |
| `bonusFirstCharMultiplier` | 2 | First pattern character bonus doubled |

### Path Handling — Schemes, Not Segment Splitting

fzf does **not** split paths into segments. It has a **scheme** system:

- **"default"**: General-purpose scoring
- **"path"**: Path separator `/` gets highest delimiter bonus. Start of string treated as if preceded by `/`. Sort by `[score, pathname, length]`.
- **"history"**: All boundary bonuses equalized

The **"path" scheme** treats the start of the path as a delimiter boundary and has a `byPathname` tiebreaker that prefers matches closer to the filename (after last `/`).

### Multiple Matching Modes

| Type | Syntax | Behavior |
|---|---|---|
| Fuzzy | `sbtrkt` | Characters in order, gaps allowed |
| Exact substring | `'wild` | Contiguous substring |
| Exact boundary | `'wild'` | Substring at word boundaries |
| Prefix | `^music` | Match at start |
| Suffix | `.mp3$` | Match at end |
| Equal | `^music$` | Entire string must match |

All modes support negation with `!` prefix. Extended search (default): space = AND, `|` = OR.

---

## Television (tv) / Nucleo

### Core Algorithm

Television uses **nucleo**, the fuzzy matching library from the Helix editor team. Algorithm: **Smith-Waterman local alignment with affine gap penalties**.

### Scoring Constants

| Constant | Value | Purpose |
|---|---|---|
| `SCORE_MATCH` | 16 | Base score per matched character |
| `PENALTY_GAP_START` | 3 | Cost to open a gap |
| `PENALTY_GAP_EXTENSION` | 1 | Cost per additional gap character |
| `BONUS_BOUNDARY` | 8 | Word boundary match |
| `BONUS_CAMEL123` | 5 | camelCase or letter-to-number transitions |
| `BONUS_NON_WORD` | 8 | Non-word characters |
| `BONUS_CONSECUTIVE` | 4 | Minimum consecutive match bonus |
| `BONUS_FIRST_CHAR_MULTIPLIER` | 2 | First pattern character bonus doubled |
| `MAX_PREFIX_BONUS` | 8 | Max bonus for prefix position |

### Two-Matrix Approach (Key Differentiator from fzf)

Nucleo uses two scoring matrices:
- **M-matrix (match)**: Scores when current character is matched
- **P-matrix (gap/skip)**: Scores when characters are skipped

Produces more intuitive matches than fzf. Example: `foo` against `xf foo` yields `x__foo` (nucleo) vs `xf_oo` (fzf).

### Path Handling

**No segment splitting.** Full path fed as single string. Nucleo's `match_paths()` mode sets delimiter chars to `/` (or `/\\` on Windows) and gives delimiter-boundary bonuses, but TV uses default config with `prefer_prefix: true`.

### Matching Modes (Same Syntax as fzf)

| Mode | Syntax | Behavior |
|---|---|---|
| Fuzzy | `foo` | Characters with gaps |
| Substring | `'foo` | Contiguous substring |
| Prefix | `^foo` | Match at start |
| Postfix | `foo$` | Match at end |
| Exact | `^foo$` | Entire string |

Space-separated = AND. Negation with `!` prefix.

### Performance

~2x faster than fzf, ~6x faster than skim. Parallel matching via rayon threadpool. Lock-free item injection. ASCII fast path.

---

## VS Code Quick Open (Ctrl+P)

### Algorithm

Two-layer architecture:
1. **Low-level**: Character-by-character fuzzy scorer (`fuzzyScorer.ts`)
2. **High-level**: Item scorer that understands file path structure (label/filename vs description/directory)

### Scoring Bonuses (Quick Open Path)

| Bonus | Points | Condition |
|---|---|---|
| Base match | +1 | Any matching character |
| Consecutive | +6 (up to 3), +3 after | Consecutive character matches |
| Case match | +1 | Exact case match |
| Start of string | +8 | Match at position 0 |
| After path separator | +5 | Match after `/` or `\` |
| After other separator | +4 | Match after `_`, `-`, `.`, space, etc. |
| camelCase boundary | +2 | Uppercase starting new word |

### Filename Priority — The Key Design Decision

Three-tier scoring with bit-shifted thresholds:

```
PATH_IDENTITY_SCORE    = 1 << 18 = 262144   (exact full path match)
LABEL_PREFIX_SCORE     = 1 << 17 = 131072   (query is prefix of filename)
LABEL_SCORE_THRESHOLD  = 1 << 16 = 65536    (query matches within filename)
(no threshold)                               (matches across directory+filename)
```

Algorithm:
1. **Identity check**: exact full path match → highest score
2. **Label-first**: Score query against just the filename. If prefix → huge bonus. If matches → large bonus.
3. **Description+Label fallback**: Score against full `directory/filename` path → lowest tier.

When query contains a path separator (`/` or `\`), switches to scoring the full path.

### Substring vs Subsequence

- **Default**: Subsequence (fuzzy) — `hw` matches `HelloWorld`
- **Quoted query** (`"exact"`): Contiguous substring match
- **Space-separated**: Multiple words, AND semantics, each scored independently

---

## Comparison Table

| Aspect | fzf | nucleo (tv) | VS Code |
|--------|-----|-------------|---------|
| **Core algorithm** | Smith-Waterman (modified) | Smith-Waterman (2-matrix) | DP subsequence scorer |
| **Default mode** | Subsequence (fuzzy) | Subsequence (fuzzy) | Subsequence (fuzzy) |
| **Path splitting** | No — full string | No — full string | No — but scores filename separately |
| **Boundary bonuses** | After `/`, `.`, `_`, camelCase | After `/`, `.`, `_`, camelCase | After `/`, `.`, `_`, camelCase |
| **Gap penalties** | Affine (-3 start, -1 extend) | Affine (-3 start, -1 extend) | -5 start, -1 per skip |
| **Consecutive bonus** | +4 per consecutive char | +4 per consecutive char | +6 (up to 3), +3 after |
| **First char multiplier** | 2x bonus | 2x bonus | Prefix threshold boost |
| **Multi-mode syntax** | `'exact` `^prefix` `$suffix` `!negate` | Same syntax (via nucleo) | `"quoted"` for exact, space=AND |
| **Filename priority** | `--scheme=path` + `byPathname` tiebreaker | Implicit via boundary scoring | Explicit 2-tier: filename >> path |

---

## Recommendations for Wade

### What's Wrong with Current Approach

1. **Segment splitting kills substring matching.** "pdf" can never match "report.pdf" because no segment *starts with* "pdf" and edit distance is too high.
2. **Damerau-Levenshtein is the wrong tool.** DL measures edits to transform one string into another — designed for typo correction on similarly-sized strings, not for finding a short query inside a long path.
3. **Two-phase prefix+fuzzy is too rigid.** Real tools use a single scoring pass where prefix/boundary matches naturally score higher.

### Proven Approach (All Three Tools Agree)

1. **Subsequence match against the relative path** — query chars must appear in order, gaps allowed. No segment splitting.
2. **Score with boundary bonuses** — after path separator, `.`, `_`, `-`, camelCase transitions. Affine gap penalty (-3 start, -1 extend).
3. **Filename priority** (VS Code's best idea) — score query against just the filename; if it matches, add large bonus.
4. **Start simple** — greedy O(n) scorer covers 95% of use cases. Multi-mode syntax can come later.
