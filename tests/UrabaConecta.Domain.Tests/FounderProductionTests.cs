using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

public sealed class FounderProductionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-27T12:00:00Z");

    private static Business Draft() => Business.CreateDraft(Guid.NewGuid(), "piloto-v5", "Piloto V5",
        Guid.NewGuid(), Guid.NewGuid(), "Descripción completa", "Calle 1", "3000000000", null, null, Now);

    private static BusinessProfileEdit Edit(Business business, string? shortDescription = null,
        string? phone = "3001234567", string? email = null, string? instagram = null, string? facebook = null)
        => new(business.Slug, business.Name, business.MunicipalityId, business.CategoryId,
            shortDescription ?? "Descripción breve válida.", "Descripción completa", "Calle 1",
            "Frente al parque", phone, null, email, instagram, facebook, null, "Traiga su cédula.");

    // ---------------------------------------------------------------- perfil

    [Fact]
    public void Commercial_profile_stores_the_fields_added_in_v5()
    {
        var business = Draft();
        business.UpdateCommercialProfile(Edit(business, email: "contacto@negocio.co",
            instagram: "https://instagram.com/negocio", facebook: "https://www.facebook.com/negocio"), Now, 0);
        Assert.Equal("Descripción breve válida.", business.ShortDescription);
        Assert.Equal("Frente al parque", business.ReferencePoint);
        Assert.Equal("contacto@negocio.co", business.PublicEmail);
        Assert.Equal("Traiga su cédula.", business.CustomerInstructions);
    }

    [Theory]
    [InlineData("123", "INVALID_PHONE")]
    [InlineData("no-es-un-telefono", "INVALID_PHONE")]
    public void Phone_format_is_validated(string phone, string expected)
    {
        var business = Draft();
        Assert.Equal(expected, Assert.Throws<DomainException>(() =>
            business.UpdateCommercialProfile(Edit(business, phone: phone), Now, 0)).Code);
    }

    [Fact]
    public void Email_and_social_links_must_point_where_they_claim()
    {
        var business = Draft();
        Assert.Equal("INVALID_EMAIL", Assert.Throws<DomainException>(() =>
            business.UpdateCommercialProfile(Edit(business, email: "sin-arroba"), Now, 0)).Code);
        Assert.Equal("INVALID_SOCIAL_URL", Assert.Throws<DomainException>(() =>
            business.UpdateCommercialProfile(Edit(business, instagram: "https://ejemplo.com/negocio"), Now, 0)).Code);
    }

    [Fact]
    public void Short_description_is_required_and_bounded()
    {
        var business = Draft();
        Assert.Equal("INVALID_SHORT_DESCRIPTION", Assert.Throws<DomainException>(() =>
            business.UpdateCommercialProfile(Edit(business, shortDescription: "  "), Now, 0)).Code);
        Assert.Equal("INVALID_SHORT_DESCRIPTION", Assert.Throws<DomainException>(() =>
            business.UpdateCommercialProfile(Edit(business, shortDescription: new string('a', 161)), Now, 0)).Code);
    }

    // -------------------------------------------------------------- revisión

    [Fact]
    public void Review_requires_a_complete_checklist_and_does_not_publish()
    {
        var business = Draft();
        Assert.Equal("BUSINESS_NOT_READY", Assert.Throws<DomainException>(() =>
            business.SubmitForReview(false, Now, 0)).Code);
        business.SubmitForReview(true, Now, 0);
        Assert.Equal(BusinessStatus.PendingReview, business.Status);
        Assert.False(business.IsPublished);
        Assert.Equal(Now, business.SubmittedForReviewAtUtc);
    }

    [Fact]
    public void Rejection_returns_the_business_to_configuration_with_notes()
    {
        var business = Draft();
        business.SubmitForReview(true, Now, 0);
        Assert.Equal("REVIEW_NOTES_REQUIRED", Assert.Throws<DomainException>(() =>
            business.RejectReview("", Now, 1)).Code);
        business.RejectReview("Falta la portada.", Now, 1);
        Assert.Equal(BusinessStatus.PendingConfiguration, business.Status);
        Assert.Equal("Falta la portada.", business.ReviewNotes);
    }

    [Fact]
    public void Publishing_clears_the_review_notes_and_records_the_first_publication()
    {
        var business = Draft();
        business.SubmitForReview(true, Now, 0);
        business.RejectReview("Falta la portada.", Now, 1);
        business.SubmitForReview(true, Now, 2);
        business.Activate(true, Now.AddHours(1), 3);
        Assert.Equal(BusinessStatus.Active, business.Status);
        Assert.Null(business.ReviewNotes);
        Assert.Equal(Now.AddHours(1), business.PublishedAtUtc);
    }

    [Fact]
    public void Configuration_change_during_review_cancels_the_review()
    {
        var business = Draft();
        business.SubmitForReview(true, Now, 0);
        business.ConfigurationChanged(Now, 1);
        Assert.Equal(BusinessStatus.PendingConfiguration, business.Status);
    }

    [Fact]
    public void Only_one_person_can_be_recorded_as_the_creator()
    {
        var business = Draft();
        business.AssignCreator(Guid.NewGuid());
        Assert.Equal("CREATOR_ALREADY_ASSIGNED", Assert.Throws<DomainException>(() =>
            business.AssignCreator(Guid.NewGuid())).Code);
    }

    // -------------------------------------------------------------- checklist

    [Fact]
    public void Checklist_reports_progress_and_names_what_is_missing()
    {
        var readiness = BusinessReadinessCalculator.Calculate(true, true,
            [BusinessModuleKind.VirtualQueues], false, false, true, false, false, false,
            new BusinessCompletionSignals(HasContact: true, HasLocation: true, HasLogo: false, HasCover: false));
        Assert.False(readiness.IsReady);
        Assert.InRange(readiness.CompletionPercentage, 1, 99);
        Assert.Contains("Cargue el logo del negocio.", readiness.MissingLabels);
        Assert.Contains("Cargue la imagen de portada.", readiness.MissingLabels);
    }

    [Fact]
    public void Checklist_is_complete_when_every_applicable_requirement_is_met()
    {
        var readiness = BusinessReadinessCalculator.Calculate(true, true,
            [BusinessModuleKind.VirtualQueues], false, false, true, false, false, false);
        Assert.True(readiness.IsReady);
        Assert.Equal(100, readiness.CompletionPercentage);
        Assert.Empty(readiness.MissingLabels);
    }

    // ------------------------------------------------------------- imágenes

    [Fact]
    public void Image_metadata_is_validated_and_alt_text_rejects_markup()
    {
        var businessId = Guid.NewGuid();
        Assert.Equal("INVALID_IMAGE", Assert.Throws<DomainException>(() => new BusinessImage(
            Guid.NewGuid(), businessId, BusinessImageKind.Logo, "k", "image/png", 0, 10, 100, null, 0, Now)).Code);
        Assert.Equal("INVALID_ALT_TEXT", Assert.Throws<DomainException>(() => new BusinessImage(
            Guid.NewGuid(), businessId, BusinessImageKind.Logo, "k", "image/png", 10, 10, 100,
            "<script>x</script>", 0, Now)).Code);
    }

    [Fact]
    public void Deleting_an_image_twice_is_rejected()
    {
        var image = new BusinessImage(Guid.NewGuid(), Guid.NewGuid(), BusinessImageKind.Gallery, "k",
            "image/webp", 800, 600, 4096, "Fachada", 0, Now);
        image.SoftDelete(Now, 0);
        Assert.True(image.IsDeleted);
        Assert.Equal("IMAGE_DELETED", Assert.Throws<DomainException>(() => image.SoftDelete(Now, 1)).Code);
    }

    [Fact]
    public void Image_changes_use_optimistic_concurrency()
    {
        var image = new BusinessImage(Guid.NewGuid(), Guid.NewGuid(), BusinessImageKind.Gallery, "k",
            "image/jpeg", 800, 600, 4096, null, 0, Now);
        Assert.Equal("CONCURRENCY_CONFLICT", Assert.Throws<DomainException>(() =>
            image.Describe("Fachada", 1, Now, 7)).Code);
    }

    // ---------------------------------------------------------- invitaciones

    private static AccessInvitation Invitation(AccessGrantKind grant = AccessGrantKind.PartnerOperator,
        Guid? businessId = null, TimeSpan? lifetime = null,
        AccessInvitationPurpose purpose = AccessInvitationPurpose.Invitation)
        => new(Guid.NewGuid(), "Socia@Ejemplo.CO", "Socia", grant, businessId, "hash-del-token",
            Guid.NewGuid(), Now, lifetime ?? TimeSpan.FromHours(72), purpose);

    [Fact]
    public void Invitation_normalizes_the_email_and_starts_pending()
    {
        var invitation = Invitation();
        Assert.Equal("socia@ejemplo.co", invitation.Email);
        Assert.True(invitation.IsPending(Now));
        Assert.Equal("Pending", invitation.StatusFor(Now));
    }

    [Fact]
    public void An_invitation_to_a_business_role_requires_a_business()
    {
        Assert.Equal("BUSINESS_REQUIRED", Assert.Throws<DomainException>(() =>
            Invitation(AccessGrantKind.BusinessOwner)).Code);
        // Un reinicio de contraseña no concede accesos nuevos, por eso no exige negocio.
        var reset = Invitation(AccessGrantKind.PartnerOperator, purpose: AccessInvitationPurpose.PasswordReset);
        Assert.Equal(AccessInvitationPurpose.PasswordReset, reset.Purpose);
    }

    [Fact]
    public void A_token_can_only_be_used_once()
    {
        var invitation = Invitation();
        invitation.Accept(Guid.NewGuid(), Now);
        Assert.Equal("Accepted", invitation.StatusFor(Now));
        Assert.Equal("INVITATION_ALREADY_USED", Assert.Throws<DomainException>(() =>
            invitation.Accept(Guid.NewGuid(), Now)).Code);
    }

    [Fact]
    public void An_expired_token_is_refused()
    {
        var invitation = Invitation(lifetime: TimeSpan.FromHours(1));
        var later = Now.AddHours(2);
        Assert.False(invitation.IsPending(later));
        Assert.Equal("Expired", invitation.StatusFor(later));
        Assert.Equal("INVITATION_EXPIRED", Assert.Throws<DomainException>(() =>
            invitation.Accept(Guid.NewGuid(), later)).Code);
    }

    [Fact]
    public void A_revoked_token_cannot_be_accepted_and_cannot_be_revoked_twice()
    {
        var invitation = Invitation();
        var actor = Guid.NewGuid();
        invitation.Revoke(actor, Now);
        Assert.Equal("Revoked", invitation.StatusFor(Now));
        Assert.Equal("INVITATION_REVOKED", Assert.Throws<DomainException>(() =>
            invitation.Accept(Guid.NewGuid(), Now)).Code);
        Assert.Equal("INVITATION_ALREADY_REVOKED", Assert.Throws<DomainException>(() =>
            invitation.Revoke(actor, Now)).Code);
    }

    [Fact]
    public void An_accepted_invitation_can_no_longer_be_revoked()
    {
        var invitation = Invitation();
        invitation.Accept(Guid.NewGuid(), Now);
        Assert.Equal("INVITATION_ALREADY_ACCEPTED", Assert.Throws<DomainException>(() =>
            invitation.Revoke(Guid.NewGuid(), Now)).Code);
    }

    [Fact]
    public void Repeated_failures_lock_the_invitation_temporarily()
    {
        var invitation = Invitation();
        for (var i = 0; i < AccessInvitation.MaximumFailedAttempts; i++) invitation.RegisterFailedAttempt(Now);
        Assert.NotNull(invitation.LockedUntilUtc);
        Assert.Equal("INVITATION_LOCKED", Assert.Throws<DomainException>(() =>
            invitation.Accept(Guid.NewGuid(), Now)).Code);
        // Pasado el bloqueo, el enlace vuelve a ser utilizable.
        invitation.Accept(Guid.NewGuid(), invitation.LockedUntilUtc!.Value.AddMinutes(1));
        Assert.NotNull(invitation.AcceptedAtUtc);
    }

    [Fact]
    public void An_invitation_lifetime_has_an_upper_bound()
    {
        Assert.Equal("INVALID_INVITATION_LIFETIME", Assert.Throws<DomainException>(() =>
            Invitation(lifetime: TimeSpan.FromDays(60))).Code);
    }

    // -------------------------------------------------------------- auditoría

    [Fact]
    public void Access_audit_truncates_the_address_and_rejects_oversized_summaries()
    {
        var audit = new PlatformAccessAudit(Guid.NewGuid(), Guid.NewGuid(),
            PlatformAccessAction.InvitationCreated, "AccessInvitation", Guid.NewGuid().ToString(), null,
            "Enlace emitido.", new string('9', 80), Now);
        Assert.Equal(45, audit.IpAddress!.Length);
        Assert.Equal("INVALID_AUDIT_SUMMARY", Assert.Throws<DomainException>(() => new PlatformAccessAudit(
            Guid.NewGuid(), null, PlatformAccessAction.PasswordChanged, "ApplicationUser", "x", null,
            new string('a', 401), null, Now)).Code);
    }

    [Fact]
    public void Status_history_bounds_the_notes()
    {
        Assert.Equal("INVALID_STATE_NOTES", Assert.Throws<DomainException>(() => new BusinessStatusChange(
            Guid.NewGuid(), Guid.NewGuid(), BusinessStatus.Draft, BusinessStatus.PendingReview,
            Guid.NewGuid(), new string('a', 401), Now)).Code);
    }

    // ----------------------------------------------------------- consentimiento

    [Fact]
    public void Consent_receipt_links_a_queue_ticket_and_keeps_minimal_evidence()
    {
        var consent = new ConsentReceipt(Guid.NewGuid(), Guid.NewGuid(), "2026-1", "Gestionar el turno.", Now);
        var ticketId = Guid.NewGuid();
        consent.LinkQueueTicket(ticketId);
        consent.RecordOrigin("203.0.113.10");
        Assert.Equal(ticketId, consent.QueueTicketId);
        Assert.Equal("203.0.113.10", consent.IpAddress);
        Assert.Equal("2026-1", consent.NoticeVersion);
    }
}
