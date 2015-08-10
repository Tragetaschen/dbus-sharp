using Microsoft.Framework.Runtime.Roslyn;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Dbus.Sharp
{
    public class ClassGenerator : ICompileModule
    {
        public void BeforeCompile(BeforeCompileContext context)
        {
            recurseNamespace(context.Compilation.GlobalNamespace);
            Console.WriteLine("I was running");
        }

        private void recurseNamespace(INamespaceSymbol ns)
        {
            foreach (var type in ns.GetTypeMembers())
                handleType(type);
            foreach (var subNamespace in ns.GetNamespaceMembers())
                recurseNamespace(subNamespace);
        }

        private void handleType(ITypeSymbol type)
        {
            var interfaceAttributeArgument = type
                .GetAttributes()
                .FirstOrDefault(x => x.AttributeClass.Name == "InterfaceAttribute")
                ?.ConstructorArguments
                .First()
            ;
            if (interfaceAttributeArgument.HasValue)
            {
                generateCodeFor(type, (string)interfaceAttributeArgument.Value.Value);
                foreach (var subType in type.GetTypeMembers())
                    handleType(subType);
            }
        }

        private void generateCodeFor(ITypeSymbol type, string value)
        {
            Console.WriteLine("Generated" + type.Name);
            foreach (var member in type.GetMembers())
                Console.WriteLine(" " + member.Name + " " + member);
            Console.WriteLine();
        }

        public void AfterCompile(AfterCompileContext context)
        {
        }
    }
}
