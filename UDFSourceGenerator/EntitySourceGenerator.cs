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
    
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("Entity.g.cs", SourceText.From(entityString, Encoding.UTF8));
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
        var properties = symbol.GetMembers().Where(s => s.Kind == SymbolKind.Property).ToList();
        if (properties.Count == 0)
        {
            spc.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                id: "UDF001",
                title: "No properties",
                messageFormat: "There cannot be an empty entity. Create at least one property",
                category: "UDF",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true), syntax.Identifier.GetLocation()));
            return;
        }

        if(properties.Any(s => (s as IPropertySymbol).SetMethod == null))
        {
            spc.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                id: "UDF003",
                title: "Private field",
                messageFormat: "All properties in an entity have to be settable",
                category: "UDF",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true), syntax.Identifier.GetLocation()));
            return;
        }

        var primaryKeys = properties.FindAll((s) => s.GetAttributes().Any((e) => e.AttributeClass?.Name == "PrimaryKey" || e.AttributeClass?.Name == "PrimaryKeyAttribute"));
        if (primaryKeys.Count == 0)
            primaryKeys = properties;
        
        

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("//Auto generated Equals function");
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine($"public partial class {className}");
        sb.AppendLine("{");
        sb.AppendLine($"    public override bool Equals(object? obj)");
        sb.AppendLine("     {");
        sb.AppendLine($"         if(obj == null) return false;");
        sb.AppendLine($"         if(obj is {className} c)");
        sb.AppendLine("          {");
        sb.Append($"                 return ");

        for (int i = 0; i < primaryKeys.Count; i++)
        {
            sb.Append($"c.{primaryKeys[i].Name} == {properties[i].Name}");
            if (i != primaryKeys.Count - 1)
                sb.Append(" && ");
            else
                sb.AppendLine(";");
        }
        
        sb.AppendLine("         }");
        sb.AppendLine("         return false;");
        sb.AppendLine("    }");
        // sb.AppendLine("    ");
        // sb.AppendLine($"    public override int GetHashCode()");
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