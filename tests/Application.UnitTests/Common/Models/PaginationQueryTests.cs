using CleanArchitecture.Application.Common.Models;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Common.Models;

public sealed class PaginationQueryTests
{
    [Test]
    public void Defaults_to_first_page_with_twenty_five_items()
    {
        var query = PaginationQuery.Default;

        query.PageNumber.ShouldBe(1);
        query.PageSize.ShouldBe(25);
        query.Skip.ShouldBe(0);
    }

    [Test]
    public void Clamps_page_size_to_one_hundred()
    {
        var query = new PaginationQuery(0, 500);

        query.PageNumber.ShouldBe(1);
        query.PageSize.ShouldBe(100);
        query.Skip.ShouldBe(0);
    }

    [TestCase(-5, -5, 1, 1)]
    [TestCase(0, 0, 1, 1)]
    [TestCase(3, 10, 3, 10)]
    public void Clamps_rather_than_refuses(int pageNumber, int pageSize, int expectedNumber, int expectedSize)
    {
        var query = new PaginationQuery(pageNumber, pageSize);

        query.PageNumber.ShouldBe(expectedNumber);
        query.PageSize.ShouldBe(expectedSize);
    }

    [Test]
    public void A_huge_page_number_is_clamped_so_skip_never_overflows()
    {
        var query = new PaginationQuery(int.MaxValue, int.MaxValue);

        query.PageNumber.ShouldBe(PaginationQuery.MaxPageNumber);
        query.PageSize.ShouldBe(PaginationQuery.MaxPageSize);
        Should.NotThrow(() => query.Skip).ShouldBeGreaterThanOrEqualTo(0);
    }

    /// <summary>
    /// An omitted binding value is the default page, never every row (PD-1).
    /// </summary>
    [TestCase(null, null, 1, 25)]
    [TestCase(2, null, 2, 25)]
    public void An_omitted_value_takes_the_default_page(int? pageNumber, int? pageSize, int expectedNumber, int expectedSize)
    {
        var query = PaginationQuery.From(pageNumber, pageSize);

        query.PageNumber.ShouldBe(expectedNumber);
        query.PageSize.ShouldBe(expectedSize);
    }
}
