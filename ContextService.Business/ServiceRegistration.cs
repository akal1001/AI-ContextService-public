
using ContextService.Business.Services;
using ContextService.Data.Database;
using ContextService.Data.Repositories;
using ContextService.LLM.Configuration;
using ContextService.LLM.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContextService.Business;

public static class ServiceRegistration
{
    public static IServiceCollection AddContextServices(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("ContextDatabase") ?? throw new InvalidOperationException("Missing database connection string.");

        services.AddSingleton(new SqlConnectionFactory(connectionString));
        services.AddScoped<ContextRepository>();
        services.AddScoped<ContextManager>();

        services.AddSingleton(new LlmOptions
        {
            BaseUrl = configuration["LLM:BaseUrl"] ?? "http://localhost:11434",
            Model = configuration["LLM:Model"] ?? "llama3.2"
        });

        services.AddHttpClient<OllamaClient>(client => client.Timeout = TimeSpan.FromMinutes(5));

        return services;
    }
}
