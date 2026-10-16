using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Xapi.Lrs.Storage;

/// <summary>
/// The EF Core context of the LRS tables. Public so that the provider packages can ship migrations for it;
/// the entity types are internal and the schema may change before 1.0.
/// </summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage)]
public sealed class XapiLrsDbContext : DbContext
{
    /// <summary>The name of the migrations history table, kept apart from an application's own migrations.</summary>
    public const string MigrationsHistoryTable = "__xapi_migrations";

    /// <summary>Creates the context.</summary>
    /// <param name="options">The options, normally configured by <c>UseSqlite</c> or <c>UsePostgreSql</c>.</param>
    public XapiLrsDbContext(DbContextOptions<XapiLrsDbContext> options)
        : base(options)
    {
    }

    internal DbSet<StatementEntity> Statements => Set<StatementEntity>();

    internal DbSet<StatementActorEntity> StatementActors => Set<StatementActorEntity>();

    internal DbSet<StatementActivityEntity> StatementActivities => Set<StatementActivityEntity>();

    internal DbSet<StatementRefEntity> StatementRefs => Set<StatementRefEntity>();

    internal DbSet<ActorEntity> Actors => Set<ActorEntity>();

    internal DbSet<ActivityEntity> Activities => Set<ActivityEntity>();

    internal DbSet<CredentialEntity> Credentials => Set<CredentialEntity>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Names are spelled out so both providers get the same snake_case schema without a naming plugin.
        modelBuilder.Entity<StatementEntity>(e =>
        {
            e.ToTable("xapi_statement");
            e.HasKey(s => s.Pk).HasName("pk_xapi_statement");
            e.Property(s => s.Pk).HasColumnName("pk").ValueGeneratedNever();
            e.Property(s => s.StatementId).HasColumnName("statement_id");
            e.Property(s => s.Stored).HasColumnName("stored");
            e.Property(s => s.Timestamp).HasColumnName("timestamp");
            e.Property(s => s.VerbIri).HasColumnName("verb_iri");
            e.Property(s => s.Registration).HasColumnName("registration");
            e.Property(s => s.RefStatementId).HasColumnName("ref_statement_id");
            e.Property(s => s.IsVoided).HasColumnName("is_voided");
            e.Property(s => s.Version).HasColumnName("version");
            e.Property(s => s.AuthorityKey).HasColumnName("authority_key");
            e.Property(s => s.Payload).HasColumnName("payload");
            e.HasIndex(s => s.StatementId).IsUnique().HasDatabaseName("ux_xapi_statement_statement_id");
            e.HasIndex(s => new { s.VerbIri, s.Pk }).HasDatabaseName("ix_xapi_statement_verb_iri_pk");
            e.HasIndex(s => new { s.Registration, s.Pk }).HasDatabaseName("ix_xapi_statement_registration_pk");
            e.HasIndex(s => s.RefStatementId).HasDatabaseName("ix_xapi_statement_ref_statement_id");
        });

        modelBuilder.Entity<StatementActorEntity>(e =>
        {
            e.ToTable("xapi_statement_actor");
            e.HasKey(a => new { a.StatementPk, a.Usage, a.ActorIfi, a.ActorType }).HasName("pk_xapi_statement_actor");
            e.Property(a => a.StatementPk).HasColumnName("statement_pk");
            e.Property(a => a.Usage).HasColumnName("usage");
            e.Property(a => a.ActorIfi).HasColumnName("actor_ifi");
            e.Property(a => a.ActorType).HasColumnName("actor_type");
            e.HasIndex(a => new { a.ActorIfi, a.Usage, a.StatementPk }).HasDatabaseName("ix_xapi_statement_actor_ifi_usage_pk");
            e.HasOne<StatementEntity>().WithMany().HasForeignKey(a => a.StatementPk).OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_xapi_statement_actor_statement");
        });

        modelBuilder.Entity<StatementActivityEntity>(e =>
        {
            e.ToTable("xapi_statement_activity");
            e.HasKey(a => new { a.StatementPk, a.Usage, a.ActivityIri }).HasName("pk_xapi_statement_activity");
            e.Property(a => a.StatementPk).HasColumnName("statement_pk");
            e.Property(a => a.Usage).HasColumnName("usage");
            e.Property(a => a.ActivityIri).HasColumnName("activity_iri");
            e.HasIndex(a => new { a.ActivityIri, a.Usage, a.StatementPk }).HasDatabaseName("ix_xapi_statement_activity_iri_usage_pk");
            e.HasOne<StatementEntity>().WithMany().HasForeignKey(a => a.StatementPk).OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_xapi_statement_activity_statement");
        });

        modelBuilder.Entity<StatementRefEntity>(e =>
        {
            e.ToTable("xapi_statement_ref");
            e.HasKey(r => new { r.AncestorPk, r.DescendantId }).HasName("pk_xapi_statement_ref");
            e.Property(r => r.AncestorPk).HasColumnName("ancestor_pk");
            e.Property(r => r.DescendantId).HasColumnName("descendant_id");
            e.HasIndex(r => r.DescendantId).HasDatabaseName("ix_xapi_statement_ref_descendant_id");
            e.HasOne<StatementEntity>().WithMany().HasForeignKey(r => r.AncestorPk).OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_xapi_statement_ref_statement");
        });

        modelBuilder.Entity<ActorEntity>(e =>
        {
            e.ToTable("xapi_actor");
            e.HasKey(a => new { a.Ifi, a.Type }).HasName("pk_xapi_actor");
            e.Property(a => a.Ifi).HasColumnName("ifi");
            e.Property(a => a.Type).HasColumnName("type");
            e.Property(a => a.Payload).HasColumnName("payload");
        });

        modelBuilder.Entity<ActivityEntity>(e =>
        {
            e.ToTable("xapi_activity");
            e.HasKey(a => a.Iri).HasName("pk_xapi_activity");
            e.Property(a => a.Iri).HasColumnName("iri");
            e.Property(a => a.Payload).HasColumnName("payload");
        });

        modelBuilder.Entity<CredentialEntity>(e =>
        {
            e.ToTable("xapi_credential");
            e.HasKey(c => c.Key).HasName("pk_xapi_credential");
            e.Property(c => c.Key).HasColumnName("key");
            e.Property(c => c.SecretHash).HasColumnName("secret_hash");
            e.Property(c => c.Scopes).HasColumnName("scopes");
            e.Property(c => c.AuthorityPayload).HasColumnName("authority_payload");
        });
    }
}
