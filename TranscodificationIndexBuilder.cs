using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Company.Transcodification;

/// <summary>
/// Un type de document tel qu'il est exploité en lecture : ses pièces nettoyées et figées,
/// celle retenue par défaut, et son origine.
/// </summary>
/// <param name="IdsPiece">
/// Identifiants des pièces associées, dans l'ordre fourni par l'émetteur. Jamais vide :
/// une entrée sans aucune pièce exploitable n'est pas indexée.
/// </param>
/// <param name="IdPieceParDefaut">
/// Identifiant de la pièce retenue par défaut, ou <c>null</c> si aucune association de
/// l'entrée ne portait l'indicateur.
/// </param>
/// <param name="ProduitParCgl">Vrai si le document est produit par CGL.</param>
internal sealed record TypeDocumentIndexe(
    ImmutableArray<string> IdsPiece,
    string? IdPieceParDefaut,
    bool ProduitParCgl);

/// <summary>
/// Résultat de l'indexation : l'index exploitable, les vues précalculées, et le décompte
/// des anomalies rencontrées.
/// </summary>
/// <param name="ParTypeDocument">
/// L'index lui-même : à chaque code de type de document, ses données figées.
/// </param>
/// <param name="CodesProduitsParCgl">
/// Codes des types de documents produits par CGL, dans l'ordre de la table. Précalculé :
/// le filtrage est une lecture, pas un parcours.
/// </param>
/// <param name="CodesNonProduitsParCgl">Codes des types de documents non produits par CGL.</param>
/// <param name="EntreesIgnoreesCount">
/// Entrées entièrement écartées — code de type de document absent, code en doublon,
/// ou aucune association exploitable.
/// </param>
/// <param name="AssociationsIgnoreesCount">
/// Associations écartées à l'intérieur d'entrées par ailleurs conservées — identifiant
/// de pièce absent, ou pièce déjà présente dans la même entrée.
/// </param>
/// <param name="DefautsMultiplesCount">
/// Indicateurs <c>IsDefault</c> surnuméraires, rencontrés au-delà du premier d'une entrée.
/// </param>
/// <param name="SansDefautCount">Types de documents indexés sans pièce par défaut.</param>
internal sealed record TranscodificationIndex(
    FrozenDictionary<string, TypeDocumentIndexe> ParTypeDocument,
    ImmutableArray<string> CodesProduitsParCgl,
    ImmutableArray<string> CodesNonProduitsParCgl,
    int EntreesIgnoreesCount,
    int AssociationsIgnoreesCount,
    int DefautsMultiplesCount,
    int SansDefautCount)
{
    /// <summary>Vrai si au moins une anomalie de référentiel a été constatée.</summary>
    public bool HasAnomalies =>
        EntreesIgnoreesCount > 0
        || AssociationsIgnoreesCount > 0
        || DefautsMultiplesCount > 0
        || SansDefautCount > 0;

    /// <summary>Codes des types de documents correspondant à une origine donnée.</summary>
    /// <param name="produitParCgl">L'origine recherchée.</param>
    /// <returns>Les codes, dans l'ordre de la table.</returns>
    public ImmutableArray<string> CodesParOrigine(bool produitParCgl) =>
        produitParCgl ? CodesProduitsParCgl : CodesNonProduitsParCgl;
}

/// <summary>
/// Transforme les entrées brutes de la table en index exploitable.
/// </summary>
/// <remarks>
/// Fonction pure, sans dépendance d'infrastructure ni journalisation : les règles
/// d'interprétation des données se testent isolément, et le cache reste cantonné au
/// cycle de vie du contenu.
/// </remarks>
internal static class TranscodificationIndexBuilder
{
    /// <summary>
    /// Comparateur unique de tous les codes métier, à l'indexation comme à la recherche.
    /// Toute comparaison faite ailleurs avec un autre comparateur serait incohérente.
    /// </summary>
    public static readonly StringComparer CodeComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Nettoie et fige les entrées de la table en un index prêt pour la lecture.
    /// </summary>
    /// <remarks>
    /// <para>Les données arrivent déjà groupées par type de document : il n'y a plus de
    /// regroupement à faire, seulement une validation à deux niveaux — l'entrée, puis
    /// chacune de ses associations.</para>
    /// <para>Les anomalies sont absorbées et comptées plutôt que de rendre tout le
    /// référentiel inutilisable. Une entrée est écartée si son code est absent, s'il
    /// double une entrée déjà vue, ou s'il ne reste aucune pièce exploitable ; à
    /// l'intérieur d'une entrée conservée, une association sans identifiant ou déjà
    /// présente est ignorée. Si plusieurs associations portent <c>IsDefault</c>, la
    /// première dans l'ordre fourni l'emporte. Le chargement n'est rejeté que s'il ne
    /// reste aucune entrée exploitable.</para>
    /// </remarks>
    /// <param name="entries">Les entrées brutes retournées par le repository.</param>
    /// <returns>L'index construit, ses vues précalculées et le décompte des anomalies.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> est nul.</exception>
    /// <exception cref="TranscodificationDataException">Aucune entrée exploitable.</exception>
    public static TranscodificationIndex Build(IReadOnlyList<TranscodificationTableEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var parTypeDocument = new Dictionary<string, TypeDocumentIndexe>(CodeComparer);
        var codesProduitsParCgl = ImmutableArray.CreateBuilder<string>();
        var codesNonProduitsParCgl = ImmutableArray.CreateBuilder<string>();
        var entreesIgnorees = 0;
        var associationsIgnorees = 0;
        var defautsMultiples = 0;
        var sansDefaut = 0;

        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.CodeTypeDocument))
            {
                entreesIgnorees++;
                continue;
            }

            // Trim défensif : les colonnes CHAR sont complétées par des espaces.
            var codeTypeDocument = entry.CodeTypeDocument.Trim();

            // L'émetteur est censé fournir une entrée par type de document. Un code en
            // double signale une anomalie : on conserve la première entrée plutôt que de
            // fusionner deux versions dont rien ne dit laquelle fait foi.
            if (parTypeDocument.ContainsKey(codeTypeDocument))
            {
                entreesIgnorees++;
                continue;
            }

            var typeDocument = SelectTypeDocument(entry, ref associationsIgnorees, ref defautsMultiples);

            // Aucune pièce exploitable : indexer ce type le rendrait « connu mais vide »,
            // ce que l'API ne distingue pas d'un type inconnu. On l'écarte plutôt.
            if (typeDocument is null)
            {
                entreesIgnorees++;
                continue;
            }

            if (typeDocument.IdPieceParDefaut is null)
            {
                sansDefaut++;
            }

            parTypeDocument.Add(codeTypeDocument, typeDocument);

            // Vues par origine, remplies au fil du parcours : le filtrage en lecture est
            // alors une simple restitution, sans parcours de l'index.
            if (typeDocument.ProduitParCgl)
            {
                codesProduitsParCgl.Add(codeTypeDocument);
            }
            else
            {
                codesNonProduitsParCgl.Add(codeTypeDocument);
            }
        }

        if (parTypeDocument.Count == 0)
        {
            throw new TranscodificationDataException(
                "La table de transcodification ne contient aucune entrée exploitable.");
        }

        // Gel : l'index devient une table de hachage optimisée pour la lecture seule.
        return new TranscodificationIndex(
            parTypeDocument.ToFrozenDictionary(CodeComparer),
            codesProduitsParCgl.ToImmutable(),
            codesNonProduitsParCgl.ToImmutable(),
            entreesIgnorees,
            associationsIgnorees,
            defautsMultiples,
            sansDefaut);
    }

    /// <summary>
    /// Retient les associations exploitables d'une entrée et désigne sa pièce par défaut.
    /// </summary>
    /// <param name="entry">L'entrée à dépouiller.</param>
    /// <param name="associationsIgnorees">Compteur d'associations écartées, incrémenté sur place.</param>
    /// <param name="defautsMultiples">Compteur d'indicateurs surnuméraires, incrémenté sur place.</param>
    /// <returns>
    /// Le type de document indexé, ou <c>null</c> si l'entrée ne comporte aucune pièce exploitable.
    /// </returns>
    private static TypeDocumentIndexe? SelectTypeDocument(
        TranscodificationTableEntry entry,
        ref int associationsIgnorees,
        ref int defautsMultiples)
    {
        var idsPiece = new List<string>(entry.Associations.Length);
        string? idPieceParDefaut = null;

        foreach (var association in entry.Associations)
        {
            // Association est un struct : un tableau dimensionné mais non rempli contient
            // des valeurs par défaut, dont l'identifiant est nul.
            if (string.IsNullOrWhiteSpace(association.IdPiece))
            {
                associationsIgnorees++;
                continue;
            }

            var idPiece = association.IdPiece.Trim();

            // Parcours linéaire volontaire : quelques pièces par type de document, sur une
            // liste contiguë. Un HashSet coûterait le hachage et une allocation par entrée
            // pour un gain nul à cette échelle.
            if (idsPiece.Contains(idPiece, CodeComparer))
            {
                associationsIgnorees++;
                continue;
            }

            idsPiece.Add(idPiece);

            if (!association.IsDefault)
            {
                continue;
            }

            if (idPieceParDefaut is null)
            {
                idPieceParDefaut = idPiece;
            }
            else
            {
                defautsMultiples++;
            }
        }

        return idsPiece.Count == 0
            ? null
            : new TypeDocumentIndexe(
                ImmutableArray.CreateRange(idsPiece), idPieceParDefaut, entry.ProduitParCgl);
    }
}
