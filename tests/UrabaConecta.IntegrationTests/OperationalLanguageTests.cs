using UrabaConecta.Web.Client.Shared;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Los dos vocabularios de la operación. Son pruebas puras —no tocan la base— pero viven aquí porque
/// es el proyecto que ya referencia la capa de presentación.
///
/// Lo que fijan no es la ortografía: es que ningún estado quede sin traducir. Un enum inglés en
/// pantalla es un defecto que nadie reporta, porque parece que dice algo a propósito.
/// </summary>
public sealed class OperationalStatusTextTests
{
    [Theory]
    [InlineData("Pending", "Pendiente")]
    [InlineData("Confirmed", "Confirmada")]
    [InlineData("Rejected", "Rechazada")]
    [InlineData("Cancelled", "Cancelada")]
    [InlineData("Completed", "Completada")]
    [InlineData("NoShow", "No asistió")]
    public void Every_appointment_status_has_a_human_name(string status, string esperado)
        => Assert.Equal(esperado, OperationalStatusText.Appointment(status));

    [Theory]
    [InlineData("Pending", "Pendiente")]
    [InlineData("Accepted", "Aceptado")]
    [InlineData("Rejected", "Rechazado")]
    [InlineData("Preparing", "En preparación")]
    [InlineData("ReadyForPickup", "Listo")]
    [InlineData("Delivered", "Entregado")]
    [InlineData("Cancelled", "Cancelado")]
    public void Every_order_status_has_a_human_name(string status, string esperado)
        => Assert.Equal(esperado, OperationalStatusText.Order(status));

    [Theory]
    [InlineData("Waiting", "En espera")]
    [InlineData("Called", "Llamado")]
    [InlineData("InService", "En atención")]
    [InlineData("Completed", "Atendido")]
    [InlineData("Skipped", "Omitido")]
    [InlineData("Cancelled", "Cancelado")]
    public void Every_queue_ticket_status_has_a_human_name(string status, string esperado)
        => Assert.Equal(esperado, OperationalStatusText.QueueTicket(status));

    /// <summary>
    /// Los tres vocabularios cubren su enum entero. Si mañana el dominio gana un estado, esta prueba
    /// falla antes de que salga a pantalla sin traducir.
    /// </summary>
    [Fact]
    public void The_three_vocabularies_cover_their_whole_enum()
    {
        foreach (var status in Enum.GetNames<UrabaConecta.Domain.AppointmentStatus>())
            Assert.NotEqual(OperationalStatusText.Unknown, OperationalStatusText.Appointment(status));
        foreach (var status in Enum.GetNames<UrabaConecta.Domain.PickupOrderStatus>())
            Assert.NotEqual(OperationalStatusText.Unknown, OperationalStatusText.Order(status));
        foreach (var status in Enum.GetNames<UrabaConecta.Domain.QueueTicketStatus>())
            Assert.NotEqual(OperationalStatusText.Unknown, OperationalStatusText.QueueTicket(status));
    }

    /// <summary>Un estado que esta versión no conoce se dice, no se filtra en inglés.</summary>
    [Theory]
    [InlineData("EstadoQueNoExiste")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_status_never_leaks_the_raw_name(string? status)
    {
        foreach (var texto in new[]
                 {
                     OperationalStatusText.Appointment(status),
                     OperationalStatusText.Order(status),
                     OperationalStatusText.QueueTicket(status)
                 })
        {
            Assert.Equal(OperationalStatusText.Unknown, texto);
            if (!string.IsNullOrEmpty(status)) Assert.DoesNotContain(status, texto);
        }
    }
}

/// <summary>
/// Las horas como las lee quien atiende. Todo se mide desde instantes UTC fijos, así que el resultado
/// no depende de la zona de la máquina que corra las pruebas.
/// </summary>
public sealed class BusinessDateTimeTextTests
{
    /// <summary>Las 20:45Z: en Bogotá son las 3:45 de la tarde del mismo día.</summary>
    private static readonly DateTimeOffset Tarde = new(2026, 8, 12, 20, 45, 0, TimeSpan.Zero);

    [Fact]
    public void The_hour_is_read_in_the_business_clock_and_not_in_utc()
    {
        var bogota = BusinessDateTimeText.Time(Tarde, "America/Bogota");
        Assert.StartsWith("3:45", bogota);
        Assert.Contains("p.", bogota);
        Assert.DoesNotContain("20:45", bogota);
        Assert.DoesNotContain("UTC", bogota);
    }

    [Fact]
    public void Morning_and_afternoon_are_told_apart()
    {
        // Las 13:30Z son las 8:30 de la mañana en Bogotá.
        var manana = BusinessDateTimeText.Time(new(2026, 8, 12, 13, 30, 0, TimeSpan.Zero), "America/Bogota");
        Assert.StartsWith("8:30", manana);
        Assert.Contains("a.", manana);
        Assert.Contains("p.", BusinessDateTimeText.Time(Tarde, "America/Bogota"));
    }

    [Fact]
    public void The_same_instant_can_be_a_different_day_in_another_zone()
    {
        // Bogotá va en UTC-5 y Tokio en UTC+9: a las 20:45Z en Tokio ya es el día siguiente. Este es
        // el caso que un desfase escrito a mano nunca acierta.
        Assert.Equal("12 ago 2026", BusinessDateTimeText.Date(Tarde, "America/Bogota"));
        Assert.Equal("13 ago 2026", BusinessDateTimeText.Date(Tarde, "Asia/Tokyo"));
        Assert.NotEqual(BusinessDateTimeText.Time(Tarde, "America/Bogota"),
            BusinessDateTimeText.Time(Tarde, "Asia/Tokyo"));
    }

    [Fact]
    public void Date_and_time_read_as_a_sentence_and_never_as_a_machine_stamp()
    {
        var texto = BusinessDateTimeText.DateAndTime(Tarde, "America/Bogota");
        Assert.StartsWith("12 ago 2026, 3:45", texto);
        Assert.DoesNotContain("2026-08-12", texto);
        Assert.DoesNotContain("T20:45", texto);
        Assert.DoesNotContain("UTC", texto);
        // Sin segundos: la operación no los necesita y sólo alargan la línea.
        Assert.DoesNotContain(":00 ", texto.Replace("3:45", ""));
    }

    [Theory]
    [InlineData("Zona/Inexistente")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unusable_zone_falls_back_instead_of_breaking_the_screen(string? zona)
    {
        // El aviso de zona mal configurada ya lo emite el servidor una vez; repetirlo por fila sólo
        // llenaría el registro. Aquí lo que importa es que la pantalla siga mostrando una hora.
        Assert.Equal(BusinessDateTimeText.Time(Tarde, "America/Bogota"), BusinessDateTimeText.Time(Tarde, zona));
    }
}
