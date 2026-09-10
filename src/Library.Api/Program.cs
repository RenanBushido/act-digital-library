var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApiDatabase(builder.Configuration);
builder.Services.AddApiCaching(builder.Configuration);
builder.Services.AddApiProblemDetails();
builder.Services.AddValidation();
builder.Services.Configure<LoanOptions>(builder.Configuration.GetSection(LoanOptions.SectionName));

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();

app.UseMiddleware<CorrelationIdMiddleware>();

app.MapBooksEndpoints();
app.MapLoansEndpoints();
app.MapUsersEndpoints();

app.Run();

public partial class Program;
