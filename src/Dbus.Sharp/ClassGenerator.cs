using DBus.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DBus
{
    public class ClassGenerator
    {
        private Dictionary<string, string> implementations = new Dictionary<string, string>();

        public string Generate(IEnumerable<Type> types)
        {
            var result = "internal static partial class Dbus {";

            foreach (var type in types)
                result += handleType(type);
            result += createInit();

            result += "}";

            return result;
        }

        private string createInit()
        {
            var builder = new StringBuilder();
            builder.AppendLine("static partial void DoInit()");
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

        private string handleType(Type type)
        {
            var interfaceAttribute = type.GetCustomAttribute<InterfaceAttribute>();

            var result = string.Empty;
            if (interfaceAttribute != null)
            {
                if (!interfaceAttribute.SkipCodeGeneration)
                {
                    result += generateCodeFor(type, interfaceAttribute.Name);
                }
            }
            foreach (var subType in type.GetNestedTypes())
                result += handleType(subType);

            return result;
        }

        private string generateCodeFor(Type type, string interfaceName)
        {
            implementations.Add(buildTypeString(type), "Generated" + type.Name);

            var builder = new StringBuilder();
            builder.AppendLine("private class Generated" + type.Name + ": DBus.BusObject, " + buildTypeString(type));
            builder.AppendLine("{");
            var allMembers = type.GetMembers().Concat(
                type.GetInterfaces().SelectMany(x => x.GetMembers())
            );

            var implementer = new memberImplementer(interfaceName);
            foreach (var member in allMembers)
                implementer.Add(member);

            builder.AppendLine(implementer.BuildImplementations());

            builder.AppendLine("}");
            builder.AppendLine("");

            return builder.ToString();
        }

        private class memberImplementer
        {
            private readonly string interfaceName;
            private readonly Dictionary<string, getterAndSetter> properties = new Dictionary<string, getterAndSetter>();
            private readonly Dictionary<string, getterAndSetter> events = new Dictionary<string, getterAndSetter>();
            private readonly Dictionary<string, string> methods = new Dictionary<string, string>();

            public memberImplementer(string interfaceName)
            {
                this.interfaceName = interfaceName;
            }

            public void Add(MemberInfo member)
            {
                EventInfo eventInfo;
                PropertyInfo propertyInfo;
                MethodInfo methodInfo;

                if ((eventInfo = member as EventInfo) != null)
                    implementEvent(eventInfo);
                else if ((propertyInfo = member as PropertyInfo) != null)
                    implementProperty(propertyInfo);
                else if ((methodInfo = member as MethodInfo) != null)
                    implementMethod(methodInfo);
            }

            private void implementEvent(EventInfo e)
            {
                var signature =
                    "public event " +
                    buildTypeString(e.EventHandlerType) +
                    " " +
                    e.Name
                ;
                events.Add(signature, new getterAndSetter());
                events[signature].Getter = buildEvent(false, e.Name);
                events[signature].Setter = buildEvent(true, e.Name);
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

            private void implementProperty(PropertyInfo property)
            {
                var signature =
                    "public " + buildTypeString(property.PropertyType) +
                    " " +
                    property.Name
                ;
                var type = property.PropertyType.GetGenericArguments()[0];

                properties.Add(signature, new getterAndSetter());
                if (property.GetMethod != null)
                    properties[signature].Getter = buildPropertyGet(property.Name, buildTypeString(type));
            }

            private string buildPropertyGet(string propertyName, string type)
            {
                var builder = new StringBuilder();
                builder.AppendLine("{");
                builder.Append("return ");
                builder.Append("SendPropertyGet(");
                builder.Append("\"" + interfaceName + "\", ");
                builder.Append("\"" + propertyName + "\"");
                builder.Append(")");
                builder.Append(".ContinueWith(x => (" + type + ")x.Result)");
                builder.AppendLine(";");
                builder.AppendLine("}");
                return builder.ToString();
            }

            private void implementMethod(MethodInfo method)
            {
                if (method.IsSpecialName)
                    return;

                var isAsync = buildTypeString(method.ReturnType).StartsWith("System.Threading.Tasks.Task");
                var methodName = method.Name;
                if (!isAsync || !methodName.EndsWith("Async"))
                {
                    Console.WriteLine("Only asynchronous methods are supported: " + interfaceName + " " + methodName);
                    return;
                }
                methodName = methodName.Substring(0, methodName.Length - 5);

                var methodSignature =
                    "public async " +
                    buildTypeString(method.ReturnType) +
                    " " +
                    method.Name +
                    "(" +
                    string.Join(", ", method.GetParameters().Select(x => buildTypeString(x.ParameterType) + " " + x.Name)) +
                    ")"
                ;
                var body = generateBody(method.GetParameters(), method.ReturnType, methodName);
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

            private string generateBody(IEnumerable<ParameterInfo> parameters, Type returnType, string methodName)
            {
                var typeArguments = returnType.GetGenericArguments();
                var hasReturnType = typeArguments.Length > 0;

                var returnDataType = typeof(void);
                var returnDataTypeString = "void";
                if (hasReturnType)
                {
                    returnDataType = typeArguments[0];
                    returnDataTypeString = buildTypeString(returnDataType);
                }

                var builder = new StringBuilder();
                builder.AppendLine("{");

                builder.AppendLine("var writer = new DBus.Protocol.MessageWriter();");

                foreach (var parameter in parameters)
                {
                    builder.Append("writer.Write(typeof(");
                    builder.Append(buildTypeString(parameter.ParameterType));
                    builder.Append("), ");
                    builder.Append(parameter.Name);
                    builder.AppendLine(");");
                }

                Signature signatureIn;
                Signature signatureOut;
                sigsForMethod(
                    parameters.Select(x => x.ParameterType),
                    returnDataType,
                    out signatureIn,
                    out signatureOut
                );

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
                builder.Append(".ConfigureAwait(false)");
                builder.AppendLine(";");

                if (hasReturnType)
                {
                    builder.Append("var result = ");
                    if (returnDataTypeString.StartsWith("System.Collections.Generic.Dictionary<") ||
                        returnDataTypeString.StartsWith("System.Collections.Generic.IDictionary<"))
                    {
                        var dataTypeArguments = returnDataType.GetGenericArguments();
                        builder.Append("reader.ReadDictionary<");
                        builder.Append(buildTypeString(dataTypeArguments[0]));
                        builder.Append(",");
                        builder.Append(buildTypeString(dataTypeArguments[1]));
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

        private static void sigsForMethod(IEnumerable<Type> parameters, Type returnType, out Signature signatureIn, out Signature signatureOut)
        {
            signatureIn = Signature.Empty;
            signatureOut = Signature.Empty;

            foreach (var parameter in parameters)
            {
                signatureIn += Signature.GetSig(parameter);
            }

            signatureOut += Signature.GetSig(returnType);
        }

        private static string buildTypeString(Type type)
        {
            if (!type.IsConstructedGenericType)
                return type.FullName;

            var genericName = type.GetGenericTypeDefinition().FullName;
            var withoutSuffix = genericName.Substring(0, genericName.Length - 2);
            var result = withoutSuffix + "<" +
                string.Join(",", type.GenericTypeArguments.Select(buildTypeString)) +
                ">"
            ;
            return result;
        }

        private class getterAndSetter
        {
            public string Getter;
            public string Setter;
        }
    }
}
