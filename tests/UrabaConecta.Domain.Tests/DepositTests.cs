using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

/// <summary>
/// Adelantos manuales. El ejemplo de referencia es un servicio de $80.000 con adelanto del 50 %,
/// es decir $40.000, y el mismo servicio con un valor fijo de $30.000.
/// </summary>
public sealed class DepositTests
{
    private const decimal Precio = 80000m;
    private static readonly DateTimeOffset Ahora = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private static DepositPolicy Porcentaje(decimal valor = 50m)
        => DepositPolicy.Create(true, DepositType.Percentage, valor, "Transfiera a la cuenta de ahorros 000.",
            "573001234567", Precio);
    private static DepositPolicy Fijo(decimal valor = 30000m)
        => DepositPolicy.Create(true, DepositType.FixedAmount, valor, "Transfiera a la cuenta de ahorros 000.",
            "573001234567", Precio);

    private static Appointment Cita(DepositPolicy? deposito, decimal precio = Precio) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Ahora.AddDays(3), 60,
        "Corte femenino", precio, "alias", "telefono", "1234", "notas", "hash", 1, Guid.NewGuid(), Ahora,
        deposito);

    [Fact]
    public void A_service_without_deposit_keeps_the_empty_policy()
    {
        var servicio = new Service(Guid.NewGuid(), Guid.NewGuid(), "Cepillado", 45, 25000);
        Assert.False(servicio.RequiresDeposit);
        Assert.Equal(DepositType.None, servicio.DepositType);
        Assert.Equal(0m, servicio.DepositValue);
        Assert.Equal(0m, servicio.Deposit.CalculateFor(25000m));
    }

    [Fact]
    public void A_fixed_deposit_is_accepted_and_stored()
    {
        var servicio = new Service(Guid.NewGuid(), Guid.NewGuid(), "Corte", 60, Precio, deposit: Fijo());
        Assert.True(servicio.RequiresDeposit);
        Assert.Equal(DepositType.FixedAmount, servicio.DepositType);
        Assert.Equal(30000m, servicio.DepositValue);
        Assert.Equal("573001234567", servicio.DepositWhatsAppNumber);
    }

    [Fact]
    public void A_percentage_deposit_is_accepted_and_stored()
    {
        var servicio = new Service(Guid.NewGuid(), Guid.NewGuid(), "Corte", 60, Precio, deposit: Porcentaje());
        Assert.True(servicio.RequiresDeposit);
        Assert.Equal(DepositType.Percentage, servicio.DepositType);
        Assert.Equal(50m, servicio.DepositValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(101)]
    [InlineData(150)]
    public void An_invalid_percentage_is_rejected(decimal porcentaje)
        => Assert.Throws<DomainException>(() => Porcentaje(porcentaje));

    [Fact]
    public void A_fixed_amount_above_the_price_is_rejected()
    {
        var error = Assert.Throws<DomainException>(() => Fijo(Precio + 1));
        Assert.Equal("DEPOSIT_ABOVE_PRICE", error.Code);
    }

    [Fact]
    public void A_fixed_amount_equal_to_the_price_is_allowed()
        => Assert.Equal(Precio, Fijo(Precio).CalculateFor(Precio));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("3001234567")]        // sin código de país
    [InlineData("0573001234567")]     // no puede empezar por cero
    [InlineData("5730012345678901")]  // demasiado largo
    public void WhatsApp_is_required_and_validated(string? numero)
        => Assert.Throws<DomainException>(() => DepositPolicy.Create(true, DepositType.Percentage, 50m,
            "Instrucciones.", numero, Precio));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Instructions_are_required(string? instrucciones)
    {
        var error = Assert.Throws<DomainException>(() => DepositPolicy.Create(true, DepositType.Percentage, 50m,
            instrucciones, "573001234567", Precio));
        Assert.Equal("DEPOSIT_INSTRUCTIONS_REQUIRED", error.Code);
    }

    [Fact]
    public void A_deposit_without_a_type_is_rejected()
    {
        var error = Assert.Throws<DomainException>(() => DepositPolicy.Create(true, DepositType.None, 50m,
            "Instrucciones.", "573001234567", Precio));
        Assert.Equal("DEPOSIT_TYPE_REQUIRED", error.Code);
    }

    [Fact]
    public void The_fixed_calculation_is_the_configured_amount()
        => Assert.Equal(30000m, Fijo().CalculateFor(Precio));

    [Fact]
    public void The_percentage_calculation_is_half_of_the_price()
        => Assert.Equal(40000m, Porcentaje().CalculateFor(Precio));

    [Theory]
    [InlineData(33, 35000, 11550)]   // 11.550,00 exacto
    [InlineData(33, 35001, 11550)]   // 11.550,33 baja a 11.550
    [InlineData(50, 35001, 17501)]   // 17.500,50 sube a 17.501
    public void The_amount_is_rounded_to_whole_pesos(decimal porcentaje, decimal precio, decimal esperado)
    {
        var politica = DepositPolicy.Create(true, DepositType.Percentage, porcentaje, "Instrucciones.",
            "573001234567", precio);
        Assert.Equal(esperado, politica.CalculateFor(precio));
        Assert.Equal(decimal.Truncate(politica.CalculateFor(precio)), politica.CalculateFor(precio));
    }

    [Fact]
    public void The_appointment_freezes_the_whole_policy()
    {
        var cita = Cita(Porcentaje());
        Assert.Equal(DepositType.Percentage, cita.DepositType);
        Assert.Equal(50m, cita.DepositConfiguredValue);
        Assert.Equal(40000m, cita.DepositAmount);
        Assert.Equal("Transfiera a la cuenta de ahorros 000.", cita.DepositInstructions);
        Assert.Equal("573001234567", cita.DepositWhatsAppNumber);
        Assert.Equal(Precio, cita.DisplayPrice);
    }

    [Fact]
    public void Editing_the_service_afterwards_does_not_touch_an_existing_appointment()
    {
        var servicio = new Service(Guid.NewGuid(), Guid.NewGuid(), "Corte", 60, Precio, deposit: Porcentaje());
        var cita = Cita(servicio.Deposit);
        servicio.Update("Corte", 60, 120000m, true, null, 0, servicio.Version,
            DepositPolicy.Create(true, DepositType.FixedAmount, 90000m, "Otra cuenta.", "573009999999", 120000m));
        Assert.Equal(90000m, servicio.DepositValue);
        // La cita conserva lo pactado: 50 % de $80.000.
        Assert.Equal(40000m, cita.DepositAmount);
        Assert.Equal(DepositType.Percentage, cita.DepositType);
        Assert.Equal("573001234567", cita.DepositWhatsAppNumber);
    }

    [Fact]
    public void Editing_a_service_without_mentioning_the_deposit_keeps_it()
    {
        var servicio = new Service(Guid.NewGuid(), Guid.NewGuid(), "Corte", 60, Precio, deposit: Porcentaje());
        servicio.Update("Corte renombrado", 60, Precio, false, null, 0, servicio.Version);
        Assert.True(servicio.RequiresDeposit);
        Assert.Equal(50m, servicio.DepositValue);
    }

    [Fact]
    public void An_appointment_with_deposit_starts_pending_and_one_without_starts_not_required()
    {
        Assert.Equal(DepositStatus.Pending, Cita(Porcentaje()).DepositStatus);
        Assert.Equal(DepositStatus.NotRequired, Cita(null).DepositStatus);
        Assert.Equal(DepositStatus.NotRequired, Cita(DepositPolicy.None).DepositStatus);
    }

    [Fact]
    public void The_customer_can_only_reach_reported()
    {
        var cita = Cita(Porcentaje());
        cita.ReportDeposit(Ahora);
        Assert.Equal(DepositStatus.Reported, cita.DepositStatus);
        Assert.Equal(Ahora, cita.DepositReportedAtUtc);
        // Reportar dos veces no es una transición válida: ya está reportado.
        Assert.Throws<DomainException>(() => cita.ReportDeposit(Ahora));
    }

    [Fact]
    public void Verifying_records_the_actor_and_the_date()
    {
        var actor = Guid.NewGuid();
        var cita = Cita(Fijo());
        cita.ReportDeposit(Ahora);
        cita.VerifyDeposit(actor, Ahora.AddMinutes(5));
        Assert.Equal(DepositStatus.Verified, cita.DepositStatus);
        Assert.Equal(actor, cita.DepositVerifiedByUserId);
        Assert.Equal(Ahora.AddMinutes(5), cita.DepositVerifiedAtUtc);
    }

    [Fact]
    public void Rejecting_allows_a_new_attempt()
    {
        var cita = Cita(Fijo());
        cita.ReportDeposit(Ahora);
        cita.RejectDeposit(Ahora.AddMinutes(1), "El comprobante no se lee.");
        Assert.Equal(DepositStatus.Rejected, cita.DepositStatus);
        Assert.Equal("El comprobante no se lee.", cita.DepositRejectionReason);
        Assert.Null(cita.DepositVerifiedAtUtc);
        cita.ReportDeposit(Ahora.AddMinutes(2));
        Assert.Equal(DepositStatus.Reported, cita.DepositStatus);
    }

    [Fact]
    public void Invalid_deposit_transitions_are_rejected()
    {
        var sinAdelanto = Cita(null);
        Assert.Equal("DEPOSIT_NOT_REQUIRED",
            Assert.Throws<DomainException>(() => sinAdelanto.ReportDeposit(Ahora)).Code);

        var cita = Cita(Fijo());
        cita.VerifyDeposit(Guid.NewGuid(), Ahora);
        // Verificado es definitivo: ni se reporta ni se rechaza después.
        Assert.Equal("INVALID_DEPOSIT_TRANSITION",
            Assert.Throws<DomainException>(() => cita.ReportDeposit(Ahora)).Code);
        Assert.Equal("INVALID_DEPOSIT_TRANSITION",
            Assert.Throws<DomainException>(() => cita.RejectDeposit(Ahora, null)).Code);
        Assert.Equal("INVALID_DEPOSIT_TRANSITION",
            Assert.Throws<DomainException>(() => cita.ReopenDeposit(Ahora)).Code);
    }

    [Fact]
    public void An_appointment_with_deposit_cannot_be_confirmed_before_verification()
    {
        var cita = Cita(Porcentaje());
        Assert.Equal("DEPOSIT_NOT_VERIFIED",
            Assert.Throws<DomainException>(() => cita.ChangeStatus(AppointmentStatus.Confirmed, Ahora)).Code);
        cita.ReportDeposit(Ahora);
        Assert.Throws<DomainException>(() => cita.ChangeStatus(AppointmentStatus.Confirmed, Ahora));
        cita.RejectDeposit(Ahora, null);
        Assert.Throws<DomainException>(() => cita.ChangeStatus(AppointmentStatus.Confirmed, Ahora));
    }

    [Fact]
    public void Once_verified_the_appointment_can_be_confirmed()
    {
        var cita = Cita(Porcentaje());
        cita.ReportDeposit(Ahora);
        cita.VerifyDeposit(Guid.NewGuid(), Ahora);
        cita.ChangeStatus(AppointmentStatus.Confirmed, Ahora);
        Assert.Equal(AppointmentStatus.Confirmed, cita.Status);
    }

    [Fact]
    public void An_appointment_with_a_pending_deposit_can_still_be_cancelled()
    {
        var cita = Cita(Porcentaje());
        cita.ChangeStatus(AppointmentStatus.Cancelled, Ahora);
        Assert.Equal(AppointmentStatus.Cancelled, cita.Status);
    }

    [Fact]
    public void An_appointment_without_deposit_keeps_the_previous_flow()
    {
        var cita = Cita(null);
        cita.ChangeStatus(AppointmentStatus.Confirmed, Ahora);
        Assert.Equal(AppointmentStatus.Confirmed, cita.Status);
    }

    [Fact]
    public void Only_the_platform_reverts_a_verification_and_never_on_a_confirmed_appointment()
    {
        var cita = Cita(Fijo());
        cita.VerifyDeposit(Guid.NewGuid(), Ahora);
        cita.ChangeStatus(AppointmentStatus.Confirmed, Ahora);
        Assert.Equal("APPOINTMENT_ALREADY_CONFIRMED",
            Assert.Throws<DomainException>(() => cita.RevertDepositVerification(Ahora)).Code);

        var otra = Cita(Fijo());
        otra.VerifyDeposit(Guid.NewGuid(), Ahora);
        otra.RevertDepositVerification(Ahora);
        Assert.Equal(DepositStatus.Pending, otra.DepositStatus);
        Assert.Null(otra.DepositVerifiedByUserId);
    }

    [Fact]
    public void The_whatsapp_link_carries_only_digits_and_an_encoded_message()
    {
        var mensaje = DepositMessage.Build("Salón Bella Urabá", "Corte femenino",
            new DateTimeOffset(2026, 8, 10, 14, 0, 0, TimeSpan.Zero), "America/Bogota",
            "ABCDEFGHIJ1234567890", 40000m, 80000m);
        var enlace = WhatsAppNumbers.BuildLink("+57 300 123 4567", mensaje);

        Assert.StartsWith("https://wa.me/573001234567?text=", enlace);
        Assert.DoesNotContain("+", enlace);
        Assert.DoesNotContain(" ", enlace);
        // El mensaje viaja codificado y se recupera intacto.
        var codificado = enlace["https://wa.me/573001234567?text=".Length..];
        Assert.Equal(mensaje, Uri.UnescapeDataString(codificado));
    }

    [Fact]
    public void The_message_carries_the_appointment_data_and_no_personal_contact()
    {
        var mensaje = DepositMessage.Build("Salón Bella Urabá", "Corte femenino",
            new DateTimeOffset(2026, 8, 10, 14, 0, 0, TimeSpan.Zero), "America/Bogota",
            "ABCDEFGHIJ1234567890", 40000m, 80000m);
        Assert.StartsWith("Hola, realicé el adelanto de mi cita.", mensaje);
        Assert.Contains("Negocio: Salón Bella Urabá", mensaje);
        Assert.Contains("Servicio: Corte femenino", mensaje);
        Assert.Contains("Código: ABCDEFGHIJ1234567890", mensaje);
        Assert.Contains("9:00", mensaje);           // 14:00 UTC son las 9:00 en Bogotá
        Assert.Contains("10 de agosto", mensaje);
        Assert.Contains(Money.Display(40000m), mensaje);
        Assert.Contains(Money.Display(80000m), mensaje);
        Assert.EndsWith("Adjunto el comprobante para su verificación.", mensaje);
    }
}
