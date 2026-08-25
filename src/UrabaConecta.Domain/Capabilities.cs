namespace UrabaConecta.Domain;

/// <summary>
/// Lo que un negocio puede hacer, resuelto a partir de las filas de <see cref="BusinessModule"/>.
///
/// La categoría dice qué clase de negocio es y sirve para que la gente lo encuentre; no decide
/// funciones. Una veterinaria puede querer fila y pedidos, otra sólo citas, y una óptica puede
/// terminar con la misma combinación que una droguería. Meter eso en condicionales por vertical
/// significaría un producto distinto por cada categoría nueva.
///
/// Appointments, VirtualQueues y PickupOrders son las tres operaciones que el negocio abre al
/// público. Services, Products y Staff son dependencias internas de esas operaciones, no
/// capacidades públicas independientes. Por eso su estado efectivo se deriva siempre de la
/// operación que las consume.
/// </summary>
public static class BusinessCapabilities
{
    /// <summary>Las operaciones que se abren al público. Al menos una es obligatoria.</summary>
    public static readonly BusinessModuleKind[] Operations =
        [BusinessModuleKind.Appointments, BusinessModuleKind.VirtualQueues, BusinessModuleKind.PickupOrders];

    /// <summary>Dependencias que se deducen de una operación y nunca se habilitan por separado.</summary>
    public static readonly BusinessModuleKind[] Derived =
        [BusinessModuleKind.Services, BusinessModuleKind.Products, BusinessModuleKind.Staff];

    /// <summary>Qué operación enciende cada capacidad derivada cuando no hay decisión explícita.</summary>
    public static bool DerivedDefault(BusinessModuleKind derived, IReadOnlyCollection<BusinessModuleKind> enabledOperations)
        => derived switch
        {
            BusinessModuleKind.Services => enabledOperations.Contains(BusinessModuleKind.Appointments),
            BusinessModuleKind.Staff => enabledOperations.Contains(BusinessModuleKind.Appointments),
            BusinessModuleKind.Products => enabledOperations.Contains(BusinessModuleKind.PickupOrders),
            _ => false
        };

    /// <summary>Conjunto efectivo: las filas derivadas heredadas no pueden contradecir el grafo.</summary>
    public static IReadOnlySet<BusinessModuleKind> Resolve(IEnumerable<BusinessModule> stored)
    {
        var rows = stored.ToDictionary(x => x.Module, x => x.IsEnabled);
        var result = new HashSet<BusinessModuleKind>();
        foreach (var operation in Operations)
            if (rows.TryGetValue(operation, out var enabled) && enabled) result.Add(operation);
        foreach (var derived in Derived)
            if (DerivedDefault(derived, result)) result.Add(derived);
        return result;
    }

    /// <summary>Misma resolución partiendo de banderas ya leídas, para las consultas proyectadas.</summary>
    public static IReadOnlySet<BusinessModuleKind> Resolve(bool appointments, bool queues, bool orders,
        bool? services = null, bool? products = null, bool? staff = null)
    {
        var result = new HashSet<BusinessModuleKind>();
        if (appointments) result.Add(BusinessModuleKind.Appointments);
        if (queues) result.Add(BusinessModuleKind.VirtualQueues);
        if (orders) result.Add(BusinessModuleKind.PickupOrders);
        if (DerivedDefault(BusinessModuleKind.Services, result)) result.Add(BusinessModuleKind.Services);
        if (DerivedDefault(BusinessModuleKind.Products, result)) result.Add(BusinessModuleKind.Products);
        if (DerivedDefault(BusinessModuleKind.Staff, result)) result.Add(BusinessModuleKind.Staff);
        return result;
    }

    /// <summary>Rechaza clientes antiguos que intenten guardar una contradicción explícita.</summary>
    public static void EnsureConsistent(IReadOnlyCollection<BusinessModuleKind> operations,
        bool? services, bool? products, bool? staff)
    {
        var stated = new[]
        {
            (BusinessModuleKind.Services, services),
            (BusinessModuleKind.Products, products),
            (BusinessModuleKind.Staff, staff)
        };
        foreach (var (kind, value) in stated)
            if (value.HasValue && value.Value != DerivedDefault(kind, operations))
                throw new DomainException("CAPABILITY_DEPENDENCY",
                    $"{kind} depende de la operación que la utiliza y no se puede configurar por separado.");
    }
}

/// <summary>
/// Sugerencia de arranque por categoría. Es exactamente eso: lo que se propone marcado al dar de
/// alta un negocio de esa clase, no lo que ese negocio puede llegar a tener. Quien lo crea puede
/// cambiar cualquier casilla antes de guardar, y la administración puede cambiarlas después.
/// </summary>
public static class CategoryCapabilityPresets
{
    private static readonly Dictionary<string, BusinessModuleKind[]> Presets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["odontologia"] = [BusinessModuleKind.Appointments],
        ["veterinarias"] = [BusinessModuleKind.Appointments, BusinessModuleKind.VirtualQueues, BusinessModuleKind.PickupOrders],
        ["spa-y-belleza"] = [BusinessModuleKind.Appointments, BusinessModuleKind.PickupOrders],
        ["droguerias"] = [BusinessModuleKind.PickupOrders],
        ["opticas"] = [BusinessModuleKind.Appointments, BusinessModuleKind.PickupOrders],
        ["barberia"] = [BusinessModuleKind.Appointments, BusinessModuleKind.VirtualQueues],
        ["maquillaje-y-cosmeticos"] = [BusinessModuleKind.PickupOrders]
    };

    /// <summary>Sin preselección para una categoría desconocida: mejor que elija quien da de alta.</summary>
    public static IReadOnlyList<BusinessModuleKind> For(string? categorySlug)
        => categorySlug is { Length: > 0 } slug && Presets.TryGetValue(slug, out var preset) ? preset : [];
}
