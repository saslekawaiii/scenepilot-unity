// Adapted from CoplayDev/unity-mcp, MIT, Copyright (c) 2025 CoplayDev.
// Source commit e2fd09c71644d1d9fa1ae134526ce913cbf97536, ExecuteCode.cs RoslynCompiler.
// Modifications: Unity bundled compiler discovery, unique assembly identity, local namespace/loader.
// License: ThirdPartyNotices/CoplayDev-MIT.txt
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
namespace WorldWeaver.Editor.Assistant
{
    internal static class EditorSnippetCompiler
    {
        private static bool? _isAvailable;
        private static Type _syntaxTreeType;
        private static Type _compilationType;
        private static Type _compilationOptionsType;
        private static Type _parseOptionsType;
        private static Type _metadataReferenceType;
        private static Type _outputKindEnum;
        private static Type _languageVersionEnum;
        private static MethodInfo _parseText;
        private static MethodInfo _createCompilation;
        private static MethodInfo _createFromFile;
        private static MethodInfo _emit;
        private static object _parseOptions;
        private static object _compilationOptions;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable == null)
                    _isAvailable = Initialize();
                return _isAvailable.Value;
            }
        }

        public static void ResetCache()
        {
            _isAvailable = null;
        }

        internal static bool HasActiveLegacyInputCalls(string source,IEnumerable<string> defines)
        {
            if(!IsAvailable)return false;
            var withSymbols=_parseOptionsType.GetMethod("WithPreprocessorSymbols",new[]{typeof(IEnumerable<string>)});
            var options=withSymbols.Invoke(_parseOptions,new object[]{defines});
            var tree=_parseText.Invoke(null,new object[]{source,options,"",Encoding.UTF8,default(System.Threading.CancellationToken)});
            var root=tree.GetType().GetMethod("GetRoot",new[]{typeof(System.Threading.CancellationToken)}).Invoke(tree,new object[]{default(System.Threading.CancellationToken)});
            var descendants=root.GetType().GetMethods().First(m=>m.Name=="DescendantNodes"&&m.GetParameters().Length==2);
            var nodes=(System.Collections.IEnumerable)descendants.Invoke(root,new object[]{null,false});
            foreach(var node in nodes)
            {
                var type=node.GetType();if(type.Name!="MemberAccessExpressionSyntax")continue;
                string expression=type.GetProperty("Expression").GetValue(node).ToString();
                string member=type.GetProperty("Name").GetValue(node).ToString();
                if((expression=="Input"||expression=="UnityEngine.Input")&&System.Text.RegularExpressions.Regex.IsMatch(member,@"^(?:GetAxis|GetAxisRaw|GetKey|GetKeyDown|GetKeyUp|GetMouseButton|GetMouseButtonDown|GetMouseButtonUp|mousePosition|mouseScrollDelta|inputString|anyKey|anyKeyDown)$"))return true;
            }
            return false;
        }

        private static bool Initialize()
        {
            try
            {
                string bundled=Path.Combine(UnityEditor.EditorApplication.applicationContentsPath,"Resources/Scripting/MonoBleedingEdge/lib/mono/4.5");
                foreach(string name in new[]{"System.Collections.Immutable.dll","System.Reflection.Metadata.dll","Microsoft.CodeAnalysis.dll","Microsoft.CodeAnalysis.CSharp.dll"})
                {
                    string path=Path.Combine(bundled,name);
                    if(File.Exists(path)&&!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name==Path.GetFileNameWithoutExtension(name))) Assembly.LoadFrom(path);
                }
                _syntaxTreeType = Type.GetType("Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree, Microsoft.CodeAnalysis.CSharp");
                _compilationType = Type.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilation, Microsoft.CodeAnalysis.CSharp");
                _compilationOptionsType = Type.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions, Microsoft.CodeAnalysis.CSharp");
                _parseOptionsType = Type.GetType("Microsoft.CodeAnalysis.CSharp.CSharpParseOptions, Microsoft.CodeAnalysis.CSharp");
                _metadataReferenceType = Type.GetType("Microsoft.CodeAnalysis.MetadataReference, Microsoft.CodeAnalysis");
                _outputKindEnum = Type.GetType("Microsoft.CodeAnalysis.OutputKind, Microsoft.CodeAnalysis");
                _languageVersionEnum = Type.GetType("Microsoft.CodeAnalysis.CSharp.LanguageVersion, Microsoft.CodeAnalysis.CSharp");

                if (_syntaxTreeType == null || _compilationType == null || _compilationOptionsType == null ||
                    _parseOptionsType == null || _metadataReferenceType == null || _outputKindEnum == null ||
                    _languageVersionEnum == null)
                    return false;

                // CSharpSyntaxTree.ParseText(string, CSharpParseOptions, string, Encoding, CancellationToken)
                var syntaxTreeBase = Type.GetType("Microsoft.CodeAnalysis.SyntaxTree, Microsoft.CodeAnalysis");
                _parseText = _syntaxTreeType.GetMethod("ParseText", new[] { typeof(string), _parseOptionsType, typeof(string), typeof(Encoding), typeof(System.Threading.CancellationToken) });
                if (_parseText == null)
                    return false;

                // CSharpCompilation.Create(string, IEnumerable<SyntaxTree>, IEnumerable<MetadataReference>, CSharpCompilationOptions)
                var metadataRefBase = _metadataReferenceType;
                var syntaxTreeEnumerable = typeof(IEnumerable<>).MakeGenericType(syntaxTreeBase);
                var metadataRefEnumerable = typeof(IEnumerable<>).MakeGenericType(metadataRefBase);
                _createCompilation = _compilationType.GetMethod("Create", new[] { typeof(string), syntaxTreeEnumerable, metadataRefEnumerable, _compilationOptionsType });
                if (_createCompilation == null)
                    return false;

                // MetadataReference.CreateFromFile(string, MetadataReferenceProperties, DocumentationProvider)
                _createFromFile = _metadataReferenceType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "CreateFromFile");
                if (_createFromFile == null)
                    return false;

                // Emit has no single-param overload; the simplest is
                // Emit(Stream, Stream, Stream, Stream, IEnumerable<ResourceDescription>, EmitOptions, CancellationToken)
                var compilationBase = Type.GetType("Microsoft.CodeAnalysis.Compilation, Microsoft.CodeAnalysis");
                if (compilationBase == null) return false;
                _emit = compilationBase.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "Emit")
                    .OrderBy(m => m.GetParameters().Length)
                    .FirstOrDefault();
                if (_emit == null)
                    return false;

                // Build CSharpParseOptions — constructor has optional params, use reflection
                var latestValue = Enum.Parse(_languageVersionEnum, "Latest");
                var parseOptionsCtor = _parseOptionsType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
                var parseCtorParams = parseOptionsCtor.GetParameters();
                var parseArgs = new object[parseCtorParams.Length];
                for (int i = 0; i < parseCtorParams.Length; i++)
                {
                    if (parseCtorParams[i].Name == "languageVersion")
                        parseArgs[i] = latestValue;
                    else if (parseCtorParams[i].HasDefaultValue)
                        parseArgs[i] = parseCtorParams[i].DefaultValue;
                    else
                        parseArgs[i] = null;
                }
                _parseOptions = parseOptionsCtor.Invoke(parseArgs);

                // Build CSharpCompilationOptions — use the first constructor (has most defaults)
                var dllKind = Enum.Parse(_outputKindEnum, "DynamicallyLinkedLibrary");
                var compOptionsCtor = _compilationOptionsType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
                var compCtorParams = compOptionsCtor.GetParameters();
                var compArgs = new object[compCtorParams.Length];
                for (int i = 0; i < compCtorParams.Length; i++)
                {
                    if (compCtorParams[i].Name == "outputKind")
                        compArgs[i] = dllKind;
                    else if (compCtorParams[i].HasDefaultValue)
                        compArgs[i] = compCtorParams[i].DefaultValue;
                    else
                        compArgs[i] = null;
                }
                _compilationOptions = compOptionsCtor.Invoke(compArgs);

                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[ExecuteCode] Roslyn initialization failed: {e.Message}");
                return false;
            }
        }

        public static Assembly Compile(string source, string[] assemblyPaths, out List<string> errors)
        {
            errors = new List<string>();

            try
            {
                // Parse source
                var syntaxTree = _parseText.Invoke(null, new object[] { source, _parseOptions, null, null, default(System.Threading.CancellationToken) });

                // Build metadata references
                var metadataRefBase = _metadataReferenceType;
                var listType = typeof(List<>).MakeGenericType(metadataRefBase);
                var refs = (System.Collections.IList)Activator.CreateInstance(listType);

                foreach (var path in assemblyPaths)
                {
                    try
                    {
                        var cfParams = _createFromFile.GetParameters();
                        var cfArgs = new object[cfParams.Length];
                        cfArgs[0] = path; // string path
                        for (int i = 1; i < cfParams.Length; i++)
                            cfArgs[i] = cfParams[i].HasDefaultValue ? cfParams[i].DefaultValue : null;
                        var metaRef = _createFromFile.Invoke(null, cfArgs);
                        refs.Add(metaRef);
                    }
                    catch
                    {
                        // Skip assemblies that can't be loaded as metadata
                    }
                }

                // Build syntax tree array
                var syntaxTreeBase = Type.GetType("Microsoft.CodeAnalysis.SyntaxTree, Microsoft.CodeAnalysis");
                var treeArray = Array.CreateInstance(syntaxTreeBase, 1);
                treeArray.SetValue(syntaxTree, 0);

                // Create compilation
                var compilation = _createCompilation.Invoke(null, new object[] { "WWEditorSnippet_" + Guid.NewGuid().ToString("N"), treeArray, refs, _compilationOptions });

                // Emit to memory
                using (var ms = new MemoryStream())
                {
                    // Build args for the Emit overload (fill non-stream params with defaults)
                    var emitParams = _emit.GetParameters();
                    var emitArgs = new object[emitParams.Length];
                    emitArgs[0] = ms; // peStream
                    for (int i = 1; i < emitParams.Length; i++)
                    {
                        if (emitParams[i].HasDefaultValue)
                            emitArgs[i] = emitParams[i].DefaultValue;
                        else
                            emitArgs[i] = null;
                    }
                    var emitResult = _emit.Invoke(compilation, emitArgs);

                    // Check emitResult.Success
                    var successProp = emitResult.GetType().GetProperty("Success");
                    bool success = (bool)successProp.GetValue(emitResult);

                    if (!success)
                    {
                        // Read emitResult.Diagnostics
                        var diagProp = emitResult.GetType().GetProperty("Diagnostics");
                        var diagnostics = (System.Collections.IEnumerable)diagProp.GetValue(emitResult);
                        var severityError = Enum.Parse(Type.GetType("Microsoft.CodeAnalysis.DiagnosticSeverity, Microsoft.CodeAnalysis"), "Error");

                        foreach (var diag in diagnostics)
                        {
                            var sevProp = diag.GetType().GetProperty("Severity");
                            var severity = sevProp.GetValue(diag);
                            if (!severity.Equals(severityError)) continue;

                            var locProp = diag.GetType().GetProperty("Location");
                            var loc = locProp.GetValue(diag);
                            var spanProp = loc.GetType().GetMethod("GetLineSpan");
                            var lineSpan = spanProp.Invoke(loc, null);
                            var startProp = lineSpan.GetType().GetProperty("StartLinePosition");
                            var startPos = startProp.GetValue(lineSpan);
                            var lineProp = startPos.GetType().GetProperty("Line");
                            int line = (int)lineProp.GetValue(startPos);

                            var msgProp = diag.GetType().GetMethod("GetMessage", new[] { typeof(System.Globalization.CultureInfo) });
                            string msg = (string)msgProp.Invoke(diag, new object[] { null });

                            int userLine = Math.Max(1, line + 1 - 6);
                            errors.Add($"Line {userLine}: {msg}");
                        }
                        return null;
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    return Assembly.Load(ms.ToArray());
                }
            }
            catch (Exception e)
            {
                // Walk to the deepest cause: TargetInvocationException (and friends) wrap the real
                // failure inside .InnerException, and reporting only e.Message hides everything
                // useful (e.g. a missing transitive dep manifests as the generic "Exception has been
                // thrown by the target of an invocation.").
                Exception root = e;
                while (root.InnerException != null) root = root.InnerException;
                errors.Add($"Roslyn compilation error: {root.GetType().Name}: {root.Message}");
                return null;
            }
        }
    }
}
