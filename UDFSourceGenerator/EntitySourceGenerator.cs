using System.Collections;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UDFSourceGenerator;

[Generator]
public class EntitySourceGenerator : IIncrementalGenerator
{

    private string entityString =
        "namespace UDFSourceGenerator.AttributesTemplates;\n\n[AttributeUsage(AttributeTargets.Class)]\npublic class Entity : Attribute\n{\n    \n}";

    private string primaryKeyString =
        "namespace UDFSourceGenerator.AttributesTemplates;\n\n[AttributeUsage(AttributeTargets.Parameter)]\npublic class PrimaryKeyAttribute : Attribute\n{\n    \n}";

    private string autoIncrementKeyString =
        "namespace UDFSourceGenerator.AttributesTemplates;\n\n[AttributeUsage(AttributeTargets.Parameter)]\npublic class AutoIncrementAttribute : Attribute\n{\n    \n}";
    
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("Entity.g.cs", SourceText.From(entityString, Encoding.UTF8));
            ctx.AddSource("PrimaryKeyAttribute.g.cs", SourceText.From(primaryKeyString, Encoding.UTF8));
            ctx.AddSource("AutoIncrementAttribute.g.cs", SourceText.From(autoIncrementKeyString, Encoding.UTF8));
        });
        
        var modifingClasses = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: (node, _) => IsCandidate(node),
            transform: (gsc, _) => GetCandidate(gsc)).Where(static x => x != null);
        
        context.RegisterSourceOutput(modifingClasses, GenerateEqualizer);
    }


    private static void GenerateEqualizer(SourceProductionContext spc, (ClassDeclarationSyntax classSyntax, INamedTypeSymbol? symbol)? items)
    {
        if (items == null) return;
        
        ClassDeclarationSyntax syntax = items.Value.classSyntax;
        INamedTypeSymbol? symbol = items.Value.symbol;
        if (symbol.InstanceConstructors.Length <= 0) return;
        var primaryConstructor = symbol.InstanceConstructors[0];

        var parameters = new List<IParameterSymbol>(primaryConstructor.Parameters);

        if (!syntax.Modifiers.Any((s) => s.Text.Equals("partial")))
        {
            spc.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                id: "UDF002",
                title: "Not partial",
                messageFormat: "Each entity field has to be partial as to allow to generate an Equals function.",
                category: "UDF",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true), syntax.Identifier.GetLocation()));
            return;
        }
        
        var ns = symbol.ContainingNamespace.ToDisplayString();
        var className = symbol.Name;
        if (symbol == null) return;
        var primaryKeys = parameters.FindAll((s) => s.GetAttributes().Any((e) => e.AttributeClass?.Name == "PrimaryKeyAttribute"));
        if (primaryKeys.Count == 0)
            primaryKeys = parameters;
        
        

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("//Auto generated Equals function");
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine("using SQLite;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine($"public partial class {className} : INotifyPropertyChanged");
        sb.AppendLine("{");
        sb.AppendLine($"    public event PropertyChangedEventHandler? PropertyChanged;");
        sb.AppendLine("    #pragma warning disable CS0649");
        sb.AppendLine("    #pragma warning disable CS0169");
        sb.AppendLine("    #pragma warning disable CS9113");
        sb.AppendLine("    #pragma warning disable CS8618");
        sb.AppendLine("    #nullable enable");
        foreach (var parameter in parameters)
        {
            sb.AppendLine($"    private static {parameter.Type.Name} _static{parameter.Name};");
        }

        sb.AppendLine("");
        sb.Append($"    public {className}() : this(");
        foreach (var parameter in parameters)
        {
            sb.Append($"_static{parameter.Name}, ");
        }
        sb.Remove(sb.Length - 2, 2);
        sb.AppendLine("){}");
        foreach (var parameter in parameters)
        {
            sb.AppendLine($"    private {parameter.Type.Name} _{parameter.Name};");
            if (parameter.GetAttributes().Any(a => a.AttributeClass?.Name == "PrimaryKeyAttribute"))
                sb.AppendLine("    [PrimaryKey]");
            if (parameter.GetAttributes().Any(a => a.AttributeClass?.Name == "AutoIncrementAttribute"))
                sb.AppendLine("    [AutoIncrement]");
            sb.AppendLine($"    public {parameter.Type.Name} {parameter.Name}");
            sb.AppendLine("    {");
            sb.AppendLine($"        get =>  _{parameter.Name};");
            sb.AppendLine($"        set");
            sb.AppendLine("         {");
            sb.AppendLine($"             if(value == _{parameter.Name}) return;");
            sb.AppendLine($"             _{parameter.Name} = value;");
            sb.AppendLine("              OnPropertyChanged();");
            sb.AppendLine("         }");
            sb.AppendLine("     }");
        }
        sb.AppendLine($"    public override bool Equals(object? obj)");
        sb.AppendLine("     {");
        sb.AppendLine($"         if(obj == null) return false;");
        sb.AppendLine($"         if(obj is {className} c)");
        sb.AppendLine("          {");
        sb.Append($"                 return ");

        for (int i = 0; i < primaryKeys.Count; i++)
        {
            sb.Append($"c.{primaryKeys[i].Name} == {parameters[i].Name}");
            if (i != primaryKeys.Count - 1)
                sb.Append(" && ");
            else
                sb.AppendLine(";");
        }
        
        sb.AppendLine("         }");
        sb.AppendLine("         return false;");
        sb.AppendLine("    }");
        sb.AppendLine("     protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)");
        sb.AppendLine("     {");
        sb.AppendLine($"         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));");
        sb.AppendLine("     }");
        sb.AppendLine("}");
        
        
        spc.AddSource($"{className}.g.cs",  SourceText.From(sb.ToString(), Encoding.UTF8));

    }

    private static (ClassDeclarationSyntax classSyntax, INamedTypeSymbol? symbol)? GetCandidate(GeneratorSyntaxContext context)
    {
        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax)
        {
            
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax) as INamedTypeSymbol;
            if (symbol == null) return null;
            
            var entityAttr = context.SemanticModel.Compilation.GetTypeByMetadataName("UDFSourceGenerator.AttributesTemplates.Entity");
            if (entityAttr == null) return null;
            
            return symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, entityAttr)) ? (
                classDeclarationSyntax ,symbol
                ) : null;
        }else if (context.Node is FieldDeclarationSyntax fieldDeclarationSyntax)
        {
            
        }

        return null;
    }

    private bool IsCandidate(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclarationSyntax && classDeclarationSyntax.AttributeLists.Count > 0;
    }
    
}