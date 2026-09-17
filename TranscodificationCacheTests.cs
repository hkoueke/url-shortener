using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Company.Transcodification.Tests;

/// <summary>
/// Paquets : xunit, Microsoft.Extensions.Time.Testing, Microsoft.Extensions.DependencyInjection.
/// Pas de framework de mock : un double simple rend les assertions sur le nombre
/// d'appels au repository explicites.
/// </summary>
public sealed class TranscodificationCacheTests
{
    /// <summary>Instant de référence de tous les tests ; l'horloge est ensuite avancée à la main.</summary>
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Table de référence des tests. Volontairement N:N : FACTURE est partagée par deux
    /// types de documents, et chacun a exactement une pièce par défaut.
    /// </summary>
    private static readonly TranscodificationTableEntry[] TableDeReference =
    [
        Entree(
            "FAC",
            new Association("FACTURE", isDefault: true),
            new Association("BON_COMMANDE"),
            new Association("BON_LIVRAISON")),
        Entree(
            "AVO",
            new Association("FACTURE"),
            new Association("AVOIR", isDefault: true))
    ];

    // ------------------------------------------------------------- Pièce par défaut

    [Fact]
    public async Task Retourne_la_piece_par_defaut_du_type_de_document()
    {
        using var sut = CreateSut(out var repository, out _);

        Assert.Equal("FACTURE", await sut.GetIdPieceParDefautAsync("FAC"));
        Assert.Equal("AVOIR", await sut.GetIdPieceParDefautAsync("AVO"));
        Assert.Equal(1, repository.CallCount);
    }

    [Fact]
    public async Task Type_de_document_inconnu_na_pas_de_piece_par_defaut()
    {
        using var sut = CreateSut(out _, out _);

        Assert.Null(await sut.GetIdPieceParDefautAsync("INCONNU"));
    }

    [Fact]
    public async Task Type_de_document_sans_indicateur_par_defaut_est_signale()
    {
        using var sut = CreateSut(out var repository, out _);
        repository.Rows =
        [
            Entree("FAC", new Association("FACTURE", isDefault: true)),
            Entree("CTR", new Association("CONTRAT"))    // anomalie de référentiel
        ];

        Assert.Null(await sut.GetIdPieceParDefautAsync("CTR"));
        Assert.Equal(["CONTRAT"], await sut.GetIdsPieceAsync("CTR"));   // le type existe bien
        Assert.Equal(1, sut.GetStatus().TypeDocumentSansDefautCount);
    }

    [Fact]
    public async Task Indicateurs_par_defaut_multiples_retiennent_le_premier()
    {
        using var sut = CreateSut(out var repository, out _);
        repository.Rows =
        [
            Entree(
                "FAC",
                new Association("FACTURE", isDefault: true),
                new Association("BON_COMMANDE", isDefault: true))   // anomalie de référentiel
        ];

        Assert.Equal("FACTURE", await sut.GetIdPieceParDefautAsync("FAC"));
        Assert.Equal(0, sut.GetStatus().TypeDocumentSansDefautCount);
    }

    // ------------------------------------------------------------------ Liste N:N

    [Fact]
    public async Task Retourne_tous_les_types_de_piece_dans_lordre_du_repository()
    {
        using var sut = CreateSut(out _, out _);

        Assert.Equal(["FACTURE", "BON_COMMANDE", "BON_LIVRAISON"], await sut.GetIdsPieceAsync("FAC"));
    }

    [Fact]
    public async Task Un_type_de_piece_peut_appartenir_a_plusieurs_types_de_document()
    {
        using var sut = CreateSut(out _, out _);

        Assert.Contains("FACTURE", await sut.GetIdsPieceAsync("FAC"));
        Assert.Contains("FACTURE", await sut.GetIdsPieceAsync("AVO"));
    }

    [Fact]
    public async Task Type_de_document_inconnu_retourne_une_collection_vide()
    {
        using var sut = CreateSut(out _, out _);

        var result = await sut.GetIdsPieceAsync("INCONNU");

        Assert.True(result.IsEmpty);
        Assert.False(result.IsDefault);    // jamais un ImmutableArray non initialisé
    }

    [Fact]
    public async Task Comparaison_insensible_a_la_casse_et_aux_espaces()
    {
        using var sut = CreateSut(out _, out _);

        Assert.Equal("FACTURE", await sut.GetIdPieceParDefautAsync("  fac  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Code_invalide_leve_une_exception_sans_charger(string? code)
    {
        using var sut = CreateSut(out var repository, out _);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => sut.GetIdPieceParDefautAsync(code!).AsTask());
        Assert.Equal(0, repository.CallCount);
    }

    // ------------------------------------------------------------------- Origine

    [Fact]
    public async Task Restitue_lorigine_du_type_de_document()
    {
        using var sut = CreateSut(out var repository, out _);
        repository.Rows =
        [
            Entree("FAC", produitParCgl: true, new Association("FACTURE", isDefault: true)),
            Entree("CTR", produitParCgl: false, new Association("CONTRAT", isDefault: true))
        ];

        Assert.True(await sut.IsProduitParCglAsync("FAC"));
        Assert.False(await sut.IsProduitParCglAsync("CTR"));
        Assert.Null(await sut.IsProduitParCglAsync("INCONNU"));
    }

    [Fact]
    public async Task Filtre_les_types_de_document_par_origine()
    {
        using var sut = CreateSut(out var repository, out _);
        repository.Rows =
        [
            Entree("FAC", produitParCgl: true, new Association("FACTURE", isDefault: true)),
            Entree("CTR", produitParCgl: false, new Association("CONTRAT", isDefault: true)),
            Entree("AVO", produitParCgl: true, new Association("AVOIR", isDefault: true))
        ];

        Assert.Equal(["FAC", "AVO"], await sut.GetCodesTypeDocumentAsync(produitParCgl: true));
        Assert.Equal(["CTR"], await sut.GetCodesTypeDocumentAsync(produitParCgl: false));
        Assert.Equal(1, repository.CallCount);
    }

    // -------------------------------------------------------------------- TTL

    [Fact]
    public async Task Appels_dans_le_ttl_ne_rechargent_pas()
    {
        using var sut = CreateSut(out var repository, out var clock);

        await sut.GetIdPieceParDefautAsync("FAC");
        clock.Advance(TimeSpan.FromMinutes(29));
        await sut.GetIdPieceParDefautAsync("AVO");

        Assert.Equal(1, repository.CallCount);
    }

    [Fact]
    public async Task Expiration_du_ttl_declenche_un_rechargement()
    {
        using var sut = CreateSut(out var repository, out var clock);

        await sut.GetIdPieceParDefautAsync("FAC");
        clock.Advance(TimeSpan.FromMinutes(30));
        repository.Rows = [Entree("FAC", new Association("FACTURE_V2", isDefault: true))];

        Assert.Equal("FACTURE_V2", await sut.GetIdPieceParDefautAsync("FAC"));
        Assert.Equal(2, repository.CallCount);
    }

    // ------------------------------------------------------------- Résilience

    [Fact]
    public async Task Echec_de_rechargement_sert_le_contenu_precedent()
    {
        using var sut = CreateSut(out var repository, out var clock);

        await sut.GetIdPieceParDefautAsync("FAC");
        clock.Advance(TimeSpan.FromMinutes(31));
        repository.Failure = new InvalidOperationException("base injoignable");

        Assert.Equal("FACTURE", await sut.GetIdPieceParDefautAsync("FAC"));

        var status = sut.GetStatus();
        Assert.True(status.IsLoaded);
        Assert.Equal(2, status.TypeDocumentCount);
        Assert.Equal(T0, status.LoadedAt);
        Assert.Equal(nameof(InvalidOperationException), status.LastFailureKind);
    }

    [Fact]
    public async Task Echec_de_rechargement_respecte_le_backoff()
    {
        using var sut = CreateSut(out var repository, out var clock);

        await sut.GetIdPieceParDefautAsync("FAC");
        clock.Advance(TimeSpan.FromMinutes(31));
        repository.Failure = new InvalidOperationException("base injoignable");

        await sut.GetIdPieceParDefautAsync("FAC");    // tentative -> échec, backoff armé
        await sut.GetIdPieceParDefautAsync("FAC");    // servi depuis le contenu périmé
        clock.Advance(TimeSpan.FromSeconds(30));
        await sut.GetIdPieceParDefautAsync("FAC");

        Assert.Equal(2, repository.CallCount);          // 1 chargement initial + 1 tentative

        clock.Advance(TimeSpan.FromSeconds(31));        // backoff écoulé
        repository.Failure = null;
        Assert.Equal("FACTURE", await sut.GetIdPieceParDefautAsync("FAC"));
        Assert.Equal(3, repository.CallCount);
    }

    [Fact]
    public async Task Echec_du_chargement_initial_remonte_et_arme_le_backoff()
    {
        using var sut = CreateSut(out var repository, out var clock);
        repository.Failure = new InvalidOperationException("base injoignable");

        await Assert.ThrowsAsync<TranscodificationUnavailableException>(
            () => sut.GetIdPieceParDefautAsync("FAC").AsTask());
        await Assert.ThrowsAsync<TranscodificationUnavailableException>(
            () => sut.GetIdPieceParDefautAsync("FAC").AsTask());

        Assert.Equal(1, repository.CallCount);          // la base n'est pas martelée
        Assert.False(sut.GetStatus().IsLoaded);

        clock.Advance(TimeSpan.FromMinutes(1));
        repository.Failure = null;
        Assert.Equal("FACTURE", await sut.GetIdPieceParDefautAsync("FAC"));
    }

    // ---------------------------------------------------------- Intégrité données

    [Fact]
    public async Task Lignes_incompletes_ou_en_doublon_sont_ignorees()
    {
        using var sut = CreateSut(out var repository, out _);
        repository.Rows =
        [
            Entree(
                "FAC",
                new Association("FACTURE", isDefault: true),
                new Association("facture"),             // doublon, à la casse près
                new Association("   "),                 // identifiant vide
                new Association("AVOIR"))
        ];

        Assert.Equal(["FACTURE", "AVOIR"], await sut.GetIdsPieceAsync("FAC"));
        Assert.Equal("FACTURE", await sut.GetIdPieceParDefautAsync("FAC"));
    }

    [Fact]
    public async Task Table_vide_est_rejetee_et_preserve_le_contenu()
    {
        using var sut = CreateSut(out var repository, out var clock);

        await sut.GetIdPieceParDefautAsync("FAC");
        clock.Advance(TimeSpan.FromMinutes(31));
        repository.Rows = [];

        Assert.Equal("FACTURE", await sut.GetIdPieceParDefautAsync("FAC"));
        Assert.Equal(nameof(TranscodificationDataException), sut.GetStatus().LastFailureKind);
    }

    [Fact]
    public async Task Table_vide_au_premier_chargement_remonte_une_exception()
    {
        using var sut = CreateSut(out var repository, out _);
        repository.Rows = [];

        var ex = await Assert.ThrowsAsync<TranscodificationUnavailableException>(
            () => sut.GetIdPieceParDefautAsync("FAC").AsTask());
        Assert.IsType<TranscodificationDataException>(ex.InnerException);
    }

    // ------------------------------------------------------------- Invalidation

    [Fact]
    public async Task Invalidate_force_le_rechargement_au_prochain_appel()
    {
        using var sut = CreateSut(out var repository, out _);

        await sut.GetIdPieceParDefautAsync("FAC");
        repository.Rows = [Entree("FAC", new Association("FACTURE_V2", isDefault: true))];
        sut.Invalidate();

        Assert.Equal("FACTURE_V2", await sut.GetIdPieceParDefautAsync("FAC"));
        Assert.Equal(2, repository.CallCount);
    }

    [Fact]
    public async Task Invalidation_pendant_un_chargement_nest_pas_perdue()
    {
        using var sut = CreateSut(out var repository, out _);

        await sut.GetIdPieceParDefautAsync("FAC");     // chargement initial
        sut.Invalidate();

        var release = new TaskCompletionSource();
        repository.PauseUntil = release.Task;
        var enCours = Task.Run(() => sut.GetIdPieceParDefautAsync("FAC").AsTask());
        await Task.Delay(50);                            // le rechargement est en vol

        sut.Invalidate();                                // survient pendant la lecture
        release.SetResult();
        await enCours;

        Assert.Equal(2, repository.CallCount);

        // Le contenu a été publié déjà périmé : le prochain appel relit la base.
        await sut.GetIdPieceParDefautAsync("FAC");
        Assert.Equal(3, repository.CallCount);
    }

    // -------------------------------------------------------------- Concurrence

    [Fact]
    public async Task Appels_concurrents_ne_declenchent_quun_seul_chargement()
    {
        using var sut = CreateSut(out var repository, out _);
        var release = new TaskCompletionSource();
        repository.PauseUntil = release.Task;

        var calls = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => sut.GetIdPieceParDefautAsync("FAC").AsTask()))
            .ToArray();

        await Task.Delay(50);
        release.SetResult();
        var results = await Task.WhenAll(calls);

        Assert.All(results, r => Assert.Equal("FACTURE", r));
        Assert.Equal(1, repository.CallCount);
    }

    // ------------------------------------------------------------------ Fixture

    /// <summary>Raccourci de lecture : une entrée dont seuls le code et les pièces importent ici.</summary>
    /// <param name="codeTypeDocument">Code du type de document.</param>
    /// <param name="associations">Les associations de l'entrée.</param>
    /// <returns>L'entrée correspondante.</returns>
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

    /// <summary>
    /// Construit le cache sur un repository double et une horloge contrôlée.
    /// Le conteneur n'est là que pour fournir un <see cref="IServiceScopeFactory"/> réel :
    /// c'est ainsi que le cache résout le repository.
    /// </summary>
    /// <param name="repository">Le double, pour piloter données et pannes.</param>
    /// <param name="clock">L'horloge, pour franchir TTL et délai de reprise sans attendre.</param>
    /// <returns>Le cache sous test, à libérer par l'appelant.</returns>
    private static TranscodificationCache CreateSut(
        out StubRepository repository,
        out FakeTimeProvider clock)
    {
        var stub = new StubRepository();
        repository = stub;
        clock = new FakeTimeProvider(T0);

        var services = new ServiceCollection();
        services.AddScoped<ITranscodificationRepository>(_ => stub);

        return new TranscodificationCache(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TranscodificationOptions()),
            NullLogger<TranscodificationCache>.Instance,
            clock);
    }

    /// <summary>
    /// Repository double. Un framework de mock rendrait les assertions sur le nombre
    /// d'appels moins lisibles, et ne saurait pas bloquer proprement le test de concurrence.
    /// </summary>
    private sealed class StubRepository : ITranscodificationRepository
    {
        private int _callCount;

        /// <summary>Lignes retournées au prochain appel. Modifiable en cours de test.</summary>
        public IReadOnlyList<TranscodificationTableEntry> Rows { get; set; } = TableDeReference;

        /// <summary>Si renseignée, levée à chaque appel pour simuler une panne d'accès.</summary>
        public Exception? Failure { get; set; }

        /// <summary>
        /// Si renseignée, l'appel reste suspendu jusqu'à son achèvement. Permet de tenir
        /// un chargement « en vol » le temps d'observer le comportement des concurrents.
        /// </summary>
        public Task? PauseUntil { get; set; }

        /// <summary>Nombre d'accès effectifs à la « base ».</summary>
        public int CallCount => Volatile.Read(ref _callCount);

        /// <inheritdoc />
        public async Task<IReadOnlyList<TranscodificationTableEntry>> GetAllAsync(
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);

            if (PauseUntil is not null)
            {
                await PauseUntil.ConfigureAwait(false);
            }

            return Failure is null ? Rows : throw Failure;
        }
    }
}
