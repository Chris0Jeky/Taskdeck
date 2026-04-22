using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class BoardPaginationApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BoardPaginationApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetBoards_DefaultPagination_ReturnsWrappedResult()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pagination-default");
        await ApiTestHarness.CreateBoardAsync(client, "pg-default");

        var result = await ApiTestHarness.ListBoardsPaginatedAsync(client);

        result.Items.Should().NotBeEmpty();
        result.TotalCount.Should().BeGreaterOrEqualTo(1);
        result.Offset.Should().Be(0);
        result.Limit.Should().Be(50, "default limit should be 50");
    }

    [Fact]
    public async Task GetBoards_EmptyList_ReturnsPaginationMetadata()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pagination-empty");

        var result = await ApiTestHarness.ListBoardsPaginatedAsync(client);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.HasMore.Should().BeFalse();
        result.Offset.Should().Be(0);
    }

    [Fact]
    public async Task GetBoards_WithLimit_ReturnsClampedPageSize()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pagination-limit");
        // Create 3 boards
        await ApiTestHarness.CreateBoardAsync(client, "pg-limit-1");
        await ApiTestHarness.CreateBoardAsync(client, "pg-limit-2");
        await ApiTestHarness.CreateBoardAsync(client, "pg-limit-3");

        var result = await ApiTestHarness.ListBoardsPaginatedAsync(client, limit: 2);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(3);
        result.HasMore.Should().BeTrue();
        result.Limit.Should().Be(2);
    }

    [Fact]
    public async Task GetBoards_WithOffset_SkipsBoards()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pagination-offset");
        await ApiTestHarness.CreateBoardAsync(client, "pg-offset-1");
        await ApiTestHarness.CreateBoardAsync(client, "pg-offset-2");
        await ApiTestHarness.CreateBoardAsync(client, "pg-offset-3");

        var result = await ApiTestHarness.ListBoardsPaginatedAsync(client, offset: 1, limit: 2);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(3);
        result.Offset.Should().Be(1);
    }

    [Fact]
    public async Task GetBoards_PartialPage_ReturnsHasMoreFalse()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pagination-partial");
        await ApiTestHarness.CreateBoardAsync(client, "pg-partial-1");
        await ApiTestHarness.CreateBoardAsync(client, "pg-partial-2");

        // Request limit larger than total count
        var result = await ApiTestHarness.ListBoardsPaginatedAsync(client, limit: 50);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task GetBoards_LimitOverMax_ClampedTo200()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pagination-max");
        await ApiTestHarness.CreateBoardAsync(client, "pg-max");

        var result = await ApiTestHarness.ListBoardsPaginatedAsync(client, limit: 500);

        result.Limit.Should().Be(200, "limit should be clamped to max 200");
    }

    [Fact]
    public async Task GetBoards_NegativeOffset_TreatedAsZero()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pagination-neg-offset");
        await ApiTestHarness.CreateBoardAsync(client, "pg-neg");

        var result = await ApiTestHarness.ListBoardsPaginatedAsync(client, offset: -5, limit: 10);

        result.Offset.Should().Be(0);
        result.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetBoards_OffsetBeyondTotal_ReturnsEmptyPage()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pagination-beyond");
        await ApiTestHarness.CreateBoardAsync(client, "pg-beyond");

        var result = await ApiTestHarness.ListBoardsPaginatedAsync(client, offset: 100, limit: 10);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(1);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task GetBoards_ZeroLimit_ClampedToOne()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pagination-zero");
        await ApiTestHarness.CreateBoardAsync(client, "pg-zero-1");
        await ApiTestHarness.CreateBoardAsync(client, "pg-zero-2");

        var result = await ApiTestHarness.ListBoardsPaginatedAsync(client, limit: 0);

        result.Items.Should().HaveCount(1, "limit=0 should be clamped to 1");
        result.HasMore.Should().BeTrue();
        result.Limit.Should().Be(1);
    }

    [Fact]
    public async Task GetBoards_PaginationIteratesAllBoards()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pagination-iterate");
        var board1 = await ApiTestHarness.CreateBoardAsync(client, "pg-iter-1");
        var board2 = await ApiTestHarness.CreateBoardAsync(client, "pg-iter-2");
        var board3 = await ApiTestHarness.CreateBoardAsync(client, "pg-iter-3");

        var allBoardIds = new List<Guid>();

        // Iterate page by page with limit=1
        var page1 = await ApiTestHarness.ListBoardsPaginatedAsync(client, offset: 0, limit: 1);
        page1.Items.Should().HaveCount(1);
        page1.TotalCount.Should().Be(3);
        page1.HasMore.Should().BeTrue();
        allBoardIds.AddRange(page1.Items.Select(b => b.Id));

        var page2 = await ApiTestHarness.ListBoardsPaginatedAsync(client, offset: 1, limit: 1);
        page2.Items.Should().HaveCount(1);
        page2.HasMore.Should().BeTrue();
        allBoardIds.AddRange(page2.Items.Select(b => b.Id));

        var page3 = await ApiTestHarness.ListBoardsPaginatedAsync(client, offset: 2, limit: 1);
        page3.Items.Should().HaveCount(1);
        page3.HasMore.Should().BeFalse();
        allBoardIds.AddRange(page3.Items.Select(b => b.Id));

        // All three boards should appear across the pages with no duplicates
        allBoardIds.Should().HaveCount(3);
        allBoardIds.Distinct().Should().HaveCount(3);
        allBoardIds.Should().Contain(board1.Id);
        allBoardIds.Should().Contain(board2.Id);
        allBoardIds.Should().Contain(board3.Id);
    }

    [Fact]
    public async Task GetBoards_CrossUserIsolation_WithPagination()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "pagination-iso-a");
        await ApiTestHarness.CreateBoardAsync(clientA, "pg-iso-a");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "pagination-iso-b");
        await ApiTestHarness.CreateBoardAsync(clientB, "pg-iso-b");

        var resultA = await ApiTestHarness.ListBoardsPaginatedAsync(clientA);
        var resultB = await ApiTestHarness.ListBoardsPaginatedAsync(clientB);

        resultA.TotalCount.Should().Be(1);
        resultB.TotalCount.Should().Be(1);
        resultA.Items.Single().Id.Should().NotBe(resultB.Items.Single().Id);
    }
}
