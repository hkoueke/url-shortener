using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Company.Transcodification;

/// <summary>Enregistrement du composant de transcodification dans le conteneur.</summary>
public static class TranscodificationServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre le cache de transcodification, ses options et son préchargement.
    /// </summary>
    /// <remarks>
    /// L'implémentation de <see cref="ITranscodificationRepository"/> reste à la charge
    /// de l'appelant et peut être enregistrée en <c>Scoped</c> ou <c>Transient</c>.
    /// Les options sont validées au démarrage : une valeur aberrante fait échouer le boot
    /// plutôt que de dégrader silencieusement le comportement.
    /// </remarks>
    /// <param name="services">Le conteneur à alimenter.</param>
    /// <param name="configuration">
    /// La configuration, dont la section <see cref="TranscodificationOptions.SectionName"/> est liée.
    /// </param>
    /// <returns>Le conteneur, pour chaînage.</returns>
    /// <exception cref="ArgumentNullException">Un argument est nul.</exception>
    public static IServiceCollection AddTranscodification(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<TranscodificationOptions>()
            .Bind(configuration.GetSection(TranscodificationOptions.SectionName))
            .Validate(
                o => o.Ttl > TimeSpan.Zero && o.RetryBackoff > TimeSpan.Zero,
                $"{TranscodificationOptions.SectionName} : Ttl et RetryBackoff doivent être positifs.")
            .Validate(
                o => o.RetryBackoff <= o.Ttl,
                $"{TranscodificationOptions.SectionName}:RetryBackoff ne doit pas dépasser Ttl.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITranscodificationResolver, TranscodificationCache>();
        services.AddHostedService<TranscodificationWarmUp>();

        return services;
    }
}

/// <summary>
/// Précharge la table au démarrage pour éviter la latence du premier appel.
/// </summary>
/// <remarks>
/// Un échec est journalisé sans interrompre le démarrage : le chargement paresseux prend
/// le relais. Le démarrage ne doit jamais dépendre de la disponibilité de la base ; c'est
/// la sonde <c>ready</c> qui doit refléter l'indisponibilité.
/// </remarks>
/// <param name="resolver">Le cache à précharger.</param>
/// <param name="logger">Journal du préchargement.</param>
internal sealed class TranscodificationWarmUp(
    ITranscodificationResolver resolver,
    ILogger<TranscodificationWarmUp> logger) : IHostedService
{
    /// <summary>
    /// Déclenche le chargement initial, en absorbant toute erreur d'accès aux données.
    /// </summary>
    /// <param name="cancellationToken">Jeton d'arrêt de l'hôte.</param>
    /// <exception cref="OperationCanceledException">L'hôte s'arrête pendant le préchargement.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await resolver.PreloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Préchargement de la transcodification échoué ; nouvelle tentative au premier appel.");
        }
    }

    /// <summary>Aucune ressource à libérer : le cache est disposé par le conteneur.</summary>
    /// <param name="cancellationToken">Jeton d'arrêt de l'hôte.</param>
    /// <returns>Une tâche déjà achevée.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
