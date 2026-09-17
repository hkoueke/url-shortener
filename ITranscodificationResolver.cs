using System.Collections.Immutable;

namespace Company.Transcodification;

/// <summary>
/// Résout la pièce correspondant à un type de document.
/// Thread-safe, enregistré en singleton.
/// </summary>
public interface ITranscodificationResolver
{
    /// <summary>
    /// Retourne l'identifiant de la pièce retenue par défaut pour un type de document.
    /// </summary>
    /// <param name="codeTypeDocument">
    /// Code du type de document. La casse et les espaces de bordure sont ignorés.
    /// </param>
    /// <param name="cancellationToken">Jeton d'annulation de l'appelant.</param>
    /// <returns>
    /// L'identifiant de la pièce, ou <c>null</c> si le type de document est inconnu, ou si
    /// aucune de ses associations ne porte l'indicateur par défaut (anomalie de référentiel :
    /// elle est journalisée au chargement et comptée dans
    /// <see cref="TranscodificationStatus.TypeDocumentSansDefautCount"/>).
    /// Pour distinguer les deux cas, interroger <see cref="GetIdsPieceAsync"/> : une
    /// collection vide signifie « type de document inconnu ».
    /// </returns>
    /// <exception cref="ArgumentException">Le code est vide ou anormalement long.</exception>
    /// <exception cref="TranscodificationUnavailableException">Aucun chargement n'a abouti.</exception>
    ValueTask<string?> GetIdPieceParDefautAsync(
        string codeTypeDocument,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retourne les identifiants de toutes les pièces associées à un type de document.
    /// </summary>
    /// <param name="codeTypeDocument">
    /// Code du type de document. La casse et les espaces de bordure sont ignorés.
    /// </param>
    /// <param name="cancellationToken">Jeton d'annulation de l'appelant.</param>
    /// <returns>
    /// Les identifiants des pièces, dans l'ordre fourni par l'émetteur. Collection
    /// vide — jamais <c>null</c>, jamais <c>default</c> — si le type de document est inconnu.
    /// </returns>
    /// <exception cref="ArgumentException">Le code est vide ou anormalement long.</exception>
    /// <exception cref="TranscodificationUnavailableException">Aucun chargement n'a abouti.</exception>
    ValueTask<ImmutableArray<string>> GetIdsPieceAsync(
        string codeTypeDocument,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Charge la table si elle est absente ou périmée, sans rien retourner. Idempotent.
    /// Destiné au préchargement au démarrage et aux tests d'intégration.
    /// </summary>
    /// <param name="cancellationToken">Jeton d'annulation de l'appelant.</param>
    /// <exception cref="TranscodificationUnavailableException">Aucun chargement n'a abouti.</exception>
    ValueTask PreloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marque le contenu comme périmé : le prochain appel rechargera la table.
    /// Le contenu courant reste disponible comme repli si ce rechargement échoue.
    /// </summary>
    /// <remarks>
    /// N'ouvre aucun accès à la base par elle-même : la lecture ne part qu'au prochain
    /// appel métier. Le cache étant local au processus, cette opération doit être
    /// déclenchée sur chaque instance.
    /// </remarks>
    void Invalidate();

    /// <summary>
    /// Retourne l'état courant du cache, sans déclencher de chargement.
    /// </summary>
    /// <returns>Un instantané exploitable par une sonde de santé ou un diagnostic.</returns>
    TranscodificationStatus GetStatus();
}

/// <summary>État observable du cache, retourné par <see cref="ITranscodificationResolver.GetStatus"/>.</summary>
/// <param name="IsLoaded">Vrai si une table exploitable est en mémoire.</param>
/// <param name="TypeDocumentCount">Nombre de types de documents indexés.</param>
/// <param name="TypeDocumentSansDefautCount">
/// Nombre de types de documents sans pièce par défaut. Toute valeur non nulle est une
/// anomalie de paramétrage à corriger en base.
/// </param>
/// <param name="LoadedAt">Date du dernier chargement réussi (UTC), ou <c>null</c>.</param>
/// <param name="LastFailureKind">
/// Nom du type de la dernière exception, ou <c>null</c>. Le message n'est jamais exposé :
/// il reste dans les journaux, pour ne rien divulguer via une sonde.
/// </param>
public readonly record struct TranscodificationStatus(
    bool IsLoaded,
    int TypeDocumentCount,
    int TypeDocumentSansDefautCount,
    DateTimeOffset? LoadedAt,
    string? LastFailureKind);

/// <summary>
/// La table lue ne contient aucune association exploitable. Signale une donnée
/// incohérente, par opposition à une panne d'accès.
/// </summary>
/// <param name="message">Description de l'incohérence constatée.</param>
public sealed class TranscodificationDataException(string message)
    : Exception(message);

/// <summary>
/// Aucun chargement n'a jamais abouti : le composant ne peut pas répondre.
/// Distincte de <see cref="TranscodificationDataException"/> pour permettre un mapping
/// HTTP 503 côté API.
/// </summary>
/// <param name="message">Description de l'indisponibilité.</param>
/// <param name="innerException">La cause première : panne d'accès ou donnée incohérente.</param>
public sealed class TranscodificationUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
