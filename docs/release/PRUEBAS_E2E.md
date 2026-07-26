# Pruebas E2E

Los cuatro recorridos de `OrderingHardeningJourneyTests` son independientes:

1. El precio del pedido histórico permanece congelado y uno nuevo usa el precio actualizado.
2. Un producto inactivo desaparece del menú, permanece en el histórico y puede reactivarse.
3. Una franja llena responde conflicto específico y otra acepta el pedido.
4. La cancelación pública tardía se rechaza y el pedido permanece en preparación.

Ejecute:

```powershell
dotnet test tests\UrabaConecta.EndToEndTests\UrabaConecta.EndToEndTests.csproj -c Release --no-build
```
