using CleanArchitecture.Application.Common.Models;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Common.Models;

public sealed class PaginatedListTests
{
    [Test]
    public void Computes_total_pages_and_navigation_flags()
    {
        var page = new PaginatedList<int>([4, 5, 6], PageNumber: 2, PageSize: 3, TotalCount: 8);

        page.TotalPages.ShouldBe(3);
        page.HasPreviousPage.ShouldBeTrue();
        page.HasNextPage.ShouldBeTrue();
    }

    [Test]
    public void An_empty_collection_has_no_pages_and_no_neighbours()
    {
        var page = new PaginatedList<int>([], PageNumber: 1, PageSize: 25, TotalCount: 0);

        page.TotalPages.ShouldBe(0);
        page.HasPreviousPage.ShouldBeFalse();
        page.HasNextPage.ShouldBeFalse();
    }

    [Test]
    public void A_page_past_the_end_keeps_the_real_totals_and_has_no_next_page()
    {
        var page = new PaginatedList<int>([], PageNumber: 5, PageSize: 2, TotalCount: 3);

        page.TotalPages.ShouldBe(2);
        page.HasNextPage.ShouldBeFalse();
    }
}
