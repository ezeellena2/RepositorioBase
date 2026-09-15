# CleanArchitecture

The project was generated using the [Clean.Architecture.Solution.Template](caRepositoryUrl) version caPackageVersion.

## Build

Run `dotnet build` to build the solution.

## Run

To run the application:

```bash
dotnet run --project .\src\AppHost
```

The Aspire dashboard will open automatically, showing the application URLs and logs.

## Code Styles & Formatting

The template includes [EditorConfig](https://editorconfig.org/) support to help maintain consistent coding styles for multiple developers working on the same project across various editors and IDEs. The **.editorconfig** file defines the coding styles applicable to this solution.

## Code Scaffolding

The template includes support to scaffold new commands and queries.

Start in the `.\src\Application\` folder.

If you encounter the error *"No templates or subcommands found matching: 'ca-usecase'."*, install the template and try again:

```bash
dotnet new install Clean.Architecture.Solution.Template::caPackageVersion
```

## Test

The solution contains unit, integration, and functional tests.

To run the tests:
```bash
dotnet test
```

## Repository knowledge model

- [Current specifications](openspec/specs/) define the behavioral baseline.
- [Identity and access](openspec/specs/identity-access/spec.md), [localization](openspec/specs/localization/spec.md),
  and [API offset pagination](openspec/specs/api-offset-pagination/spec.md) are canonical current specifications.
- [Architecture Decision Records](openspec/decisions/) capture durable cross-project decisions.
- `openspec/changes/<change>/` contains an active large-feature package; archived packages preserve immutable audit
  history after completion.
- `src/` is the implementation and `tests/` is its executable evidence. `docs/mockups/` is non-normative visual
  reference material.

Large features move through proposal, delta specs and design, tasks, apply, verify, and archive. Archiving merges
approved behavior into `openspec/specs/` and preserves the full change history. Retaining or promoting an accepted
cross-project ADR into `openspec/decisions/` is a repository-owned, manual SDD design/archive obligation enforced by
local instructions and tests today; Gentle AI and OpenSpec do not do it automatically. Do not create parallel feature
truth under `docs/features/` or implementation plans under `docs/superpowers/`.

## Help
To learn more about the template go to the [project website](caDocsUrl). Here you can find additional guidance, request new features, report a bug, and discuss the template with other users.
