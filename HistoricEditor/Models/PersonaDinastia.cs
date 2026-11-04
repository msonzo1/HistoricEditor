namespace HistoricEditor;

// Collegamento per identificativo tra una scheda e una delle sue dinastie (una persona può averne più di una).
public sealed class PersonaDinastia
{
    public int PersonaId { get; set; }

    public HistoricalPerson? Persona { get; set; }

    public int DinastiaId { get; set; }

    public Dinastia? Dinastia { get; set; }
}
