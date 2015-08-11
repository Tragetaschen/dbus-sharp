using Microsoft.Framework.Runtime.Roslyn;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using DBus.Protocol;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Dbus.Sharp
{
    public class ClassGenerator : ICompileModule
    {
        private Dictionary<string, string> implementations = new Dictionary<string, string>();

        public void BeforeCompile(BeforeCompileContext context)
        {
            var result = generateClasses(context);
            System.IO.File.WriteAllText("generated.txt", result);
            context.Compilation = context.Compilation.AddSyntaxTrees(
                SyntaxFactory.ParseSyntaxTree(result)
            );
            Console.WriteLine("I was running");
        }

        private string generateClasses(BeforeCompileContext context)
        {
            var result = "namespace " + context.ProjectContext.Name + "\n{\n";

            result += recurseNamespace(context.Compilation.GlobalNamespace);
            result += createInit();

            result += "}\n";

            return result;
        }

        private string createInit()
        {
            var builder = new StringBuilder();
            builder.AppendLine("public static class DbusInitializer");
            builder.AppendLine("{");
            builder.AppendLine("public static void Init()");
            builder.AppendLine("{");
            foreach (var implementation in implementations)
            {
                builder.Append("DBus.TypeImplementer.Root.SetImplementation(");
                builder.Append("typeof(");
                builder.Append(implementation.Key);
                builder.Append("), typeof(");
                builder.Append(implementation.Value);
                builder.Append(")");
                builder.AppendLine(");");
            }
            builder.AppendLine("}");
            builder.AppendLine("}");

            return builder.ToString();
        }

        private string recurseNamespace(INamespaceSymbol ns)
        {
            var result = string.Empty;
            foreach (var type in ns.GetTypeMembers())
                result += handleType(type);
            foreach (var subNamespace in ns.GetNamespaceMembers())
                result += recurseNamespace(subNamespace);

            return result;
        }

        private string handleType(ITypeSymbol type)
        {
            var result = string.Empty;
            var interfaceAttributeArgument = type
                .GetAttributes()
                .FirstOrDefault(x => x.AttributeClass.Name == "InterfaceAttribute")
                ?.ConstructorArguments
                .First()
            ;
            if (type.Name.EndsWith("Exception"))
                return result;

            if (interfaceAttributeArgument.HasValue)
            {
                //if (type.Name == "IBluezAdapter")
                result += generateCodeFor(type, (string)interfaceAttributeArgument.Value.Value);
                foreach (var subType in type.GetTypeMembers())
                    result += handleType(subType);
            }

            return result;
        }

        private string generateCodeFor(ITypeSymbol type, string interfaceName)
        {
            implementations.Add(type.ToString(), "Generated" + type.Name);

            var builder = new StringBuilder();
            builder.AppendLine("internal class Generated" + type.Name + ": DBus.BusObject");
            builder.AppendLine("{");
            var allMembers = type
                .GetMembers()
                .Concat(type.AllInterfaces
                    .SelectMany(x => x.GetMembers())
                )
            ;

            var visitor = new memberVisitor(interfaceName);
            foreach (var member in allMembers)
                member.Accept(visitor);

            builder.AppendLine(visitor.BuildImplementations());

            builder.AppendLine("}");
            builder.AppendLine("");

            return builder.ToString();
        }

        public void AfterCompile(AfterCompileContext context)
        {
        }

        private class memberVisitor : SymbolVisitor
        {
            private readonly string interfaceName;
            private readonly Dictionary<string, getterAndSetter> properties = new Dictionary<string, getterAndSetter>();
            private readonly Dictionary<string, getterAndSetter> events = new Dictionary<string, getterAndSetter>();
            private readonly Dictionary<string, string> methods = new Dictionary<string, string>();

            public memberVisitor(string interfaceName)
            {
                this.interfaceName = interfaceName;
            }

            public override void VisitProperty(IPropertySymbol symbol)
            {
                var propertyName = symbol.Name;
                var returnTypeString = symbol.Type.ToString();
                var methodSignature =
                    "public " +
                    returnTypeString +
                    " " +
                    propertyName
                ;
                properties.Add(methodSignature, new getterAndSetter());
                if (symbol.GetMethod != null)
                {
                    var body = generateBody(Enumerable.Empty<IParameterSymbol>(), symbol.Type, "Get" + propertyName);
                    properties[methodSignature].Getter = body;
                }
                else
                {
                    var body = generateBody(Enumerable.Empty<IParameterSymbol>(), symbol.Type, "Set" + propertyName);
                    properties[methodSignature].Setter = body;
                }
            }

            public override void VisitMethod(IMethodSymbol symbol)
            {
                if (symbol.MethodKind != MethodKind.Ordinary)
                    return;

                var methodSignature =
                    "public " +
                    symbol.ReturnType.ToString() +
                    " " +
                    symbol.Name +
                    "(" +
                    string.Join(", ", symbol.Parameters.Select(x => x.Type + " " + x.Name)) +
                    ")"
                ;
                var body = generateBody(symbol.Parameters, symbol.ReturnType, symbol.Name);
                methods[methodSignature] = body;
            }

            public string BuildImplementations()
            {
                var builder = new StringBuilder();
                foreach (var property in properties)
                {
                    builder.AppendLine(property.Key);
                    builder.AppendLine("{");
                    if (property.Value.Getter != null)
                    {
                        builder.AppendLine("get");
                        builder.AppendLine(property.Value.Getter);
                    }
                    if (property.Value.Setter != null)
                    {
                        builder.AppendLine("set");
                        builder.AppendLine(property.Value.Setter);
                    }
                    builder.AppendLine("}");
                }

                foreach (var method in methods)
                {
                    builder.AppendLine(method.Key);
                    builder.AppendLine(method.Value);
                }

                return builder.ToString();
            }

            private string generateBody(IEnumerable<IParameterSymbol> parameters, ITypeSymbol returnType, string methodName)
            {
                var returnTypeString = returnType.ToString();
                var builder = new StringBuilder();
                builder.AppendLine("{");

                builder.AppendLine("var writer = new DBus.Protocol.MessageWriter();");
                builder.AppendLine("System.Exception exception;");

                Signature signatureIn;
                Signature signatureOut;
                sigsForMethod(parameters, returnType, out signatureIn, out signatureOut);

                if (returnTypeString != "void")
                    builder.Append("var reader = ");
                builder.Append("SendMethodCall(");
                builder.Append("\"" + interfaceName + "\", ");
                builder.Append("\"" + methodName + "\", ");
                builder.Append("\"" + signatureIn.Value + "\", ");
                builder.Append("writer, ");
                builder.Append("typeof(" + returnTypeString + "), ");
                builder.Append("out exception");
                builder.AppendLine(");");

                builder.AppendLine("if (exception != null)");
                builder.AppendLine(" throw exception;");

                if (returnTypeString == "void")
                    builder.AppendLine("return;");
                else
                {
                    builder.AppendLine("return (" + returnTypeString + ")reader.ReadValue(typeof(" + returnTypeString + "));");
                }

                builder.AppendLine("}");

                return builder.ToString();
            }
        }

        private static void sigsForMethod(IEnumerable<IParameterSymbol> parameters, ITypeSymbol returnType, out Signature signatureIn, out Signature signatureOut)
        {
            signatureIn = Signature.Empty;
            signatureOut = Signature.Empty;

            foreach (var parameter in parameters)
            {
                signatureIn += Signature.GetSig(parameter.Type);
            }

            signatureOut += Signature.GetSig(returnType);
        }

        private class getterAndSetter
        {
            public string Getter;
            public string Setter;
        }
    }
}
