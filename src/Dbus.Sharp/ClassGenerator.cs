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
            var syntaxTree = SyntaxFactory.ParseSyntaxTree(result);
            //System.IO.File.WriteAllText("generated.txt", syntaxTree.GetRoot().NormalizeWhitespace().ToString());
            context.Compilation = context.Compilation.AddSyntaxTrees(
                syntaxTree
            );
            //Console.WriteLine("I was running");
        }

        private string generateClasses(BeforeCompileContext context)
        {
            var result = "namespace " + context.ProjectContext.Name + " {";
            result += "internal static class Dbus {";

            result += recurseNamespace(context.Compilation.GlobalNamespace);
            result += createInit();

            result += "}}";

            return result;
        }

        private string createInit()
        {
            var builder = new StringBuilder();
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
            var interfaceAttribute = type
                .GetAttributes()
                .FirstOrDefault(x => x.AttributeClass.Name == "InterfaceAttribute")
            ;
            if (interfaceAttribute != null)
            {
                var skipArgument = interfaceAttribute.NamedArguments.FirstOrDefault(x => x.Key == "SkipCodeGeneration");
                if (string.IsNullOrEmpty(skipArgument.Key) || !(bool)skipArgument.Value.Value)
                {
                    var interfaceAttributeArgument = interfaceAttribute.ConstructorArguments.First();
                    result += generateCodeFor(type, (string)interfaceAttributeArgument.Value);
                }
            }
            foreach (var subType in type.GetTypeMembers())
                result += handleType(subType);

            return result;
        }

        private string generateCodeFor(ITypeSymbol type, string interfaceName)
        {
            implementations.Add(type.ToString(), "Generated" + type.Name);

            var builder = new StringBuilder();
            builder.AppendLine("private class Generated" + type.Name + ": DBus.BusObject, " + type.ToString());
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

            public override void VisitEvent(IEventSymbol symbol)
            {
                var signature =
                    "public event " +
                    symbol.Type.ToString() +
                    " " +
                    symbol.Name
                ;
                events.Add(signature, new getterAndSetter());
                events[signature].Getter = buildEvent(false, symbol.Name);
                events[signature].Setter = buildEvent(true, symbol.Name);
            }

            private string buildEvent(bool isAdder, string eventName)
            {
                var builder = new StringBuilder();
                builder.AppendLine("{");
                builder.Append("ToggleSignalAsync(");
                builder.Append("\"" + interfaceName + "\", ");
                builder.Append("\"" + eventName + "\", ");
                builder.Append("value, ");
                builder.Append(isAdder ? "true" : "false");
                builder.AppendLine(").Wait();");
                builder.AppendLine("}");
                return builder.ToString();
            }

            public override void VisitMethod(IMethodSymbol symbol)
            {
                if (symbol.MethodKind != MethodKind.Ordinary)
                    return;

                var isAsync = symbol.ReturnType.ToString().StartsWith("System.Threading.Tasks.Task");
                var methodName = symbol.Name;
                if (!isAsync || !methodName.EndsWith("Async"))
                {
                    Console.WriteLine("Only asynchronous methods are supported: " + interfaceName + " " + methodName);
                    return;
                }
                methodName = methodName.Substring(0, methodName.Length - 5);

                var methodSignature =
                    "public async " +
                    symbol.ReturnType.ToString() +
                    " " +
                    symbol.Name +
                    "(" +
                    string.Join(", ", symbol.Parameters.Select(x => x.Type + " " + x.Name)) +
                    ")"
                ;
                var body = generateBody(symbol.Parameters, symbol.ReturnType, methodName);
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

                foreach (var e in events)
                {
                    builder.AppendLine(e.Key);
                    builder.AppendLine("{");
                    builder.AppendLine("add");
                    builder.AppendLine(e.Value.Setter);
                    builder.AppendLine("remove");
                    builder.AppendLine(e.Value.Getter);
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
                var namedReturnType = returnType as INamedTypeSymbol;
                var hasReturnType = namedReturnType.TypeArguments.Length > 0;

                ITypeSymbol returnDataType = null;
                var returnDataTypeString = "void";
                if (hasReturnType)
                {
                    returnDataType = namedReturnType.TypeArguments[0];
                    returnDataTypeString = returnDataType.ToString();
                }

                var builder = new StringBuilder();
                builder.AppendLine("{");

                builder.AppendLine("var writer = new DBus.Protocol.MessageWriter();");

                foreach (var parameter in parameters)
                {
                    builder.Append("writer.Write(typeof(");
                    builder.Append(parameter.Type.ToString());
                    builder.Append("), ");
                    builder.Append(parameter.Name);
                    builder.AppendLine(");");
                }

                Signature signatureIn;
                Signature signatureOut;
                sigsForMethod(parameters, returnDataType, out signatureIn, out signatureOut);

                if (hasReturnType)
                    builder.Append("var reader = ");
                builder.Append("await ");
                builder.Append("SendMethodCall(");
                builder.Append("\"" + interfaceName + "\", ");
                builder.Append("\"" + methodName + "\", ");
                builder.Append("\"" + signatureIn.Value + "\", ");
                builder.Append("writer, ");
                builder.Append("typeof(" + returnDataTypeString + ")");
                builder.Append(")");
                builder.AppendLine(";");

                if (hasReturnType)
                {
                    builder.Append("var result = ");
                    if (returnDataTypeString.StartsWith("System.Collections.Generic.Dictionary<") ||
                        returnDataTypeString.StartsWith("System.Collections.Generic.IDictionary<"))
                    {
                        var typeArguments = ((INamedTypeSymbol)returnDataType).TypeArguments;
                        builder.Append("reader.ReadDictionary<");
                        builder.Append(typeArguments[0].ToString());
                        builder.Append(",");
                        builder.Append(typeArguments[1].ToString());
                        builder.Append(">()");
                    }
                    else
                        builder.Append("(" + returnDataTypeString + ")reader.ReadValue(typeof(" + returnDataTypeString + "))");
                    builder.AppendLine(";");
                    builder.AppendLine("return result;");
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
