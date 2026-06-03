Audit the latest changes in this branch for bugs and compliance with the project's conventions defined in CLAUDE.md.

## Steps

1. **Identify changed files**: Run `git diff main --name-only` to find all files modified on this branch.

2. **Read each changed file** and review it for:

### Bug check
- Logic errors, off-by-one mistakes, incorrect conditions
- Unhandled edge cases (null, undefined, empty inputs)
- Race conditions or state management issues
- Functions that could throw but aren't wrapped in try/catch where needed
- Incorrect type narrowing or unsafe casts

### Code style (from CLAUDE.md)
- Check all conventions defined in CLAUDE.md and flag any violations
- Pay special attention to naming, typing, error handling, and import patterns

### Documentation check
- For each changed source file, check if any documentation in `docs/` describes behaviour that the change modified
- If a doc needs updating, report it as a finding with severity "Doc drift"

3. **Report findings** in this format:

For each issue found:
- **File**: path and line number
- **Severity**: Bug / Convention violation / Nitpick
- **Issue**: What's wrong
- **Fix**: How to fix it

If no issues are found, state that the changes are clean.

4. **Note pre-existing issues** separately — flag them but don't fix them unless they interact with the new changes.
