using Xunit;

namespace Company.Transcodification.Tests;

/// <summary>
/// Tests de l'interprétation des entrées de la table, isolée de toute infrastructure :
/// pas de conteneur, pas d'horloge, pas de repository. Requiert
/// <c>[assembly: InternalsVisibleTo("Company.Transcodification.Tests")]</c> côté production.
/// </summary>
public sealed class TranscodificationIndexBuilderTests
{
    [Fact]
    public void Reprend_les_pieces_de_chaque_entree_en_preservant_lordre()
    {
        var index = TranscodificationIndexBuilder.Build(
        [
            Entree("FAC", new Association("FACTURE", isDefault: true), new Association("BON_COMMANDE")),
            Entree("AVO", new Association("AVOIR", isDefault: true))
        ]);

        Assert.Equal(2, index.ParTypeDocument.Count);
        Assert.Equal(["FACTURE", "BON_COMMANDE"], index.ParTypeDocument["FAC"].IdsPiece);
        Assert.Equal("FACTURE", index.ParTypeDocument["FAC"].IdPieceParDefaut);
        Assert.False(index.HasAnomalies);
    }

    [Fact]
    public void Indexe_sans_tenir_compte_de_la_casse_ni_des_espaces()
    {
        var index = TranscodificationIndexBuilder.Build(
            [Entree("  fac  ", new Association("  facture  ", isDefault: true))]);

        Assert.Equal("facture", index.ParTypeDocument["FAC"].IdPieceParDefaut);
    }

    [Fact]
    public void Entree_sans_code_de_type_de_document_est_ecartee()
    {
        var index = TranscodificationIndexBuilder.Build(
        [
            Entree("FAC", new Association("FACTURE", isDefault: true)),
            Entree("   ", new Association("ORPHELINE"))
        ]);

        Assert.Single(index.ParTypeDocument);
        Assert.Equal(1, index.EntreesIgnoreesCount);
    }

    [Fact]
    public void Code_de_type_de_document_en_doublon_conserve_la_premiere_entree()
    {
        var index = TranscodificationIndexBuilder.Build(
        [
            Entree("FAC", new Association("FACTURE", isDefault: true)),
            Entree("fac", new Association("AUTRE", isDefault: true))
        ]);

        Assert.Equal(["FACTURE"], index.ParTypeDocument["FAC"].IdsPiece);
        Assert.Equal(1, index.EntreesIgnoreesCount);
    }

    [Fact]
    public void Entree_sans_aucune_piece_exploitable_est_ecartee()
    {
        // Le tableau dimensionné mais non rempli produit des Association par défaut,
        // dont l'identifiant est nul : c'est le piège du struct.
        var index = TranscodificationIndexBuilder.Build(
        [
            Entree("FAC", new Association("FACTURE", isDefault: true)),
            Entree("VIDE"),
            Entree("DEFAUT_STRUCT", new Association[2])
        ]);

        Assert.Single(index.ParTypeDocument);
        Assert.Equal(2, index.EntreesIgnoreesCount);
        Assert.Equal(2, index.AssociationsIgnoreesCount);
    }

    [Fact]
    public void Associations_sans_identifiant_ou_en_doublon_sont_ignorees()
    {
        var index = TranscodificationIndexBuilder.Build(
        [
            Entree(
                "FAC",
                new Association("FACTURE", isDefault: true),
                new Association("facture"),          // doublon, à la casse près
                new Association("   "),              // identifiant vide
                new Association("AVOIR"))
        ]);

        Assert.Equal(["FACTURE", "AVOIR"], index.ParTypeDocument["FAC"].IdsPiece);
        Assert.Equal(2, index.AssociationsIgnoreesCount);
        Assert.Equal(0, index.EntreesIgnoreesCount);
    }

    [Fact]
    public void Retient_le_premier_indicateur_par_defaut_et_compte_les_surnumeraires()
    {
        var index = TranscodificationIndexBuilder.Build(
        [
            Entree(
                "FAC",
                new Association("FACTURE", isDefault: true),
                new Association("BON_COMMANDE", isDefault: true),
                new Association("BON_LIVRAISON", isDefault: true))
        ]);

        Assert.Equal("FACTURE", index.ParTypeDocument["FAC"].IdPieceParDefaut);
        Assert.Equal(2, index.DefautsMultiplesCount);
    }

    [Fact]
    public void Compte_les_types_de_document_sans_piece_par_defaut()
    {
        var index = TranscodificationIndexBuilder.Build(
        [
            Entree("FAC", new Association("FACTURE", isDefault: true)),
            Entree("CTR", new Association("CONTRAT")),
            Entree("BON", new Association("BON_LIVRAISON"))
        ]);

        Assert.Null(index.ParTypeDocument["CTR"].IdPieceParDefaut);
        Assert.Equal(2, index.SansDefautCount);
    }

    [Fact]
    public void Repartit_les_codes_par_origine_en_preservant_lordre()
    {
        var index = TranscodificationIndexBuilder.Build(
        [
            Entree("FAC", produitParCgl: true, new Association("FACTURE", isDefault: true)),
            Entree("CTR", produitParCgl: false, new Association("CONTRAT", isDefault: true)),
            Entree("AVO", produitParCgl: true, new Association("AVOIR", isDefault: true))
        ]);

        Assert.True(index.ParTypeDocument["FAC"].ProduitParCgl);
        Assert.Equal(["FAC", "AVO"], index.CodesParOrigine(produitParCgl: true));
        Assert.Equal(["CTR"], index.CodesParOrigine(produitParCgl: false));
    }

    [Fact]
    public void Origine_sans_aucun_document_donne_une_vue_vide()
    {
        var index = TranscodificationIndexBuilder.Build(
            [Entree("FAC", new Association("FACTURE", isDefault: true))]);

        var produits = index.CodesParOrigine(produitParCgl: true);

        Assert.True(produits.IsEmpty);
        Assert.False(produits.IsDefault);
    }

    [Fact]
    public void Aucune_entree_exploitable_est_rejetee()
    {
        Assert.Throws<TranscodificationDataException>(
            () => TranscodificationIndexBuilder.Build([]));

        Assert.Throws<TranscodificationDataException>(
            () => TranscodificationIndexBuilder.Build([Entree("FAC")]));
    }

    /// <summary>Raccourci de lecture : une entrée dont seuls le code et les pièces importent ici.</summary>
    /// <param name="codeTypeDocument">Code du type de document.</param>
    /// <param name="associations">Les associations de l'entrée.</param>
    /// <returns>L'entrée correspondante, non produite par CGL.</returns>
    private static TranscodificationTableEntry Entree(
        string codeTypeDocument,
        params Association[] associations)
        => new(codeTypeDocument, acronyme: null, libelle: null, description: null, associations);

    /// <summary>Variante précisant l'origine du document.</summary>
    /// <param name="codeTypeDocument">Code du type de document.</param>
    /// <param name="produitParCgl">Origine du document.</param>
    /// <param name="associations">Les associations de l'entrée.</param>
    /// <returns>L'entrée correspondante.</returns>
    private static TranscodificationTableEntry Entree(
        string codeTypeDocument,
        bool produitParCgl,
        params Association[] associations)
        => new(codeTypeDocument, null, null, null, associations, produitParCgl);
}
