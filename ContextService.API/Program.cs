
using ContextService.Business;
using ContextService.Business.Services;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddContextServices(builder.Configuration);

var app = builder.Build();

//temporary code for testing 
//using (var scope = app.Services.CreateScope())
//{
//    var ollama = scope.ServiceProvider.GetRequiredService<ContextManager>();

//    string query = "How do we connect C# to SQL Server?";
//    string context = "Our application uses ADO.NET and Microsoft.Data.SqlClient to connect to SQL Server.";

//    decimal score = await ollama.EvaluateRelevanceAsync(query, context);

//    Console.WriteLine($"Ollama Relevance Score: {score}");
//}



// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
