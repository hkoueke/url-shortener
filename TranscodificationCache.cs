using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Company.Transcodification;

/// <summary>
/// Cache mémoire de la table de transcodification « type de document ↔ pièces », avec
/// la pièce retenue par défaut pour chaque type de document.
/// </summary>
/// <remarks>
/// <para><b>Responsabilité</b> : le cycle de vie du contenu — quand charger, quand
/// considérer périmé, que servir en cas de panne. L'interprétation des lignes de la
/// table relève de <see cref="TranscodificationIndexBuilder"/>.</para>
/// <para><b>Lecture</b> : un index immuable publié par écriture atomique. Aucun verrou,
/// aucune allocation sur le chemin nominal.</para>
/// <para><b>Chargement</b> : paresseux, puis à chaque péremption. Un sémaphore garantit
/// qu'un seul rechargement est en vol à la fois.</para>
/// <para><b>Résilience</b> : si un rechargement échoue alors qu'un index existe, celui-ci
/// continue d'être servi et la tentative suivante est différée. Si aucun chargement n'a
/// jamais abouti, l'erreur remonte — mais reste mémorisée le temps du délai de reprise,
/// pour ne pas saturer une base indisponible.</para>
/// <para><b>Durée de vie</b> : singleton. L'accès aux données passe par un scope dédié,
/// ce qui autorise un repository enregistré en <c>Scoped</c> sans captive dependency.</para>
/// </remarks>
public sealed class TranscodificationCache : ITranscodificationResolver, IDisposable
{
    /// <summary>
    /// Longueur maximale acceptée pour un code fourni par un appelant. Garde-fou contre
    /// une entrée externe arbitrairement longue — la sortie de l'IDP, notamment.
    /// </summary>
    private const int MaxCodeLength = 128;

    /// <summary>Message unique porté par <see cref="TranscodificationUnavailableException"/>.</summary>
    private const string UnavailableMessage =
        "Table de transcodification indisponible : aucun chargement n'a abouti.";

    /// <summary>Fabrique de scopes DI, utilisée pour résoudre le repository au chargement.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Valeurs de configuration, figées à la construction.</summary>
    private readonly TranscodificationOptions _options;

    /// <summary>Journal du composant.</summary>
    private readonly ILogger<TranscodificationCache> _logger;

    /// <summary>Source de temps, injectée pour rendre TTL et délai de reprise testables.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Sérialise les rechargements : un seul accès base en vol à la fois.</summary>
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>
    /// Contenu courant du cache, ou <c>null</c> avant toute tentative de chargement.
    /// Publié et lu par <see cref="Volatile"/> / <see cref="Interlocked"/> : jamais muté
    /// sur place, toujours remplacé par une nouvelle instance.
    /// </summary>
    private CacheContent? _content;

    /// <summary>
    /// Compteur d'invalidations, incrémenté par <see cref="Invalidate"/>. Un rechargement
    /// le relève avant de lire la base et le revérifie avant de publier : s'il a changé,
    /// les données lues peuvent être antérieures à la modification annoncée, et le contenu
    /// est publié déjà périmé.
    /// </summary>
    private long _invalidationEpoch;

    /// <summary>Vrai une fois <see cref="Dispose"/> appelé.</summary>
    private volatile bool _disposed;

    /// <summary>Construit le cache. Toutes les dépendances sont obligatoires.</summary>
    /// <param name="scopeFactory">Fabrique de scopes DI résolvant le repository.</param>
    /// <param name="options">Configuration : durée de validité et délai de reprise.</param>
    /// <param name="logger">Journal du composant.</param>
    /// <param name="timeProvider">Source de temps.</param>
    /// <exception cref="ArgumentNullException">Une dépendance est nulle.</exception>
    public TranscodificationCache(
        IServiceScopeFactory scopeFactory,
        IOptions<TranscodificationOptions> options,
        ILogger<TranscodificationCache> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    // ---------------------------------------------------------------- API publique

    /// <inheritdoc />
    public async ValueTask<string?> GetIdPieceParDefautAsync(
        string codeTypeDocument,
        CancellationToken cancellationToken = default)
    {
        var typeDocument = await FindTypeDocumentAsync(codeTypeDocument, cancellationToken)
            .ConfigureAwait(false);

        return typeDocument?.IdPieceParDefaut;
    }

    /// <inheritdoc />
    public async ValueTask<ImmutableArray<string>> GetIdsPieceAsync(
        string codeTypeDocument,
        CancellationToken cancellationToken = default)
    {
        var typeDocument = await FindTypeDocumentAsync(codeTypeDocument, cancellationToken)
            .ConfigureAwait(false);

        return typeDocument?.IdsPiece ?? ImmutableArray<string>.Empty;
    }

    /// <inheritdoc />
    public async ValueTask<bool?> IsProduitParCglAsync(
        string codeTypeDocument,
        CancellationToken cancellationToken = default)
    {
        var typeDocument = await FindTypeDocumentAsync(codeTypeDocument, cancellationToken)
            .ConfigureAwait(false);

        return typeDocument?.ProduitParCgl;
    }

    /// <inheritdoc />
    public async ValueTask<ImmutableArray<string>> GetCodesTypeDocumentAsync(
        bool produitParCgl,
        CancellationToken cancellationToken = default)
    {
        var index = await GetCurrentIndexAsync(cancellationToken).ConfigureAwait(false);
        return index.CodesParOrigine(produitParCgl);
    }

    /// <inheritdoc />
    public async ValueTask PreloadAsync(CancellationToken cancellationToken = default)
    {
        await GetCurrentIndexAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        // Relevé d'abord : un rechargement déjà en vol constatera ce changement avant de
        // publier, et publiera son contenu périmé plutôt que de masquer l'invalidation.
        Interlocked.Increment(ref _invalidationEpoch);

        // Boucle de compare-and-swap : une tentative unique pourrait être supplantée par
        // un rechargement publiant au même instant, et l'invalidation serait alors perdue.
        CacheContent? current;
        CacheContent? expired = null;

        do
        {
            current = Volatile.Read(ref _content);
            if (current is null)
            {
                break;
            }

            expired = current with { NextAttemptAt = DateTimeOffset.MinValue };
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _content, expired, current), current));

        _logger.LogInformation("Transcodification invalidée ; rechargement au prochain appel.");
    }

    /// <inheritdoc />
    public TranscodificationStatus GetStatus()
    {
        var content = Volatile.Read(ref _content);

        return new TranscodificationStatus(
            IsLoaded: content?.Index is not null,
            TypeDocumentCount: content?.Index?.ParTypeDocument.Count ?? 0,
            TypeDocumentSansDefautCount: content?.Index?.SansDefautCount ?? 0,
            LoadedAt: content?.LoadedAt,
            LastFailureKind: content?.LastError?.GetType().Name);
    }

    /// <summary>
    /// Libère le sémaphore de rechargement. Le composant étant un singleton, l'appel
    /// intervient à l'arrêt de l'application, par le conteneur.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshGate.Dispose();
    }

    // ------------------------------------------------------------------- Lecture

    /// <summary>
    /// Chemin commun à toutes les lectures ciblant un type de document : valide le code,
    /// obtient l'index à jour et y cherche l'entrée.
    /// </summary>
    /// <param name="codeTypeDocument">Code fourni par l'appelant, non encore normalisé.</param>
    /// <param name="cancellationToken">Jeton d'annulation de l'appelant.</param>
    /// <returns>Le type de document indexé, ou <c>null</c> s'il est inconnu.</returns>
    /// <exception cref="ArgumentException">Le code est vide ou anormalement long.</exception>
    private async ValueTask<TypeDocumentIndexe?> FindTypeDocumentAsync(
        string codeTypeDocument,
        CancellationToken cancellationToken)
    {
        var code = NormalizeCode(codeTypeDocument, nameof(codeTypeDocument));
        var index = await GetCurrentIndexAsync(cancellationToken).ConfigureAwait(false);

        return index.ParTypeDocument.TryGetValue(code, out var typeDocument) ? typeDocument : null;
    }

    /// <summary>
    /// Retourne l'index à jour, en déclenchant un rechargement s'il est absent ou périmé.
    /// </summary>
    /// <param name="cancellationToken">Jeton d'annulation de l'appelant.</param>
    /// <returns>L'index exploitable.</returns>
    /// <exception cref="ObjectDisposedException">Le composant a été libéré.</exception>
    /// <exception cref="TranscodificationUnavailableException">Aucun chargement n'a abouti.</exception>
    private async ValueTask<TranscodificationIndex> GetCurrentIndexAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var content = Volatile.Read(ref _content);

        // Chemin nominal : contenu encore valide, aucune synchronisation, aucune allocation.
        if (content is not null && _timeProvider.GetUtcNow() < content.NextAttemptAt)
        {
            return content.GetIndexOrFail();
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Recharge la table sous sémaphore, de sorte qu'un seul accès base soit en vol
    /// quel que soit le nombre d'appelants concurrents.
    /// </summary>
    /// <param name="cancellationToken">
    /// Jeton de l'appelant qui porte le rechargement. Son annulation interrompt la
    /// tentative sans marquer le cache en échec ; un appelant en attente en relancera une.
    /// </param>
    /// <returns>L'index fraîchement chargé, ou le précédent si le rechargement a échoué.</returns>
    /// <exception cref="TranscodificationUnavailableException">
    /// Le chargement a échoué et aucun contenu antérieur n'est disponible.
    /// </exception>
    private async ValueTask<TranscodificationIndex> RefreshAsync(
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var content = Volatile.Read(ref _content);

            // Un appelant concurrent a pu recharger — ou constater l'indisponibilité —
            // pendant l'attente au sémaphore.
            if (content is not null && now < content.NextAttemptAt)
            {
                return content.GetIndexOrFail();
            }

            try
            {
                var epochBeforeLoad = Interlocked.Read(ref _invalidationEpoch);

                var entries = await LoadEntriesAsync(cancellationToken).ConfigureAwait(false);
                var index = TranscodificationIndexBuilder.Build(entries);
                LogAnomalies(index);

                // Une invalidation survenue pendant la lecture peut viser une modification
                // postérieure à celle-ci : le contenu est alors publié déjà périmé, plutôt
                // que servi pendant tout un TTL.
                var invalidatedDuringLoad =
                    Interlocked.Read(ref _invalidationEpoch) != epochBeforeLoad;

                var nextAttemptAt = invalidatedDuringLoad
                    ? DateTimeOffset.MinValue
                    : now + _options.Ttl;

                Volatile.Write(
                    ref _content, new CacheContent(index, now, nextAttemptAt, LastError: null));

                LogLoaded(index, nextAttemptAt, invalidatedDuringLoad);

                return index;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Annulation de l'appelant : ce n'est pas une panne, on ne pénalise pas le cache.
                throw;
            }
            catch (Exception ex)
            {
                return ServePreviousOrFail(ex, content, now);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Applique la dégradation contrôlée après un chargement en échec : conserve le
    /// contenu précédent s'il existe, et diffère la tentative suivante.
    /// </summary>
    /// <param name="exception">La cause de l'échec, journalisée et mémorisée dans le contenu.</param>
    /// <param name="previous">Le contenu d'avant la tentative, éventuellement <c>null</c>.</param>
    /// <param name="now">Instant de la tentative, servant de base au délai de reprise.</param>
    /// <returns>L'index précédent, s'il existe.</returns>
    /// <exception cref="TranscodificationUnavailableException">
    /// Toujours levée lorsqu'aucun contenu antérieur n'est disponible.
    /// </exception>
    private TranscodificationIndex ServePreviousOrFail(
        Exception exception,
        CacheContent? previous,
        DateTimeOffset now)
    {
        var retryAt = now + _options.RetryBackoff;

        // Le contenu précédent, s'il existe, est reconduit tel quel : seule sa date de
        // prochaine tentative et la dernière erreur changent.
        Volatile.Write(
            ref _content, new CacheContent(previous?.Index, previous?.LoadedAt, retryAt, exception));

        if (previous?.Index is not null)
        {
            _logger.LogError(
                exception,
                "Échec du rechargement de la transcodification. Contenu du {LoadedAt:O} conservé ; "
                + "prochaine tentative à partir de {RetryAt:O}.",
                previous.LoadedAt,
                retryAt);

            return previous.Index;
        }

        _logger.LogCritical(
            exception,
            "Échec du chargement de la transcodification ; aucun repli disponible. "
            + "Prochaine tentative à partir de {RetryAt:O}.",
            retryAt);

        throw new TranscodificationUnavailableException(UnavailableMessage, exception);
    }

    /// <summary>
    /// Lit la table via le repository, résolu dans un scope dédié.
    /// </summary>
    /// <remarks>
    /// Le scope est indispensable : sans lui, un repository enregistré en <c>Scoped</c>
    /// — le réflexe par défaut avec EF Core — constituerait une captive dependency de ce
    /// singleton et ferait échouer la validation du conteneur au démarrage.
    /// </remarks>
    /// <param name="cancellationToken">Jeton d'annulation de l'appelant.</param>
    /// <returns>Les lignes brutes de la table, dans l'ordre de la requête.</returns>
    /// <exception cref="TranscodificationDataException">Le repository a retourné <c>null</c>.</exception>
    private async Task<IReadOnlyList<TranscodificationTableEntry>> LoadEntriesAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITranscodificationRepository>();

        return await repository.GetAllAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new TranscodificationDataException("Le repository a retourné null.");
    }

    // ------------------------------------------------------------- Journalisation

    /// <summary>Rend compte d'un chargement abouti.</summary>
    /// <param name="index">L'index qui vient d'être publié.</param>
    /// <param name="nextAttemptAt">Date de péremption retenue.</param>
    /// <param name="invalidatedDuringLoad">
    /// Vrai si une invalidation a croisé la lecture, auquel cas le contenu est publié
    /// déjà périmé — le signaler évite de laisser croire à une boucle de rechargement.
    /// </param>
    private void LogLoaded(
        TranscodificationIndex index,
        DateTimeOffset nextAttemptAt,
        bool invalidatedDuringLoad)
    {
        if (invalidatedDuringLoad)
        {
            _logger.LogInformation(
                "Transcodification chargée ({TypeDocumentCount} types de documents) puis invalidée "
                + "pendant la lecture : un nouveau chargement partira au prochain appel.",
                index.ParTypeDocument.Count);

            return;
        }

        _logger.LogInformation(
            "Transcodification chargée : {TypeDocumentCount} types de documents, "
            + "valide jusqu'à {ExpiresAt:O}.",
            index.ParTypeDocument.Count,
            nextAttemptAt);
    }

    /// <summary>
    /// Rend compte des anomalies de référentiel relevées par l'indexation, en un seul
    /// enregistrement agrégé plutôt qu'un par ligne fautive.
    /// </summary>
    /// <param name="index">Le résultat de l'indexation, porteur des décomptes.</param>
    private void LogAnomalies(TranscodificationIndex index)
    {
        if (!index.HasAnomalies)
        {
            return;
        }

        _logger.LogWarning(
            "Anomalies dans la table de transcodification : {EntreesIgnoreesCount} entrée(s) écartée(s), "
            + "{AssociationsIgnoreesCount} association(s) ignorée(s), {DefautsMultiplesCount} indicateur(s) "
            + "« par défaut » surnuméraire(s), {SansDefautCount} type(s) de document sans pièce par défaut.",
            index.EntreesIgnoreesCount,
            index.AssociationsIgnoreesCount,
            index.DefautsMultiplesCount,
            index.SansDefautCount);
    }

    // ------------------------------------------------------------ Validation entrée

    /// <summary>
    /// Valide et normalise un code fourni par un appelant, avant toute recherche.
    /// </summary>
    /// <param name="value">Le code brut.</param>
    /// <param name="parameterName">Nom du paramètre d'origine, porté par l'exception.</param>
    /// <returns>
    /// Le code débarrassé de ses espaces de bordure. <see cref="string.Trim()"/> renvoie
    /// l'instance d'origine s'il n'y a rien à rogner : aucune allocation sur le cas courant.
    /// </returns>
    /// <exception cref="ArgumentException">Le code est vide ou excède <see cref="MaxCodeLength"/>.</exception>
    private static string NormalizeCode(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Length > MaxCodeLength)
        {
            throw new ArgumentException(
                $"Le code fourni excède {MaxCodeLength} caractères.", parameterName);
        }

        return value.Trim();
    }

    // -------------------------------------------------------------- Contenu publié

    /// <summary>
    /// Ce que le cache détient à un instant donné : l'index, sa fraîcheur et la dernière
    /// erreur rencontrée. Immuable et remplacé d'un bloc, ce qui permet aux lecteurs de
    /// n'observer jamais d'état partiel sans prendre de verrou.
    /// </summary>
    /// <param name="Index">
    /// L'index exploitable, ou <c>null</c> tant qu'aucun chargement n'a abouti. Il porte
    /// aussi les décomptes d'anomalies : les recopier ici les ferait diverger.
    /// </param>
    /// <param name="LoadedAt">Date du dernier chargement réussi, ou <c>null</c>.</param>
    /// <param name="NextAttemptAt">
    /// Instant à partir duquel un rechargement sera tenté. Porte indifféremment la
    /// péremption normale et le délai de reprise après échec — c'est cette unification
    /// qui évite d'avoir deux états concurrents à tenir cohérents.
    /// </param>
    /// <param name="LastError">Dernière exception rencontrée, ou <c>null</c>.</param>
    private sealed record CacheContent(
        TranscodificationIndex? Index,
        DateTimeOffset? LoadedAt,
        DateTimeOffset NextAttemptAt,
        Exception? LastError)
    {
        /// <summary>Retourne l'index, ou signale l'indisponibilité si aucun n'a été chargé.</summary>
        /// <returns>L'index exploitable.</returns>
        /// <exception cref="TranscodificationUnavailableException">
        /// Aucun chargement n'a abouti ; la cause première est portée en exception interne.
        /// </exception>
        public TranscodificationIndex GetIndexOrFail() =>
            Index ?? throw new TranscodificationUnavailableException(UnavailableMessage, LastError);
    }
}
