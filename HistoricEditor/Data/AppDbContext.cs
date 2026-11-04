using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HistoricEditor;

public sealed class AppDbContext : DbContext
{
    private static readonly ValueConverter<Dictionary<string, List<string>>, string> QualifiersConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<Dictionary<string, List<string>>>(v, (JsonSerializerOptions?)null) ?? new());

    private static readonly ValueComparer<Dictionary<string, List<string>>> QualifiersComparer = new(
        (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
        v => JsonSerializer.Deserialize<Dictionary<string, List<string>>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null) ?? new());

    private readonly string dbPath;

    public DbSet<HistoricalPerson> People => Set<HistoricalPerson>();
    public DbSet<Luogo> Luoghi => Set<Luogo>();
    public DbSet<LuogoSpecifico> LuoghiSpecifici => Set<LuogoSpecifico>();
    public DbSet<Unione> Unioni => Set<Unione>();
    public DbSet<Dinastia> Dinastie => Set<Dinastia>();
    public DbSet<Evento> Eventi => Set<Evento>();
    public DbSet<Relazione> Relazioni => Set<Relazione>();
    public DbSet<PersonaDinastia> PersoneDinastie => Set<PersonaDinastia>();
    public DbSet<Opera> Opere => Set<Opera>();
    public DbSet<Allegato> Allegati => Set<Allegato>();

    public AppDbContext(string dbPath) => this.dbPath = dbPath;

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={dbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var person = modelBuilder.Entity<HistoricalPerson>();
        person.ToTable("People");
        person.HasKey(p => p.Id);

        person.Ignore(p => p.DisplayName);
        person.Ignore(p => p.LifeSpan);

        // I dati di ricerca annidati sono salvati come colonne JSON sulla riga della persona.
        person.OwnsMany(p => p.BirthDateEvidence, b =>
        {
            b.ToJson();
            b.Property(e => e.Qualifiers).HasConversion(QualifiersConverter, QualifiersComparer);
            b.OwnsMany(e => e.Sources);
        });
        person.OwnsMany(p => p.DeathDateEvidence, b =>
        {
            b.ToJson();
            b.Property(e => e.Qualifiers).HasConversion(QualifiersConverter, QualifiersComparer);
            b.OwnsMany(e => e.Sources);
        });
        person.OwnsMany(p => p.BirthPlaces, ConfigurePlaces);
        person.OwnsMany(p => p.DeathPlaces, ConfigurePlaces);
        person.OwnsMany(p => p.BurialPlaces, ConfigurePlaces);
        person.OwnsMany(p => p.Titles, b =>
        {
            b.ToJson();
            b.OwnsMany(t => t.FontiDate);
        });
        person.OwnsMany(p => p.LocalizedNames, b => b.ToJson());
        person.OwnsMany(p => p.WikipediaLeads, b => b.ToJson());

        person.HasIndex(p => p.Name);
        person.HasIndex(p => p.Family);
        person.HasIndex(p => p.BirthPlace);
        person.HasIndex(p => p.DeathPlace);

        person.HasOne(p => p.BirthLuogo)
            .WithMany()
            .HasForeignKey(p => p.BirthLuogoId)
            .OnDelete(DeleteBehavior.SetNull);

        person.HasOne(p => p.DeathLuogo)
            .WithMany()
            .HasForeignKey(p => p.DeathLuogoId)
            .OnDelete(DeleteBehavior.SetNull);

        person.HasOne(p => p.BurialLuogo)
            .WithMany()
            .HasForeignKey(p => p.BurialLuogoId)
            .OnDelete(DeleteBehavior.SetNull);

        person.HasOne(p => p.BirthLuogoSpecifico)
            .WithMany()
            .HasForeignKey(p => p.BirthLuogoSpecificoId)
            .OnDelete(DeleteBehavior.SetNull);

        person.HasOne(p => p.DeathLuogoSpecifico)
            .WithMany()
            .HasForeignKey(p => p.DeathLuogoSpecificoId)
            .OnDelete(DeleteBehavior.SetNull);

        person.HasOne(p => p.BurialLuogoSpecifico)
            .WithMany()
            .HasForeignKey(p => p.BurialLuogoSpecificoId)
            .OnDelete(DeleteBehavior.SetNull);

        person.HasOne(p => p.Father)
            .WithMany()
            .HasForeignKey(p => p.FatherId)
            .OnDelete(DeleteBehavior.SetNull);

        person.HasOne(p => p.Mother)
            .WithMany()
            .HasForeignKey(p => p.MotherId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Unione>(u =>
        {
            u.ToTable("Unioni");
            u.HasOne(x => x.Persona1).WithMany().HasForeignKey(x => x.Persona1Id).OnDelete(DeleteBehavior.Cascade);
            u.HasOne(x => x.Persona2).WithMany().HasForeignKey(x => x.Persona2Id).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Relazione>(r =>
        {
            r.ToTable("Relazioni");
            r.HasOne(x => x.Persona1).WithMany().HasForeignKey(x => x.Persona1Id).OnDelete(DeleteBehavior.Cascade);
            r.HasOne(x => x.Persona2).WithMany().HasForeignKey(x => x.Persona2Id).OnDelete(DeleteBehavior.Cascade);
            r.HasIndex(x => x.Tipo);
        });

        modelBuilder.Entity<Dinastia>(d =>
        {
            d.ToTable("Dinastie");
            // Eliminando una dinastia i suoi rami diventano dinastie principali.
            d.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.SetNull);
            d.Property(x => x.Nome).UseCollation("NOCASE");
            d.HasIndex(x => x.Nome).IsUnique();
        });

        modelBuilder.Entity<PersonaDinastia>(l =>
        {
            l.ToTable("PersoneDinastie");
            l.HasKey(x => new { x.PersonaId, x.DinastiaId });
            l.HasOne(x => x.Persona).WithMany().HasForeignKey(x => x.PersonaId).OnDelete(DeleteBehavior.Cascade);
            l.HasOne(x => x.Dinastia).WithMany().HasForeignKey(x => x.DinastiaId).OnDelete(DeleteBehavior.Cascade);
            l.HasIndex(x => x.DinastiaId);
        });

        modelBuilder.Entity<Evento>(e =>
        {
            e.ToTable("Eventi");
            e.Ignore(x => x.DataTesto);
            e.Ignore(x => x.Anno);
            e.HasIndex(x => x.Tipo);
            e.HasMany(x => x.Partecipanti).WithOne(x => x.Evento).HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Luogo).WithMany().HasForeignKey(x => x.LuogoId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.LuogoSpecifico).WithMany().HasForeignKey(x => x.LuogoSpecificoId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EventoPartecipante>(p =>
        {
            p.ToTable("EventiPartecipanti");
            p.HasKey(x => new { x.EventoId, x.PersonaId });
            p.HasOne(x => x.Persona).WithMany().HasForeignKey(x => x.PersonaId).OnDelete(DeleteBehavior.Cascade);
            p.HasIndex(x => x.PersonaId);
        });

        modelBuilder.Entity<Opera>(o =>
        {
            o.ToTable("Opere");
            o.HasIndex(x => x.Tipo);
            o.HasIndex(x => x.WikidataId);
            o.HasMany(x => x.Persone).WithOne(x => x.Opera).HasForeignKey(x => x.OperaId).OnDelete(DeleteBehavior.Cascade);
            o.HasOne(x => x.Luogo).WithMany().HasForeignKey(x => x.LuogoId).OnDelete(DeleteBehavior.SetNull);
            o.HasOne(x => x.LuogoSpecifico).WithMany().HasForeignKey(x => x.LuogoSpecificoId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OperaPersona>(p =>
        {
            p.ToTable("OperePersone");
            p.HasKey(x => new { x.OperaId, x.PersonaId, x.Ruolo });
            p.HasOne(x => x.Persona).WithMany().HasForeignKey(x => x.PersonaId).OnDelete(DeleteBehavior.Cascade);
            p.HasIndex(x => x.PersonaId);
        });

        ConfigureGeography(modelBuilder);

        modelBuilder.Entity<Allegato>(a =>
        {
            a.ToTable("Allegati");
            a.HasIndex(x => new { x.Ambito, x.SchedaId });
        });
    }

    private static void ConfigureGeography(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Luogo>(l =>
        {
            l.HasOne(x => x.Parent)
                .WithMany(x => x.Figli)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
            l.HasIndex(x => new { x.ParentId, x.Tipo, x.Nome }).IsUnique();
            l.OwnsMany(x => x.NomiAltreLingue, n => n.ToJson());
            l.OwnsMany(x => x.NomiStorici, n => n.ToJson());
            l.HasIndex(x => x.WikidataId);
        });

        modelBuilder.Entity<LuogoSpecifico>(s =>
        {
            s.ToTable("LuoghiSpecifici");
            s.HasOne(x => x.Luogo)
                .WithMany()
                .HasForeignKey(x => x.LuogoId)
                .OnDelete(DeleteBehavior.Cascade);
            s.HasIndex(x => new { x.LuogoId, x.Nome }).IsUnique();
            s.HasIndex(x => x.WikidataId);
        });
    }

    private static void ConfigurePlaces(OwnedNavigationBuilder<HistoricalPerson, HistoricalPlaceRecord> places)
    {
        places.ToJson();
        places.OwnsMany(r => r.Names);
        places.OwnsMany(r => r.AdministrativeDivisions, d => d.OwnsMany(x => x.Names));
        places.OwnsMany(r => r.HistoricalContexts, c => c.OwnsMany(x => x.Sources));
        places.OwnsMany(r => r.Sources);
        places.Ignore(r => r.HistoricalNames);
    }
}

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
        => new(Path.Combine(Path.GetTempPath(), "historic-design.db"));
}
