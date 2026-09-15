# Clean Architecture Solution Template

[![Build](https://github.com/jasontaylordev/CleanArchitecture/actions/workflows/build.yml/badge.svg)](https://github.com/jasontaylordev/CleanArchitecture/actions/workflows/build.yml)
[![CodeQL](https://github.com/jasontaylordev/CleanArchitecture/actions/workflows/codeql.yml/badge.svg)](https://github.com/jasontaylordev/CleanArchitecture/actions/workflows/codeql.yml)
[![Nuget](https://img.shields.io/nuget/v/Clean.Architecture.Solution.Template?label=NuGet)](https://www.nuget.org/packages/Clean.Architecture.Solution.Template)
[![Nuget](https://img.shields.io/nuget/dt/Clean.Architecture.Solution.Template?label=Downloads)](https://www.nuget.org/packages/Clean.Architecture.Solution.Template)
![Twitter Follow](https://img.shields.io/twitter/follow/jasontaylordev?label=Follow&style=social)

The goal of this template is to provide a straightforward and efficient approach to enterprise application development, leveraging the power of Clean Architecture and ASP.NET Core. Using this template, you can effortlessly create a PostgreSQL-backed app with Angular, React, or Web API only, powered by ASP.NET Core and Aspire. Getting started is easy - simply install the **.NET template** (see below for full details).

For full documentation, visit **[cleanarchitecture.jasontaylor.dev](https://cleanarchitecture.jasontaylor.dev)**.

If you find this project useful, please give it a star. Thanks! ⭐

## Getting started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- [Node.js](https://nodejs.org/) (LTS) — only required if you plan to use the Angular or React frontend
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) or [Podman](https://podman.io/) (or any OCI-compliant container runtime) — required for the PostgreSQL development database.

### Install the template

```bash
dotnet new install Clean.Architecture.Solution.Template
```

### Create a new solution

Create a new PostgreSQL-backed solution using the template. Specify the client framework using `--client-framework` (`-cf`); `--database` (`-db`) accepts only `postgresql` and defaults to it:

```bash
dotnet new ca-sln --client-framework [angular|react|none] --database postgresql --output YourProjectName
```

| Option | Values | Default |
|---|---|---|
| `--client-framework` | `angular`, `react`, `none` | `react` |
| `--database` | `postgresql` | `postgresql` |

**Examples:**

🅰️ Angular SPA with ASP.NET Core Web API and PostgreSQL:
```bash
dotnet new ca-sln -cf angular -db postgresql -o YourProjectName
```

⚛️ React SPA with ASP.NET Core Web API and PostgreSQL:
```bash
dotnet new ca-sln -cf react -db postgresql -o YourProjectName
```

🔌 ASP.NET Core Web API only with PostgreSQL:
```bash
dotnet new ca-sln -cf none -db postgresql -o YourProjectName
```

> 💡 **Tip:** Run `dotnet new ca-sln --help` to see all available template options.

### Run the app

```bash
dotnet run --project .\src\AppHost
```

The Aspire dashboard will open automatically, showing the application URLs and logs.

To learn more, see the [Getting started](https://cleanarchitecture.jasontaylor.dev/docs/getting-started/) guide and [Architecture](https://cleanarchitecture.jasontaylor.dev/docs/architecture/) overview.

## Technologies

* [ASP.NET Core 10](https://docs.microsoft.com/en-us/aspnet/core/introduction-to-aspnet-core)
* [Aspire](https://aspire.dev)
* [Entity Framework Core 10](https://docs.microsoft.com/en-us/ef/core/)
* [Angular 21](https://angular.dev/) or [React 19](https://react.dev/)
* [MediatR](https://github.com/jbogard/MediatR)
* [FluentValidation](https://fluentvalidation.net/)
* [NUnit](https://nunit.org/), [Shouldly](https://docs.shouldly.org/), [Moq](https://github.com/devlooped/moq) & [Respawn](https://github.com/jbogard/Respawn)
* [Scalar](https://scalar.com/)

## Versions

The `main` branch is on **.NET 10.0**. Previous versions are available:

| Version | Branch |
|---|---|
| .NET 9.0 | [`net9.0`](https://github.com/jasontaylordev/CleanArchitecture/tree/net9.0) |
| .NET 8.0 | [`net8.0`](https://github.com/jasontaylordev/CleanArchitecture/tree/net8.0) |
| .NET 7.0 | [`net7.0`](https://github.com/jasontaylordev/CleanArchitecture/tree/net7.0) |
| .NET 6.0 | [`net6.0`](https://github.com/jasontaylordev/CleanArchitecture/tree/net6.0) |
| .NET 5.0 | [`net5.0`](https://github.com/jasontaylordev/CleanArchitecture/tree/net5.0) |
| .NET Core 3.1 | [`netcore3.1`](https://github.com/jasontaylordev/CleanArchitecture/tree/netcore3.1) |

## Repository knowledge model

- [Current specifications](openspec/specs/) define the behavioral baseline.
- [Identity and access](openspec/specs/identity-access/spec.md), [localization](openspec/specs/localization/spec.md),
  and [API offset pagination](openspec/specs/api-offset-pagination/spec.md) are canonical current specifications.
- [Architecture Decision Records](openspec/decisions/) capture durable cross-project decisions.
- `openspec/changes/<change>/` contains an active large-feature package; `openspec/changes/archive/` preserves
  immutable audit history after completion.
- `src/` is the implementation and `tests/` is its executable evidence. `docs/mockups/` is non-normative visual
  reference material.

A large feature moves through proposal, delta specs and design, tasks, apply, verify, and archive. Archiving merges
approved behavior into `openspec/specs/` and preserves the full change package under `openspec/changes/archive/`.
Retaining or promoting an accepted cross-project ADR into `openspec/decisions/` is a repository-owned, manual SDD
design/archive obligation enforced by local instructions and tests today; Gentle AI and OpenSpec do not do it
automatically. Do not create parallel feature truth under `docs/features/` or new implementation plans under
`docs/superpowers/`.

## Learn more

- 📖 [Clean Architecture Solution Template documentation](https://cleanarchitecture.jasontaylor.dev)

## Support

If you are having problems, please let me know by [raising a new issue](https://github.com/jasontaylordev/CleanArchitecture/issues/new/choose).

## License

This project is licensed under the [MIT License](LICENSE).
