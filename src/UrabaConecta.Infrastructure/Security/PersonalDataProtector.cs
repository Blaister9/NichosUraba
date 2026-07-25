using Microsoft.AspNetCore.DataProtection;
using UrabaConecta.Application;

namespace UrabaConecta.Infrastructure.Security;

public sealed class PersonalDataProtector(IDataProtectionProvider provider) : IPersonalDataProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("UrabaConecta.Appointments.PersonalData.v1");
    public string Protect(string value) => _protector.Protect(value);
    public string Unprotect(string value) => _protector.Unprotect(value);
}
