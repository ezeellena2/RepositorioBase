using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

/// <summary>
/// IA-REQ-012: every query for a tenant-scoped resource filters by the validated tenant as well as the resource
/// identifier.
/// <para>
/// The database already refuses a cross-tenant <em>association</em>: membership/role and invitation/role rows
/// carry `TenantId` in composite keys and foreign keys, so PostgreSQL rejects the combination (IA-REQ-034). What
/// no constraint can refuse is a <em>read</em> of a root row that forgot to name the tenant — it compiles, it
/// returns somebody else's row, and its own happy-path test passes because that test only ever has one tenant.
/// </para>
/// <para>
/// So this is a source test, not a reflection one: a LINQ predicate is not visible through reflection, and the
/// question being asked is whether the text of each query over a tenant-scoped set mentions the tenant at all.
/// It is deliberately coarse — it proves the tenant is named, never that it is the right one — which is why the
/// functional tests still own "another tenant's row reaches the caller as 404". Its value is the arrow of time:
/// the reads that legitimately cross tenants are enumerated below with their reason, and a new one cannot be
/// added without somebody writing that reason down.
/// </para>
/// </summary>
public sealed class TenantScopedQueryTests
{
    /// <summary>
    /// The LINQ and EF operators that begin a query. A set reference followed by one of these is being read; a
    /// set reference followed by `Add`, `Remove` or `Entry` is a write the change tracker resolves by key, and is
    /// none of this test's business.
    /// </summary>
    private static readonly string[] QueryOperators =
    [
        "Where", "AsNoTracking", "AsNoTrackingWithIdentityResolution", "AsQueryable", "AsSplitQuery", "Select",
        "SelectMany", "Include", "OrderBy", "OrderByDescending", "GroupBy", "Join", "Distinct", "TagWith",
        "IgnoreQueryFilters", "First", "FirstAsync", "FirstOrDefault", "FirstOrDefaultAsync", "Single",
        "SingleAsync", "SingleOrDefault", "SingleOrDefaultAsync", "Any", "AnyAsync", "All", "AllAsync", "Count",
        "CountAsync", "LongCount", "LongCountAsync", "ToList", "ToListAsync", "ToArray", "ToArrayAsync",
        "ToDictionary", "ToDictionaryAsync", "Sum", "SumAsync", "Min", "MinAsync", "Max", "MaxAsync",
        "ExecuteDelete", "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteUpdateAsync"
    ];

    private static readonly string[] ScannedProjects = ["Application", "Infrastructure", "Web"];

    /// <summary>
    /// The context declares the sets rather than reading them, so its own `Set&lt;Role&gt;()` bodies are not queries.
    /// </summary>
    private const string SetDeclarations = "src/Infrastructure/Data/ApplicationDbContext.cs";

    /// <summary>
    /// The reads that legitimately do not name a tenant, each with the reason it does not. Every entry is one of
    /// four kinds: a row addressed by a single-use token, which is the proof and the scope at once; a uniqueness
    /// question that is global by specification; an outbox delivery resolving a row the server itself enqueued;
    /// or the Platform slice, whose whole purpose is authority over other tenants (IA-REQ-043/044).
    /// <para>
    /// The count is pinned per file and set so that a second unfiltered query in an already-listed file is a
    /// failure rather than something the existing entry absorbs.
    /// </para>
    /// </summary>
    private static readonly CrossTenantRead[] DocumentedCrossTenantReads =
    [
        new("src/Application/IdentityAccess/Invitations/InvitationDelivery.cs", "Invitations", 1,
            "the token hash addresses the invitation; the caller has no session and no tenant yet."),
        new("src/Application/IdentityAccess/Organizations/ConfirmEmail/ConfirmEmailHandler.cs", "OrganizationProfiles", 1,
            "a normalized CUIT is exclusive across every tenant, so the question is global by IA-REQ-003."),
        new("src/Application/IdentityAccess/Organizations/ConfirmEmail/ConfirmEmailHandler.cs", "TenantMemberships", 1,
            "the envelope names the membership, and the next statement refuses it unless it belongs to the tenant the same envelope names."),
        new("src/Application/IdentityAccess/Organizations/RegisterOrganization/RegisterOrganizationHandler.cs", "OrganizationProfiles", 1,
            "the same global CUIT exclusivity, asked before any tenant exists."),
        new("src/Application/IdentityAccess/Platform/PlatformInvitationDelivery.cs", "PlatformAdminInvitations", 1,
            "the token hash addresses the invitation, before the identity it belongs to can hold a session."),
        new("src/Application/IdentityAccess/Platform/Administrators/PlatformAdministrators.cs", "PlatformAdminInvitations", 1,
            "Platform is the reserved singleton tenant; its standing offer is addressed by recipient."),
        new("src/Application/IdentityAccess/Platform/Bootstrap/RecoverPendingPlatformOwnerInvitationHandler.cs", "PlatformAdminInvitations", 2,
            "bootstrap recovery runs before the Platform owner, and possibly before any ApplicationUser, exists."),
        new("src/Application/IdentityAccess/Platform/Invitations/ConfirmPlatformInviteeHandler.cs", "PlatformAdminInvitations", 1,
            "confirmation is bound to the one-time token, which is what scopes it."),
        new("src/Infrastructure/Data/PermissionCatalogSynchronizer.cs", "RolePermissions", 1,
            "the catalogue is synchronized across every tenant at startup; there is no request and no active tenant."),
        new("src/Infrastructure/IdentityAccess/People/PersonalDocumentRegistry.cs", "PersonalTenantOwnerships", 1,
            "at most one Personal tenant per identity is a global uniqueness question, not a tenant-scoped read."),
        new("src/Infrastructure/Outbox/InvitationEmailDeliveryHandler.cs", "Invitations", 1,
            "outbox delivery resolves the row the server itself enqueued, by its primary key."),
        new("src/Infrastructure/Outbox/InvitedConfirmationDeliveryHandler.cs", "Invitations", 1,
            "outbox delivery resolves the row the server itself enqueued, by its primary key."),
        new("src/Infrastructure/Outbox/PlatformDeliveryHandlers.cs", "PlatformAdminInvitations", 3,
            "outbox delivery resolves the row the server itself enqueued, by its primary key."),
        new("src/Infrastructure/Outbox/SignInNoticeDeliveryHandler.cs", "Invitations", 1,
            "outbox delivery resolves the row the server itself enqueued, by its primary key."),
        new("src/Infrastructure/Platform/PlatformMembershipActivator.cs", "TenantRoles", 2,
            "activation decides who owns the singleton Platform tenant, which is the authority itself."),
        new("src/Infrastructure/Platform/PlatformMembershipActivator.cs", "MembershipRoles", 2,
            "activation decides who owns the singleton Platform tenant, which is the authority itself."),
        new("src/Infrastructure/Platform/PlatformOperationalProjectionReader.cs", "PlatformAdminInvitations", 1,
            "a read-only operational projection, allowlisted field by field by IA-REQ-044."),
        new("src/Infrastructure/Platform/PlatformOperationalProjectionReader.cs", "TenantRoles", 1,
            "a read-only operational projection, allowlisted field by field by IA-REQ-044."),
        new("src/Infrastructure/Platform/PlatformOperationalProjectionReader.cs", "MembershipRoles", 1,
            "a read-only operational projection, allowlisted field by field by IA-REQ-044."),
        new("src/Infrastructure/Platform/PlatformOperationalProjectionReader.cs", "TenantMemberships", 1,
            "a read-only operational projection, allowlisted field by field by IA-REQ-044.")
    ];

    /// <summary>
    /// What "tenant-scoped" means structurally: a set whose entity carries a non-nullable `TenantId`. Listing the
    /// names here is not a second source of truth — the scan uses the reflected set — but an aggregate that gains
    /// or loses a tenant column should say so in a diff rather than quietly change what this file guards.
    /// </summary>
    [Test]
    public void The_tenant_scoped_sets_are_the_ones_this_test_guards() =>
        TenantScopedSets().Keys.Order(StringComparer.Ordinal).ShouldBe([
            "InvitationRoles", "Invitations", "MembershipRoles", "OrganizationProfiles", "PersonalTenantOwnerships",
            "PlatformAdminInvitations", "RolePermissions", "TenantMemberships", "TenantRoles"
        ]);

    /// <summary>
    /// `AuditEvent.TenantId` is nullable because a platform-wide event belongs to no tenant, and `PersonProfile`
    /// names its tenant `PersonalTenantId` because it is keyed by the identity and readable only by its owner.
    /// Neither is scanned, and that is a decision rather than an oversight.
    /// </summary>
    [Test]
    public void A_set_whose_tenant_column_is_optional_is_not_a_tenant_scoped_set()
    {
        TenantScopedSets().Keys.ShouldNotContain("AuditEvents");
        TenantScopedSets().Keys.ShouldNotContain("PersonProfiles");
    }

    [Test]
    public void Every_query_over_a_tenant_scoped_set_names_the_validated_tenant()
    {
        var sites = QuerySites();

        // A regex that silently stopped matching would make this test pass by finding nothing at all. The floor is
        // well under the current count and only has to prove the scan still sees the query surface.
        sites.Count.ShouldBeGreaterThan(60, "the scan no longer recognizes the query surface it is meant to guard.");

        var unfiltered = sites.Where(site => !site.Statement.Contains("TenantId", StringComparison.Ordinal)).ToArray();
        var found = unfiltered.GroupBy(site => (site.File, site.Set))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var documented = DocumentedCrossTenantReads.ToDictionary(read => (read.File, read.Set));
        var problems = new List<string>();

        foreach (var (key, group) in found)
        {
            var lines = string.Join(", ", group.Select(site => $"line {site.Line}"));
            if (!documented.TryGetValue(key, out var read))
            {
                problems.Add(
                    $"{key.File} queries {key.Set} without naming the tenant at {lines}. Filter by the validated " +
                    "tenant (IA-REQ-012), or add a DocumentedCrossTenantReads entry saying why this read is global.");
                continue;
            }

            if (read.Count != group.Length)
            {
                problems.Add(
                    $"{key.File} has {group.Length} unfiltered {key.Set} queries (at {lines}) but {read.Count} " +
                    $"are documented: \"{read.Reason}\" A new one needs its own reason, not the existing one.");
            }
        }

        foreach (var read in DocumentedCrossTenantReads.Where(read => !found.ContainsKey((read.File, read.Set))))
        {
            problems.Add(
                $"{read.File} no longer has an unfiltered {read.Set} query, so the documented exception " +
                $"(\"{read.Reason}\") is stale and should be removed.");
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    private sealed record QuerySite(string File, string Set, int Line, string Statement);

    private sealed record CrossTenantRead(string File, string Set, int Count, string Reason);

    private static IReadOnlyDictionary<string, string> TenantScopedSets() =>
        typeof(ApplicationDbContext).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType.IsGenericType
                               && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(property => (Set: property.Name, Entity: property.PropertyType.GetGenericArguments()[0]))
            .Where(candidate => candidate.Entity.GetProperty("TenantId")?.PropertyType == typeof(TenantId))
            .ToDictionary(candidate => candidate.Set, candidate => candidate.Entity.Name, StringComparer.Ordinal);

    /// <summary>
    /// Every read of a tenant-scoped set, paired with the statement it belongs to. The statement is the unit
    /// because that is where the filter is written: `context.TenantRoles.AsNoTracking().Where(role => role.TenantId
    /// == tenantId)` is one expression however many lines it spans, and it ends at the first semicolon.
    /// </summary>
    private static List<QuerySite> QuerySites()
    {
        var operators = string.Join('|', QueryOperators);
        var patterns = TenantScopedSets().Select(set => (
                set.Key,
                Expression: new Regex(
                    // A set reached through an identifier — `context.TenantRoles`, `Context.TenantRoles`,
                    // `_context.TenantRoles` — and never through a namespace, so `namespace
                    // …IdentityAccess.Invitations` is not a query. The second alternative is LINQ query syntax,
                    // where `from role in context.TenantRoles` is followed by a newline instead of an operator.
                    $@"(?<![A-Za-z0-9_.])[A-Za-z_][A-Za-z0-9_]*\.{set.Key}\b(?:\s*\.\s*(?:{operators})\b|(?=\s*[,)\r\n]))"
                    // `Set<Role>()` reaches the same table without naming the property, and would otherwise be
                    // the one way around this test.
                    + $@"|(?<![A-Za-z0-9_.])Set\s*<\s*{set.Value}\s*>\s*\(\s*\)",
                    RegexOptions.Compiled)))
            .ToArray();

        var sites = new List<QuerySite>();
        foreach (var (path, relative) in SourceFiles())
        {
            var source = WithoutComments(File.ReadAllText(path));
            foreach (var (set, expression) in patterns)
            {
                foreach (Match match in expression.Matches(source))
                {
                    var end = source.IndexOf(';', match.Index);
                    var statement = source[match.Index..(end < 0 ? source.Length : end)];
                    var line = source.AsSpan(0, match.Index).Count('\n') + 1;
                    sites.Add(new QuerySite(relative, set, line, statement));
                }
            }
        }

        return sites;
    }

    private static IEnumerable<(string Path, string Relative)> SourceFiles()
    {
        var root = RepositoryRoot();
        foreach (var project in ScannedProjects)
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal)
                    || relative.Contains("/bin/", StringComparison.Ordinal)
                    || relative.Equals(SetDeclarations, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return (path, relative);
            }
        }
    }

    /// <summary>
    /// Comments are removed so that a `TenantId` named in prose cannot stand in for one named in a predicate, and
    /// string literals are copied through so that a `//` inside one is not mistaken for the start of a comment.
    /// Newlines are kept so the line numbers a failure reports are the line numbers in the file.
    /// </summary>
    private static string WithoutComments(string source)
    {
        var output = new StringBuilder(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (current == '/' && next == '/')
            {
                while (index < source.Length && source[index] != '\n') index++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                var close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var stop = close < 0 ? source.Length : close + 2;
                for (var scan = index; scan < stop; scan++)
                {
                    if (source[scan] == '\n') output.Append('\n');
                }

                index = stop;
                continue;
            }

            if (current == '"' && next == '"' && index + 2 < source.Length && source[index + 2] == '"')
            {
                var close = source.IndexOf("\"\"\"", index + 3, StringComparison.Ordinal);
                var stop = close < 0 ? source.Length : close + 3;
                output.Append(source[index..stop]);
                index = stop;
                continue;
            }

            if (current == '@' && next == '"')
            {
                output.Append("@\"");
                index += 2;
                while (index < source.Length)
                {
                    if (source[index] == '"' && index + 1 < source.Length && source[index + 1] == '"')
                    {
                        output.Append("\"\"");
                        index += 2;
                        continue;
                    }

                    output.Append(source[index]);
                    if (source[index++] == '"') break;
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                output.Append(current);
                index++;
                while (index < source.Length)
                {
                    if (source[index] == '\\' && index + 1 < source.Length)
                    {
                        output.Append(source[index]).Append(source[index + 1]);
                        index += 2;
                        continue;
                    }

                    output.Append(source[index]);
                    if (source[index++] == current) break;
                }

                continue;
            }

            output.Append(current);
            index++;
        }

        return output.ToString();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
