using Library.IntegrationTests.Features.Loans;

namespace Library.IntegrationTests.Infrastructure;

public class TracingTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await ((IAsyncLifetime)_fixture).InitializeAsync();
        _client = _fixture.CreateClient();
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_fixture).DisposeAsync();

    [Fact]
    public async Task Health_endpoints_do_not_produce_an_http_server_trace()
    {
        await _client.GetAsync("/health/live");
        await _client.GetAsync("/health/ready");

        // Aguarda o flush do processador simples usado pelo `AddInMemoryExporter` em teste.
        await Task.Delay(200);

        Assert.DoesNotContain(_fixture.ExportedActivities, activity => activity.Kind == ActivityKind.Server);
    }

    [Fact]
    public async Task Loan_creation_request_produces_a_trace_correlated_with_the_postgres_spans()
    {
        var book = await LoanTestHelpers.CreateBookAsync(_client);
        var userId = await LoanTestHelpers.CreateUserAsync(_fixture);

        _fixture.ExportedActivities.Clear();

        var response = await LoanTestHelpers.PostLoanAsync(_client, book.Id, userId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await Task.Delay(200);

        var httpActivity = Assert.Single(_fixture.ExportedActivities, activity =>
            activity.Kind == ActivityKind.Server && activity.DisplayName.Contains("/loans"));

        Assert.Contains(_fixture.ExportedActivities, activity =>
            activity.Source.Name == "Npgsql" && activity.TraceId == httpActivity.TraceId);
    }
}
