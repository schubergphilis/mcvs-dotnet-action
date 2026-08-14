# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

**mcvs-dotnet-action** is a GitHub composite action and reusable Taskfile that standardizes quality checks for .NET projects. It provides:

- A GitHub Action ([action.yml](action.yml)) for CI/CD pipelines
- A remote Taskfile ([build/task.yml](build/task.yml)) for local development and CI automation

The action orchestrates: .NET SDK installation (from global.json or the TargetFramework), lock file validation, security scanning (dotnet list package --vulnerable, osv-scanner, Grype), linting (dotnet format whitespace and style), static analysis (Roslyn analysers), unit/integration/component/e2e tests (with Testcontainers), code coverage enforcement, mutation testing (Stryker.NET), and optional single file binary releases.

It is the .NET counterpart of [mcvs-golang-action](https://github.com/schubergphilis/mcvs-golang-action) and [mcvs-php-action](https://github.com/schubergphilis/mcvs-php-action) and deliberately mirrors their layout, naming and workflows.

## Architecture

### Key Components

1. **action.yml** - Composite GitHub Action definition

   - Defines all inputs (testing-type, dotnet-version-file, release configs, etc.)
   - Derives the .NET SDK version and installs it via `actions/setup-dotnet`
   - Installs the Task runner through the taskfile.dev install script, since there is no `go install` in a .NET toolchain
   - Conditionally executes different testing/security/build workflows based on `testing-type` input
   - Supports these testing types: `component`, `coverage`, `e2e`, `integration`, `lint`, `mutation`, `nuget-validate`, `security-grype`, `security-nuget-packages`, `static-analysis`, `unit`

2. **build/task.yml** - Reusable Taskfile

   - Contains all task definitions that the action executes
   - Designed to be included remotely by other projects: `{{.REMOTE_URL}}/{{.REMOTE_URL_REPO}}/{{.REMOTE_URL_REF}}/build/task.yml`
   - Defines versions for the tools that are not part of the .NET SDK (reportgenerator, stryker, osv-scanner, yq)
   - Provides both CI-specific tasks (`test-cicd`, `coverage`) and development tasks (`test`, `lint`, `format`)

3. **scripts/package-version-updater.sh** - Automation
   - Periodically updates pinned tool versions in build/task.yml
   - Opens PRs with version updates using gh CLI
   - Resolves versions from GitHub releases, except for the .NET tools, which are published on nuget.org and are therefore resolved from the nuget.org registry

### SDK Version Resolution

The action does not install a version itself but hands the work to `actions/setup-dotnet`:

- The `dotnet-version` input takes precedence over everything.
- A `global.json` (the `dotnet-version-file` input, default `global.json`) is handed over as the `global-json-file` input of setup-dotnet, so that its `rollForward` and `allowPrerelease` keys are honoured. Its `sdk.version` key is verified to exist first.
- When there is no `global.json`, the highest `TargetFramework(s)` of the project files is used, e.g. `net9.0` becomes `9.0.x`. This fallback matters, as the majority of the .NET projects has no `global.json`, unlike PHP projects, which always have a `composer.json`.

### Tool Installation Strategy

Most tooling is part of the .NET SDK itself (`dotnet format`, `dotnet test`, `dotnet publish`, `dotnet list package`, the Roslyn analysers) and is therefore neither pinned nor installed. The remaining tools are installed into `${MCVS_DOTNET_ACTION_BIN:-$HOME/.mcvs-dotnet-action/bin}`:

- **ReportGenerator** and **Stryker.NET** by the internal `dotnet-tool-install` task, which is the counterpart of the `download-phar` task of the PHP action. `dotnet tool update --tool-path` is used, as it both installs and changes a version, without touching the global .NET tool installation.
- **osv-scanner** and **yq** as static binaries, exactly as in the PHP and Golang action.

**coverlet** and **Testcontainers** are deliberately *not* installed: both are NuGet packages that the test projects reference themselves, so that the coverage is collected, and the containers are created, by the version that the project expects. The `XPlat Code Coverage` data collector that the `coverage` task uses comes from the `coverlet.collector` package.

### Testing Architecture

Go build tags and PHPUnit testsuites have no .NET equivalent, so the testing type is mapped onto a test category that `dotnet test --filter` selects on:

- **`unit`** - every test *without* one of the categories below
- **`integration`** - `Category=Integration`
- **`component`** - `Category=Component`
- **`e2e`** - `Category=E2E`

Every filter is a variable (`TEST_FILTER_UNIT`, `TEST_FILTER_INTEGRATION`, ...), so a project that annotates with `TestCategory` (NUnit and MSTest) or that prefers a project name convention can override them.

The `TEST_FILTER` variable is combined with the filter of the testing type, e.g. `(Category=Integration)&(FullyQualifiedName~Slow)`. CI runs add `--logger "console;verbosity=detailed"`, which prints every test that has been run for the testing type at hand.

Timeouts are enforced by wrapping the `dotnet test` invocation in `timeout` (or `gtimeout` on macOS), as in the PHP action.

### Coverage

`dotnet test --collect:"XPlat Code Coverage"` writes one cobertura report per test project. ReportGenerator merges them into `build/coverage/Cobertura.xml`, of which the `line-rate` attribute of the root element is the actual coverage. As in the Golang and PHP action, `CODE_COVERAGE_STRICT` (default `true`) also fails the build when the actual coverage *exceeds* the expected coverage, forcing the threshold to be raised.

### Mutation Testing

The `mutation` testing type runs Stryker.NET. The `mutation-score-expected` input is passed as the break, low *and* high threshold, as Stryker requires `break <= low <= high` and rejects a break threshold that exceeds the low one, which is 60 by default. Deriving all three from one input keeps the expected score in a single place.

A `stryker-config.json` is optional: the task only passes `--config-file` when the file exists. The `--solution` option is only passed when a solution has been found, since Stryker is otherwise run from the test project directory.

### Testcontainers

The integration, component and end-to-end tests create their dependencies with Testcontainers, which the test projects reference themselves. The action contributes two things:

- The internal `docker-check` task, which fails with an actionable message when the container runtime is not reachable, as the failure of Testcontainers itself is hard to interpret. It runs before every container based testing type, both locally and in CI.
- The `check-docker-networks` task, ported from the PHP and Golang action, which reports the networks that have been left behind by a container that a test did not dispose. It only wraps the local `test-integration` and `test-component` tasks.

`TESTCONTAINERS_RYUK_DISABLED` is set as an environment variable of the `test` task rather than as a command line option, as Testcontainers reads its configuration from the environment.

### MSBuild

The `DOTNET_BUILD_TOOL` variable (the `build-tool` input) selects between the `dotnet` CLI, the default that covers every SDK style project, and `msbuild`, which is needed for the projects that the SDK cannot drive, such as the .NET Framework ones. Only the `build` and `restore` tasks branch on it; every other task uses the `dotnet` CLI, as `dotnet test`, `dotnet format` and `dotnet publish` drive MSBuild themselves.

The locked restore mode is expressed as `--locked-mode` for the CLI and as `-p:RestoreLockedMode=true` for MSBuild. The `MSBUILD_ARGS` variable (the `msbuild-args` input) is appended to every build, restore, test and publish command, irrespective of the build tool, as both accept `-p:` properties.

`msbuild-install: yes` runs `microsoft/setup-msbuild`, which requires a Windows runner.

### Security Scanning Flow

1. **dotnet list package --vulnerable** - the GitHub Advisory Database, as mirrored by nuget.org

   - Run with `--include-transitive` and `--format json`, so that the number of findings can be counted with jq
   - Requires a restore, unlike `composer audit`, and therefore depends on the `restore` task

2. **osv-scanner** - The OSV database, run against every `packages.lock.json`

   - Configured via `osv-scanner.toml` (see [osv-scanner.toml.example](osv-scanner.toml.example))
   - Allows temporary ignores (max 1 month) via `IgnoredVulns` with expiration dates
   - See [docs/osv-scanner.md](docs/osv-scanner.md) for detailed usage

3. **Grype** - Optional additional vulnerability scanning via Anchore

   - Triggered when `testing-type: security-grype`
   - Severity cutoff: HIGH or above

Both `osv-scanner` and `nuget-validate` fail when no `packages.lock.json` has been found, as scanning constraints instead of resolved versions gives a false sense of security. Enable `RestorePackagesWithLockFile` and commit the lock files.

## Common Development Commands

### Using the Remote Taskfile (in consuming projects)

Set up `Taskfile.yml` in your project:

```yaml
version: 3
vars:
  REMOTE_URL: https://raw.githubusercontent.com
  REMOTE_URL_REF: v1.0.0 # Use latest stable version
  REMOTE_URL_REPO: schubergphilis/mcvs-dotnet-action
includes:
  remote: >-
    {{.REMOTE_URL}}/{{.REMOTE_URL_REPO}}/{{.REMOTE_URL_REF}}/build/task.yml
```

Then run tasks with:

```bash
# Required: enable experimental remote taskfiles support
export TASK_X_REMOTE_TASKFILES=1

# Restore the nuget dependencies
task remote:restore --yes

# Run unit tests
task remote:test --yes

# Run integration tests
task remote:test-integration --yes

# Run component tests
task remote:test-component --yes

# Run linting
task remote:lint --yes

# Run static analysis
task remote:static-analysis --yes

# Run code coverage
task remote:coverage --yes

# Run security scanning
task remote:nuget-audit --yes
task remote:osv-scanner --yes

# Automatically fix linting issues
task remote:fix-linting-issues --yes

# List all available tasks
task --list-all
```

### Fixing Linting Issues

The `fix-linting-issues` task automatically fixes common linting problems:

```bash
task remote:fix-linting-issues --yes
```

It runs `dotnet format`, which applies the whitespace, code style and analyser fixes that can be resolved automatically, based on the rules and severities of the `.editorconfig`.

After running, review the changes as some linting issues may still require manual intervention.

### Testing in This Repository

This repository uses itself for CI: [.github/workflows/dotnet.yml](.github/workflows/dotnet.yml) runs *every* testing type against the [example](example) solution through `uses: ./`.

The example is a small library plus its test project and exists purely to give the action something to lint, analyse, test, cover and mutate:

- `example/src/Mcvs.Example/SemanticVersion.cs` - parses and orders the version tags of this action
- `example/tests/Mcvs.Example.Tests/SemanticVersionTests.cs` - the unit tests, i.e. the tests without a category
- `example/tests/Mcvs.Example.Tests/SemanticVersionContainerTests.cs` - the `Integration`, `Component` and `E2E` categories, where the integration test starts an actual container with Testcontainers

The thresholds in the workflow are pinned to what the example produces: `code-coverage-expected: "100.0"`, which `CODE_COVERAGE_STRICT` enforces exactly, and `mutation-score-expected: "90"`, which is deliberately below the observed 96.15%, as a Stryker upgrade may introduce mutators and the mutation score has no strict upper bound.

The example also demonstrates the intended remediation of a vulnerable transitive dependency: `Testcontainers` depends on a version of `SSH.NET` that carries GHSA-q939-rpr3-3284, so the test project pins the patched version directly. Removing that `PackageReference` makes the `security-nuget-packages` testing type fail, which is a quick way to verify that the scanners work.

### Overriding Variables

Override Taskfile variables when including remotely:

```yaml
includes:
  remote:
    taskfile: >-
      {{.REMOTE_URL}}/{{.REMOTE_URL_REPO}}/{{.REMOTE_URL_REF}}/build/task.yml
    vars:
      CODE_COVERAGE_STRICT: "false" # Disable strict coverage enforcement
      DOTNET_SOLUTION: "src/Some.sln" # Run against another solution
```

Available override variables (see the `vars` section of [build/task.yml](build/task.yml)):

- `CODE_COVERAGE_STRICT` - Enforce minimum coverage (default: "true")
- `DOTNET_BUILD_CONFIGURATION` - The configuration that is built and tested (default: "Release")
- `DOTNET_SOLUTION` - The solution or project the tasks run against (default: the solution that has been found)
- `DOTNET_BUILD_TOOL` - The tool that builds and restores: dotnet or msbuild (default: "dotnet")
- `DOTNET_VERBOSITY` - The verbosity of the dotnet commands (default: "minimal")
- `MSBUILD_ARGS` - Arguments for every build, restore, test and publish command (default: "")
- `MUTATION_TIMEOUT` - The duration before the mutation testing times out (default: "20m")
- `STRYKER_CONFIG_PATH` - The path to the optional Stryker.NET configuration (default: "stryker-config.json")
- `TESTCONTAINERS_RYUK_DISABLED` - Whether the Testcontainers resource reaper is disabled (default: "false")
- `RELEASE_SELF_CONTAINED` - Whether the binary contains the runtime (default: "true")
- `TEST_FILTER_COMPONENT` / `TEST_FILTER_E2E` / `TEST_FILTER_INTEGRATION` / `TEST_FILTER_UNIT` - The filter of each testing type

## Using the GitHub Action

### Basic Usage

```yaml
name: dotnet
on: pull_request
permissions:
  contents: read
  packages: read
jobs:
  mcvs-dotnet-action:
    strategy:
      matrix:
        args:
          - testing-type: "unit"
          - testing-type: "lint"
          - testing-type: "static-analysis"
          - testing-type: "coverage"
          - testing-type: "security-nuget-packages"
    runs-on: ubuntu-24.04
    env:
      TASK_X_REMOTE_TASKFILES: 1
    steps:
      - uses: actions/checkout@v4.2.2
      - uses: schubergphilis/mcvs-dotnet-action@v1 # Use @v1 for latest v1.x.x
        with:
          testing-type: ${{ matrix.args.testing-type }}
          token: ${{ secrets.GITHUB_TOKEN }}
```

### Advanced Usage with Releases

For publishing a binary on tagged releases:

```yaml
- uses: schubergphilis/mcvs-dotnet-action@v1
  with:
    release-application-name: "my-app"
    release-dir: "src/MyApp"
    release-runtime: "linux-x64"
    release-type: "binary"
    token: ${{ secrets.GITHUB_TOKEN }}
```

The `release-application-name` is passed as the `AssemblyName`, so that the location of the published binary is deterministic.

### Key Action Inputs

- **testing-type** - Main selector: `unit`, `integration`, `component`, `e2e`, `coverage`, `lint`, `mutation`, `static-analysis`, `nuget-validate`, `security-nuget-packages`, `security-grype`
- **test-filter** - An additional `dotnet test --filter` expression to narrow a run down (e.g., "FullyQualifiedName~Slow")
- **code-coverage-expected** - Minimum coverage percentage (default: 80)
- **dotnet-version** / **dotnet-version-file** - The SDK version, or the global.json it is derived from
- **mutation-score-expected** - Minimum mutation score (default: 60)
- **build-tool** / **msbuild-args** / **msbuild-install** - The MSBuild support, see the MSBuild paragraph
- **release-runtime** - The runtime identifier (RID) a release is published for (default: linux-x64)
- **test-timeout** / **code-coverage-timeout** - Timeouts for test execution (e.g., "10m")

## Important Implementation Details

### NuGet Verification

- The `restore` task adds `--locked-mode` automatically when a `packages.lock.json` is present and points at `dotnet restore --force-evaluate` when a discrepancy is detected
- The `nuget-validate` testing type additionally fails when no lock file exists at all
- Private NuGet packages: use `github-token-for-downloading-private-nuget-packages` to add the GitHub Packages registry of the organisation as a source

### Tool Versions

The versions of the tools that are not part of the .NET SDK are pinned in the `vars` section of [build/task.yml](build/task.yml):

- reportgenerator: 5.5.11
- stryker: 4.16.0
- osv-scanner: v2.5.0
- yq: v4.53.3
- Task runner: 3.52.0 (defined in action.yml)

The [package-version-updater workflow](.github/workflows/package-version-updater.yml) automatically opens PRs to update these versions weekly.

## Project-Specific Notes

### This Repository Structure

```
.
├── action.yml                 # Composite action definition
├── build/
│   └── task.yml               # Remote Taskfile with all tasks
├── scripts/
│   └── package-version-updater.sh  # Tool version updater
├── .github/workflows/
│   ├── dotnet.yml             # Self-testing workflow (uses this action)
│   └── package-version-updater.yml  # Weekly tool updates
├── example/                   # The solution that the action tests itself with
│   ├── Example.slnx
│   ├── src/Mcvs.Example/
│   └── tests/Mcvs.Example.Tests/
├── docs/
│   └── osv-scanner.md         # OSV scanner usage guide
├── .editorconfig              # Reference formatting and analyser configuration
├── Directory.Build.props      # Reference MSBuild properties, e.g. the lock file
├── osv-scanner.toml.example   # Example vulnerability ignore config
├── stryker-config.json.example # Example mutation testing config
└── global.json                # Defines the .NET SDK version for the action
```

### The Example Solution

Apart from [example](example), this repository contains no `.cs` code - it's purely tooling. The `global.json` exists to define the .NET SDK version for the action, and the root `.editorconfig` and `Directory.Build.props` are reference configurations that consuming projects can copy. Note that both also apply to the example, as MSBuild imports a `Directory.Build.props` from every parent directory, so the self testing workflow proves that the reference configurations are consistent with each other. Note that the two are coupled: the `.editorconfig` promotes IDE0005 to a warning, which Roslyn only reports when `GenerateDocumentationFile` is enabled, so the props file sets it and suppresses CS1591.

### Guardrail Workflows

Three workflows enforce the conventions of this repository and fail the build when they are broken:

- `action-sorted-inputs.yml` - the keys under `inputs` in action.yml must be sorted alphabetically
- `taskfile-sorted-units.yml` - the keys of every task in build/task.yml must be sorted alphabetically (`cmds`, `desc`, `internal`, `silent`, ...)
- `taskfile-without-empty-lines.yml` - build/task.yml may not contain empty lines outside of `- |` blocks

### Versioning

- Use `@v1` in workflows to automatically get latest v1.x.x updates
- Use specific tags (e.g., `@v1.0.0`) for pinned versions
- Breaking changes only occur on major version bumps (v1 → v2)
- Check the [releases page](https://github.com/schubergphilis/mcvs-dotnet-action/releases) for changelog
