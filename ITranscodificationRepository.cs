namespace Company.Transcodification;

/// <summary>
/// Une entrée de la table de transcodification : un type de document et l'ensemble
/// de ses correspondances connues. La relation est N:N — une même pièce peut figurer
/// dans les associations de plusieurs types de documents.
/// </summary>
/// <remarks>
/// Les données arrivent déjà regroupées par type de document : c'est l'émetteur qui
/// porte ce regroupement, l'indexation ne fait plus que le reprendre et le figer.
/// </remarks>
public sealed class TranscodificationTableEntry
{
    /// <summary>Construit une entrée de la table.</summary>
    /// <param name="codeTypeDocument">Code du type de document. Obligatoire.</param>
    /// <param name="acronyme">Acronyme associé au type de document.</param>
    /// <param name="libelle">Libellé associé au type de document.</param>
    /// <param name="description">Description du document.</param>
    /// <param name="associations">
    /// Associations connues de ce type de document. <c>null</c> est ramené à un tableau vide.
    /// </param>
    /// <param name="produitParCgl">Vrai si le document est produit par CGL.</param>
    /// <exception cref="ArgumentNullException"><paramref name="codeTypeDocument"/> est nul.</exception>
    public TranscodificationTableEntry(
        string codeTypeDocument,
        string? acronyme,
        string? libelle,
        string? description,
        Association[]? associations,
        bool produitParCgl = false)
    {
        CodeTypeDocument = codeTypeDocument ?? throw new ArgumentNullException(nameof(codeTypeDocument));
        Acronyme = acronyme;
        Libelle = libelle;
        Description = description;
        Associations = associations ?? [];
        ProduitParCgl = produitParCgl;
    }

    /// <summary>Code du type de document. Clé de la transcodification.</summary>
    public string CodeTypeDocument { get; }

    /// <summary>Acronyme associé au type de document, ou <c>null</c>.</summary>
    public string? Acronyme { get; }

    /// <summary>Libellé associé au type de document, ou <c>null</c>.</summary>
    public string? Libelle { get; }

    /// <summary>Description du document, ou <c>null</c>.</summary>
    public string? Description { get; }

    /// <summary>
    /// Associations connues de ce type de document, dans l'ordre fourni par l'émetteur.
    /// Jamais <c>null</c> ; peut être vide.
    /// </summary>
    public Association[] Associations { get; }

    /// <summary>Vrai si le document est produit par CGL.</summary>
    public bool ProduitParCgl { get; }
}

/// <summary>
/// Une correspondance connue pour un type de document : la pièce associée, et le fait
/// qu'elle soit ou non celle retenue par défaut.
/// </summary>
public readonly struct Association
{
    /// <summary>Construit une correspondance.</summary>
    /// <param name="idPiece">Identifiant de la pièce associée au document. Obligatoire.</param>
    /// <param name="isDefault">
    /// Vrai si la pièce est l'association par défaut pour ce type de document.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="idPiece"/> est nul.</exception>
    public Association(string idPiece, bool isDefault = false)
    {
        IdPiece = idPiece ?? throw new ArgumentNullException(nameof(idPiece));
        IsDefault = isDefault;
    }

    /// <summary>
    /// Identifiant de la pièce associée au document. Peut être <c>null</c> sur une
    /// valeur par défaut du type (<c>default(Association)</c>), que le constructeur
    /// ne protège pas : l'indexation s'en prémunit.
    /// </summary>
    public string? IdPiece { get; }

    /// <summary>Vrai si la pièce est l'association par défaut pour ce type de document.</summary>
    public bool IsDefault { get; }
}

/// <summary>
/// Port d'accès aux données. L'implémentation lit la table et rien d'autre :
/// ni cache, ni logique de reprise, ni filtrage métier.
/// </summary>
/// <remarks>
/// Peut être enregistrée en <c>Scoped</c> ou <c>Transient</c> : le cache résout
/// l'instance dans un scope dédié à chaque rechargement.
/// </remarks>
public interface ITranscodificationRepository
{
    /// <summary>
    /// Lit l'intégralité de la table (~100 types de documents).
    /// </summary>
    /// <remarks>
    /// L'ordre des associations de chaque entrée devient l'ordre exposé au métier, et
    /// départage un éventuel <see cref="Association.IsDefault"/> en double. Il doit donc
    /// être déterministe.
    /// </remarks>
    /// <param name="cancellationToken">Jeton d'annulation propagé par le cache.</param>
    /// <returns>Les entrées de la table.</returns>
    Task<IReadOnlyList<TranscodificationTableEntry>> GetAllAsync(CancellationToken cancellationToken);
}
