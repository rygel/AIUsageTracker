# Contributing to AI Usage Tracker

Thank you for your interest in contributing! This document outlines our development workflow and release process.

## Branch Strategy

We use a **Git Flow** style branching strategy with two main branches:

- **`main`** - Production-ready stable releases
- **`develop`** - Integration branch for beta releases

### Workflow

```
feature branches → develop → main (stable releases)
                      ↓
                   beta releases (tagged from develop)
```

### Branch Protection

- **main**: Requires pull request reviews, all CI checks must pass
- **develop**: Allows direct pushes for rapid iteration

## Development Workflow

### 1. Feature Development

```bash
# Create a feature branch from develop
git checkout develop
git pull origin develop
git checkout -b feature/my-new-feature

# Make your changes
git add .
git commit -m "feat: add new feature"

# Push and create PR to develop
git push origin feature/my-new-feature
# Create PR via GitHub UI targeting develop branch
```

### 2. Beta Releases

Beta releases are created from the `develop` branch for testing new features:

```bash
# Use GitHub Actions workflow
# Go to Actions → Create Release → Run workflow
# Select channel: beta
# Enter version: e.g., 2.3.0-beta.1
```

Beta releases will:
- Be tagged from `develop` branch
- Trigger builds for all platforms
- Create a GitHub pre-release
- Be available to users who opt into the Beta channel

### 3. Stable Releases

Stable releases are created from the `main` branch:

```bash
# Merge develop into main when ready for release
git checkout main
git pull origin main
git merge develop --no-ff
git push origin main

# Use GitHub Actions workflow
# Go to Actions → Create Release → Run workflow
# Select channel: stable
# Enter version: e.g., 2.3.0
```

## Release Channels

We support two release channels:

| Channel | Description | Branch | Version Format |
|---------|-------------|--------|----------------|
| **Stable** | Production-ready, well-tested releases | main | `2.2.25`, `2.3.0` |
| **Beta** | New features, may have minor bugs | develop | `2.3.0-beta.1` |

### When to Use Each Channel

- **Stable**: Use for production environments and general users
- **Beta**: Use for early access to features, willing to report issues

## Version Numbering

We follow [Semantic Versioning](https://semver.org/):

- **MAJOR** version - Incompatible API changes
- **MINOR** version - New functionality (backward compatible)
- **PATCH** version - Bug fixes (backward compatible)

### Prerelease Tags

- `-beta.x` - Feature complete, testing phase

## Commit Messages

We follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

### Types

- `feat` - New feature
- `fix` - Bug fix
- `docs` - Documentation only
- `style` - Code style (formatting, no logic change)
- `refactor` - Code refactoring
- `perf` - Performance improvements
- `test` - Adding or updating tests
- `chore` - Build process, dependencies, etc.

### Examples

```bash
feat(web): add Reliability page with sidebar link
fix(monitor): handle API timeout gracefully
docs: update release channel documentation
chore(release): bump version to 2.2.25
```

## Pull Request Guidelines

1. **Create PRs against `develop`** (not `main`)
2. Ensure CI checks pass
3. Update documentation if needed
4. Link related issues
5. Request review from maintainers

## Testing

Before submitting a PR:

1. Run the test suite: `dotnet test`
2. Test your changes locally
3. If adding features, add tests
4. Verify no regressions

## Questions?

- Open an issue for bugs or feature requests
- Join our [Discord](https://discord.gg/AZtNQtWuJA) for discussions
- Check existing issues/PRs before creating new ones

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
