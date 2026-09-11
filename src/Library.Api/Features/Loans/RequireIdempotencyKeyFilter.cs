namespace Library.Api.Features.Loans;

public sealed class RequireIdempotencyKeyFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string Endpoint = "POST /loans";
    public const string ReplayedHeaderName = "Idempotency-Replayed";

    private static readonly TimeSpan ExpirationWindow = TimeSpan.FromHours(24);

    // Limite de tentativas de reserva: a segunda existe só para a corrida rara em que a linha
    // `InFlight` some entre o `INSERT` falhar e a leitura seguinte (outra requisição liberou a
    // chave nesse intervalo) — nesse caso o slot ficou livre e a reserva deve ser refeita, não
    // tratada como erro. Não é um retry de contenção geral.
    private const int MaxReservationAttempts = 2;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Nullable de propósito: os testes de unidade deste filtro invocam `InvokeAsync` com um
        // `DefaultHttpContext` sem `RequestServices`, cobrindo só o caminho de header ausente
        // (sem banco). Em produção o `AddSingleton<LoanMetrics>()` garante que nunca é nulo.
        var loanMetrics = httpContext.RequestServices?.GetService<LoanMetrics>();
        var stopwatch = Stopwatch.StartNew();

        var result = await ExecuteAsync(context, next, loanMetrics);

        stopwatch.Stop();
        loanMetrics?.RecordCreateDuration(stopwatch.Elapsed.TotalMilliseconds, ClassifyOutcome(httpContext, result));

        return result;
    }

    // O outcome da duração é classificado pelo resultado devolvido, não recalculado a partir das
    // regras de negócio: o header de replay e o status 201 já são a fonte da verdade sobre o que
    // aconteceu, sem duplicar a decisão que CreateLoan/HandleExistingKeyAsync já tomaram.
    private static string ClassifyOutcome(HttpContext httpContext, object? result)
    {
        if (httpContext.Response.Headers.ContainsKey(ReplayedHeaderName))
        {
            return LoanMetrics.OutcomeReplayed;
        }

        return result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status201Created }
            ? LoanMetrics.OutcomeCreated
            : LoanMetrics.OutcomeRejected;
    }

    private static async Task<object?> ExecuteAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next, LoanMetrics? loanMetrics)
    {
        var httpContext = context.HttpContext;
        var key = httpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(key))
        {
            return LoanErrors.IdempotencyKeyRequired().ToProblem();
        }

        var dbContext = httpContext.RequestServices.GetRequiredService<AppDbContext>();
        var timeProvider = httpContext.RequestServices.GetRequiredService<TimeProvider>();
        var cancellationToken = httpContext.RequestAborted;

        var requestHash = await ComputeRequestHashAsync(httpContext.Request, cancellationToken);

        for (var attempt = 1; attempt <= MaxReservationAttempts; attempt++)
        {
            var now = timeProvider.GetUtcNow();
            var expiresAtUtc = now.Add(ExpirationWindow);

            var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO idempotency_keys (key, endpoint, request_hash, state, created_at_utc, expires_at_utc)
                 VALUES ({key}, {Endpoint}, {requestHash}, 'InFlight', {now}, {expiresAtUtc})
                 ON CONFLICT (key, endpoint) DO NOTHING
                 """,
                cancellationToken);

            if (inserted == 1)
            {
                return await ProcessReservedKeyAsync(context, next, dbContext, key, cancellationToken);
            }

            var decision = await HandleExistingKeyAsync(dbContext, httpContext, key, requestHash, loanMetrics, cancellationToken);
            if (decision is not RowVanished)
            {
                return decision;
            }

            // A linha vista pelo INSERT como conflito já não existe mais: outra requisição liberou
            // a chave entre o conflito e esta leitura. Tenta reservar de novo em vez de propagar erro.
        }

        // Nunca deveria chegar aqui em uso normal — as duas tentativas cobrem a única corrida
        // possível (liberação concorrente). Se persistir, trata como requisição em voo.
        return LoanErrors.RequestInFlight().ToProblem();
    }

    private static async Task<object?> ProcessReservedKeyAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        AppDbContext dbContext,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await next(context);

            if (result is not IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
            {
                await ReleaseKeyAsync(dbContext, key, cancellationToken);
            }

            return result;
        }
        catch
        {
            await ReleaseKeyAsync(dbContext, key, cancellationToken);
            throw;
        }
    }

    // Sentinela devolvido por HandleExistingKeyAsync quando a linha vista como conflito pelo INSERT
    // já não existe mais na leitura seguinte — distinto de qualquer IResult de negócio real.
    private sealed class RowVanished;

    private static async Task<object?> HandleExistingKeyAsync(
        AppDbContext dbContext,
        HttpContext httpContext,
        string key,
        string requestHash,
        LoanMetrics? loanMetrics,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.IdempotencyKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(k => k.Key == key && k.Endpoint == Endpoint, cancellationToken);

        if (existing is null)
        {
            return new RowVanished();
        }

        if (existing.RequestHash != requestHash)
        {
            return LoanErrors.IdempotencyKeyReuse().ToProblem();
        }

        if (existing.State == IdempotencyState.InFlight)
        {
            return LoanErrors.RequestInFlight().ToProblem();
        }

        if (existing.ResponseBody is not { } responseBody || existing.StatusCode is not { } statusCode)
        {
            throw new InvalidOperationException(
                $"Idempotency key '{key}' is Completed but has no stored response — the invariant that only the single Completed writer (CreateLoan) sets these fields together was violated.");
        }

        httpContext.Response.Headers[ReplayedHeaderName] = "true";
        loanMetrics?.RecordIdempotentReplay();
        return Results.Content(responseBody.RootElement.GetRawText(), "application/json", statusCode: statusCode);
    }

    private static async Task<string> ComputeRequestHashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var bodyStream = new MemoryStream();
        await request.Body.CopyToAsync(bodyStream, cancellationToken);
        request.Body.Position = 0;

        using var sha256 = SHA256.Create();
        sha256.TransformBlock(bodyStream.GetBuffer(), 0, (int)bodyStream.Length, null, 0);
        var routeBytes = Encoding.UTF8.GetBytes(Endpoint);
        sha256.TransformFinalBlock(routeBytes, 0, routeBytes.Length);

        var hash = sha256.Hash ?? throw new InvalidOperationException("SHA-256 hash was not computed after TransformFinalBlock.");
        return Convert.ToHexString(hash);
    }

    private static Task ReleaseKeyAsync(AppDbContext dbContext, string key, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM idempotency_keys WHERE key = {key} AND endpoint = {Endpoint} AND state = 'InFlight'",
            cancellationToken);
}
