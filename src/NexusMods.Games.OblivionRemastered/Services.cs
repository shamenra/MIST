using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Games;

namespace NexusMods.Games.OblivionRemastered;

public static class Services
{
    public static IServiceCollection AddOblivionRemastered(
        this IServiceCollection services)
    {
        services.AddGame<OblivionRemasteredGame>();
        return services;
    }
}
