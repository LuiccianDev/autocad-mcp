using System;

namespace AutoCAD.McpServer.Server;

internal enum StructuredJsonKind
{
    None = 0,
    Array = 1,
    Object = 2,
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
internal sealed class ToolDomainAttribute : Attribute
{
    public ToolDomainAttribute(string domain, string compoundToolName, string summary)
    {
        Domain = domain ?? throw new ArgumentNullException(nameof(domain));
        CompoundToolName = compoundToolName ?? throw new ArgumentNullException(nameof(compoundToolName));
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    public string Domain { get; }

    public string CompoundToolName { get; }

    public string Summary { get; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
internal sealed class ToolExposureAttribute : Attribute
{
    public bool Core { get; set; }

    public string? AliasOf { get; set; }
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
internal sealed class CompoundParamAttribute : Attribute
{
    public CompoundParamAttribute(string? canonicalName = null)
    {
        CanonicalName = canonicalName;
    }

    public string? CanonicalName { get; }

    public StructuredJsonKind StructuredJson { get; set; } = StructuredJsonKind.None;
}
