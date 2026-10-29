using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TechTraderPrevention.GameApi.Tests
{
    // Read metadata only: no game assembly is loaded or executed by this gate.
    internal static class Program
    {
        private static int checks;

        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: TechTrader.GameApi.Tests <repository root> <NuGet global packages directory>");
                return 2;
            }
            try
            {
                string project = Path.Combine(args[0], "Source", "TechTrader", "TechTrader.csproj");
                XElement[] pins = XDocument.Load(project).Descendants("RimWorldReferenceVersion").ToArray();
                if (pins.Length == 0) throw new InvalidOperationException("No pinned game references found in the mod project.");
                foreach (XElement pin in pins)
                {
                    Match condition = Regex.Match((string)pin.Attribute("Condition") ?? "",
                        @"'\$\(RimWorldVersion\)'\s*==\s*'([^']+)'");
                    if (!condition.Success) throw new InvalidOperationException("Unrecognized game-reference condition: " + pin);
                    string target = condition.Groups[1].Value;
                    string path = Path.Combine(args[1], "krafs.rimworld.ref", pin.Value.Trim().ToLowerInvariant(),
                        "ref", "net472", "Assembly-CSharp.dll");
                    using FileStream stream = File.OpenRead(path);
                    using var pe = new PEReader(stream);
                    var api = new Api(pe.GetMetadataReader());
                    CheckTarget(api, target);
                    Console.WriteLine("PASS RimWorld " + target + " (" + pin.Value.Trim() + "): required reflected hook shapes exist.");
                }
                Console.WriteLine("Game API metadata: " + checks + " checks passed across " + pins.Length + " pinned versions.");
                Console.WriteLine("This verifies target signatures, not Harmony installation or in-game behavior.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL Game API metadata: " + exception.Message);
                return 1;
            }
        }

        private static void CheckTarget(Api api, string target)
        {
            string selectionContext;
            switch (target)
            {
                case "1.4":
                case "1.5": selectionContext = "Verse.Map"; break;
                case "1.6": selectionContext = "RimWorld.IncidentParms"; break;
                default: throw new InvalidOperationException("Add explicit API expectations before supporting game version " + target + ".");
            }
            const string worker = "RimWorld.IncidentWorker_TraderCaravanArrival";
            api.RequireMethod(worker, "FactionCanBeGroupSource", "System.Boolean", "RimWorld.Faction", selectionContext, "System.Boolean");
            api.RequireMethod(worker, "TryResolveParmsGeneral", "System.Boolean", "RimWorld.IncidentParms");
            api.RequireMethod(worker, "CanFireNowSub", "System.Boolean", "RimWorld.IncidentParms");
            api.RequireMethod("RimWorld.TradeDeal", "AddAllTradeables", "System.Void");
            api.RequireMethod("RimWorld.TradeDeal", "AddToTradeables", "System.Void", "Verse.Thing", "RimWorld.Transactor");
            api.RequireMethod("RimWorld.TradeDeal", "TryExecute", "System.Boolean", "System.Boolean&");
            api.RequireMethod("Verse.Pawn", "Kill", "System.Void", "System.Nullable`1<Verse.DamageInfo>", "Verse.Hediff");
            api.RequireMethod("Verse.Pawn_HealthTracker", "MakeDowned", "System.Void", "System.Nullable`1<Verse.DamageInfo>", "Verse.Hediff");
            api.RequireField("Verse.Pawn_HealthTracker", "pawn", "Verse.Pawn");
            api.RequireFirstParameter("RimWorld.ResearchManager", "FinishProject", "Verse.ResearchProjectDef", optional: false);
            api.RequireFirstParameter("RimWorld.ResearchManager", "AddProgress", "Verse.ResearchProjectDef", optional: true);
            api.RequireMethod("RimWorld.ResearchManager", "ResetAllProgress", "System.Void");
            api.RequireMethod("RimWorld.ResearchManager", "DebugSetAllProjectsFinished", "System.Void");
        }

        private sealed class Api
        {
            private readonly MetadataReader reader;
            private readonly TypeNames names = new TypeNames();
            private readonly Dictionary<string, TypeDefinitionHandle> types;

            internal Api(MetadataReader reader)
            {
                this.reader = reader;
                types = reader.TypeDefinitions.ToDictionary(handle => TypeNames.DefinitionName(reader, handle));
            }

            private IEnumerable<MethodDefinition> Methods(string type, string name)
            {
                if (!types.TryGetValue(type, out TypeDefinitionHandle handle))
                    throw new InvalidOperationException("Missing type " + type);
                // AccessTools also searches base classes; model that lookup here.
                while (!handle.IsNil)
                {
                    TypeDefinition definition = reader.GetTypeDefinition(handle);
                    foreach (MethodDefinitionHandle methodHandle in definition.GetMethods())
                    {
                        MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                        if (reader.GetString(method.Name) == name) yield return method;
                    }
                    EntityHandle parent = definition.BaseType;
                    handle = parent.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)parent : default;
                }
            }

            internal void RequireMethod(string type, string name, string returnType, params string[] parameters)
            {
                foreach (MethodDefinition method in Methods(type, name))
                {
                    MethodSignature<string> signature = method.DecodeSignature(names, genericContext: null);
                    if ((method.Attributes & MethodAttributes.Static) == 0 && signature.ReturnType == returnType
                        && signature.ParameterTypes.SequenceEqual(parameters)) { checks++; return; }
                }
                throw new InvalidOperationException("Missing instance hook " + returnType + " " + type + "." + name
                    + "(" + string.Join(", ", parameters) + ")");
            }

            internal void RequireFirstParameter(string type, string name, string first, bool optional)
            {
                MethodDefinition[] methods = Methods(type, name).ToArray();
                if (methods.Length == 0 && optional)
                {
                    Console.WriteLine("NOTE Optional " + type + "." + name + " is absent; periodic refresh covers this build.");
                    return;
                }
                // Research callbacks deliberately ignore the remaining parameters.
                if (methods.Any(method =>
                {
                    MethodSignature<string> signature = method.DecodeSignature(names, genericContext: null);
                    return (method.Attributes & MethodAttributes.Static) == 0 && signature.ReturnType == "System.Void"
                        && signature.ParameterTypes.Length != 0 && signature.ParameterTypes[0] == first;
                })) { checks++; return; }
                throw new InvalidOperationException("Unsupported research hook " + type + "." + name + "; expected first argument " + first);
            }

            internal void RequireField(string type, string name, string fieldType)
            {
                if (!types.TryGetValue(type, out TypeDefinitionHandle handle))
                    throw new InvalidOperationException("Missing type " + type);
                foreach (FieldDefinitionHandle fieldHandle in reader.GetTypeDefinition(handle).GetFields())
                {
                    FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                    if (reader.GetString(field.Name) == name && (field.Attributes & FieldAttributes.Static) == 0
                        && field.DecodeSignature(names, genericContext: null) == fieldType) { checks++; return; }
                }
                throw new InvalidOperationException("Missing injected instance field " + type + "." + name + " : " + fieldType);
            }
        }

        private sealed class TypeNames : ISignatureTypeProvider<string, object>
        {
            internal static string DefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);
                string name = reader.GetString(type.Name);
                TypeDefinitionHandle parent = type.GetDeclaringType();
                if (!parent.IsNil) return DefinitionName(reader, parent) + "+" + name;
                string space = reader.GetString(type.Namespace);
                return space.Length == 0 ? name : space + "." + name;
            }

            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
                => DefinitionName(reader, handle);
            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                TypeReference type = reader.GetTypeReference(handle);
                string space = reader.GetString(type.Namespace);
                return (space.Length == 0 ? "" : space + ".") + reader.GetString(type.Name);
            }
            public string GetTypeFromSpecification(MetadataReader reader, object context, TypeSpecificationHandle handle, byte rawTypeKind)
                => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> arguments) => genericType + "<" + string.Join(",", arguments) + ">";
            public string GetByReferenceType(string elementType) => elementType + "&";
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
            public string GetGenericMethodParameter(object context, int index) => "!!" + index;
            public string GetGenericTypeParameter(object context, int index) => "!" + index;
            public string GetModifiedType(string modifier, string unmodifiedType, bool required) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";
        }
    }
}
