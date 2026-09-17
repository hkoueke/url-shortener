namespace Company.Transcodification;

/// <summary>
/// Options du cache de transcodification, liées à la section <see cref="SectionName"/>
/// de la configuration et validées au démarrage.
/// </summary>
/// <remarks>
/// Les accesseurs sont <c>init</c> : une fois l'instance produite par la liaison de
/// configuration, elle ne peut plus être modifiée. Si le projet active le générateur de
/// source de liaison (<c>EnableConfigurationBindingGenerator</c>, activé par défaut en
/// publication AOT ou trimmée), vérifier qu'il accepte les propriétés <c>init</c> ;
/// à défaut, revenir à <c>set</c> — le cache fige de toute façon ces valeurs à sa
/// construction, la mutation ultérieure serait sans effet sur lui.
/// </remarks>
public sealed class TranscodificationOptions
{
    /// <summary>Nom de la section de configuration liée à ces options.</summary>
    public const string SectionName = "Transcodification";

    /// <summary>
    /// Durée de validité du contenu en mémoire. C'est le délai maximal de propagation
    /// d'une modification faite en base, par instance applicative : deux instances qui
    /// rechargent à des instants décalés peuvent diverger d'autant.
    /// </summary>
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Délai minimal entre deux tentatives après un échec. Évite qu'une base
    /// indisponible reçoive une tentative de connexion par requête entrante.
    /// Ne doit pas dépasser <see cref="Ttl"/>.
    /// </summary>
    public TimeSpan RetryBackoff { get; init; } = TimeSpan.FromMinutes(1);
}
