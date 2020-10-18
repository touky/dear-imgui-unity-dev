#define INTERNAL_MAINTENANCE

#if INTERNAL_MAINTENANCE

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
///     This goes through the ImGui.gen.cs file and transform all the Begin/End (or approx) to C# scopes.
///     This is .... very rough and not very portable, because done in 3H a Tuesday evening.
///     Still, it works.
/// </summary>
public class ScopeGeneratorEditor : EditorWindow
{
    #region Static and Constants
    private const string TAG_SCOPE_CLASS = "#SCOPE_CLASS#";
    private const string TAG_SCOPE_STATIC = "#SCOPE_STATIC#";
    private const string TAG_BEGIN = "#BEGIN#";
    private const string TAG_END = "#END#";
    private const string TAG_CLASSNAME = "#CLASSNAME#";
    private const string TAG_SCOPE_CALL = "#SCOPE_CALL#";
    private const string TAG_RETURN_PROP = "#RETURN_VALUE_PROP#";
    private const string TAG_RETURN_SET = "#RETURN_VALUE_SET#";
    private const string TAG_RETURN_IF = "#RETURN_VALUE_IF#";
    private const string TAG_ARGUMENT_DECL = "#PARAM_DECL#";
    private const string TAG_ARGUMENT_PASS = "#PARAM_PASS#";
    private const string TAG_CTOR = "#CTOR#";

    private const string CallReturnValue = "IsOpen";
    private const string ClassPrefix = "Scope";

    private const char ARGUMENT_SEPARATOR = ',';
    private const char ARGUMENT_SPACE = ' ';
    private const string ARGUMENT_VOID = "void";
    private const string ARGUMENT_REF = "ref";
    #endregion

    #region Fields
    private string[] sourcePaths =
    {
        "../dear-imgui-touky/ImGuiNET/Wrapper/Generated/ImGui.gen.cs"
    };

    private string classPath = "../dear-imgui-touky/ImGuiNET.Unity/Scopes/ImGuiUn.Scopes.gen.cs";

    private string returnProperty = $@"        public bool {CallReturnValue} {{ get; }}
";

    private string returnSet = $"{CallReturnValue} = ";
    private string returnIf = $"if ({CallReturnValue}) ";

    private string fileCode =
        $@"namespace ImGuiNET
{{
    using System;
    using UnityEngine;

{TAG_SCOPE_CLASS}

    public static partial class ImGuiUn
    {{
{TAG_SCOPE_STATIC}
    }}
}}
";

    private string ctorCode = $@"        public {TAG_CLASSNAME}({TAG_ARGUMENT_DECL}) {{ {TAG_RETURN_SET}ImGui.{TAG_BEGIN}({TAG_ARGUMENT_PASS}); }}
";

    private string classCode =
        $@"    /// <summary>
    /// Implement a C# scope for this call set: ImGui.{TAG_BEGIN} / ImGui.{TAG_END}
    /// </summary>
    public class {TAG_CLASSNAME} : GUI.Scope
    {{
{TAG_RETURN_PROP}{TAG_CTOR}        protected override void CloseScope() {{ {TAG_RETURN_IF}ImGui.{TAG_END}(); }}
    }}

";

    private string methodCode =
        $@"        public static {TAG_CLASSNAME} {TAG_SCOPE_CALL}({TAG_ARGUMENT_DECL})
        {{ return new {TAG_CLASSNAME}({TAG_ARGUMENT_PASS}); }}
";

    private List<DetectionBlock> detectionBlock = new List<DetectionBlock>
    {
        new DetectionBlock
        {
            concatenateEnd = false,
            namePrefix = "Tree",
            begin = new Regex("([a-z]+)(?: )+(Tree)([a-zA-Z]+)*\\(([a-zA-Z0-9 ,_]*)\\)"),
            end = "([a-z]+)(?: )+(TreePop)\\("
        },
        new DetectionBlock
        {
            concatenateEnd = false,
            namePrefix = "Popup",
            begin = new Regex("([a-z]+)(?: )+(BeginPopup)([a-zA-Z]+)*\\(([a-zA-Z0-9 ,_]*)\\)"),
            end = "([a-z]+)(?: )+(EndPopup)\\("
        },
        new DetectionBlock
        {
            concatenateEnd = false,
            defaultName = "Indent",
            begin = new Regex("([a-z]+)(?: )+(Indent)([a-zA-Z]+)*\\(([a-zA-Z0-9 ,_]*)\\)"),
            end = "([a-z]+)(?: )+(Unindent)\\("
        },
        new DetectionBlock
        {
            concatenateEnd = true,
            defaultName = "Window",
            begin = new Regex("([a-z]+)(?: )+(Begin)([a-zA-Z]+)*\\(([a-zA-Z0-9 ,_]*)\\)"),
            end = "([a-z]+)(?: )+(End)#REPLACE#\\("
        },
    };

    private bool exportCode = false;
    private bool exportCommented = false;
    #endregion

    #region Unity Methods
    private void OnEnable()
    {
        // Reference to the root of the window.
        var root = rootVisualElement;

        // Creates our button and sets its Text property.
        var myButton = new Button {text = "Generate scope code"};
        myButton.clickable.clicked += Export;

        // Adds it to the root.
        root.Add(myButton);
    }
    #endregion

    #region Class Methods
    [MenuItem("DearImGui/Scope maintenance")]
    public static void ShowWindow()
    {
        var window = GetWindow<ScopeGeneratorEditor>();
        window.Focus();
        window.titleContent = new GUIContent("Scope maintenance");
    }

    private void Export()
    {
        var scopesInfos = new List<ScopeInfos>();
        GatherScopeInfos(scopesInfos);

        scopesInfos.Sort((a, b) => { return string.Compare(a.className, b.className, StringComparison.InvariantCulture); });

        var scopeCode = string.Empty;
        var scopeCall = string.Empty;
        foreach (var infos in scopesInfos)
        {
            infos.arguments.Sort((a, b) => { return a.Count - b.Count; });

            var methods = string.Empty;
            var ctor    = string.Empty;
            BuildConstructors(ref methods, ref ctor, infos);

            var code = classCode;
            code = code.Replace(TAG_CTOR, ctor);
            code = code.Replace(TAG_CLASSNAME, infos.className);
            code = code.Replace(TAG_BEGIN, infos.originalBegin);
            code = code.Replace(TAG_END, infos.originalEnd);
            code = code.Replace(TAG_RETURN_SET, string.IsNullOrEmpty(infos.returnValue) ? string.Empty : returnSet);
            code = code.Replace(TAG_RETURN_PROP, string.IsNullOrEmpty(infos.returnValue) ? string.Empty : returnProperty);
            code = code.Replace(TAG_RETURN_IF, string.IsNullOrEmpty(infos.returnValue) ? string.Empty : returnIf);

            methods = methods.Replace(TAG_CLASSNAME, infos.className);
            methods = methods.Replace(TAG_SCOPE_CALL, infos.className);

            scopeCode += code;
            scopeCall += methods;
        }

        var finalCode = fileCode.Replace(TAG_SCOPE_CLASS, scopeCode).Replace(TAG_SCOPE_STATIC, scopeCall);

        var destination = new FileInfo(Path.Combine(Application.dataPath, classPath));
        if (!destination.Exists)
        {
            return;
        }

        File.WriteAllText(destination.FullName, finalCode);
    }

    private void GatherScopeInfos(List<ScopeInfos> scopesInfos)
    {
        foreach (var sourcePath in sourcePaths)
        {
            var path     = Path.Combine(Application.dataPath, sourcePath);
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                Debug.LogError($"File {sourcePath} does not exist");
                continue;
            }

            var detectedMethods = new HashSet<string>();
            var content         = File.ReadAllText(fileInfo.FullName);
            foreach (var block in detectionBlock)
            {
                var beginMatch = block.begin.Match(content);
                while (beginMatch.Success)
                {
                    var returnValue    = beginMatch.Groups[1].Value;
                    var originalPrefix = beginMatch.Groups[2].Value;
                    var methodName     = beginMatch.Groups[3].Value;
                    var arguments      = beginMatch.Groups[4].Value;

                    if (detectedMethods.Contains(beginMatch.Value))
                    {
                        beginMatch = beginMatch.NextMatch();
                        continue;
                    }

                    var endString = block.concatenateEnd ? block.end.Replace("#REPLACE#", methodName) : block.end;
                    var endRegex  = new Regex(endString);
                    var endMatch  = endRegex.Match(content);
                    if (!endMatch.Success)
                    {
                        Debug.LogError($"FAILED to find End match: ({beginMatch.ToString()}) -> ({endRegex.ToString()})");
                        beginMatch = beginMatch.NextMatch();
                        continue;
                    }

                    detectedMethods.Add(beginMatch.Value);

                    var scopeInfos = new ScopeInfos {arguments = new List<List<Argument>>()};

                    scopeInfos.returnValue = returnValue == ARGUMENT_VOID ? string.Empty : returnValue;
                    scopeInfos.originalBegin = originalPrefix + methodName;
                    scopeInfos.originalEnd = endMatch.Groups[2] + (block.concatenateEnd ? methodName : string.Empty);
                    if (string.IsNullOrEmpty(methodName))
                    {
                        scopeInfos.className = ClassPrefix + block.namePrefix + block.defaultName;
                    }
                    else
                    {
                        scopeInfos.className = ClassPrefix + block.namePrefix + methodName;
                    }

                    var foundParams  = new List<Argument>();
                    var argumentList = arguments.Split(new[] {ARGUMENT_SEPARATOR}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in argumentList)
                    {
                        var argumentInfos = new Argument();
                        var ps            = p.Split(new[] {ARGUMENT_SPACE}, StringSplitOptions.RemoveEmptyEntries);
                        var i             = 0;
                        if (ps[i] == ARGUMENT_REF)
                        {
                            argumentInfos.isRef = true;
                            i++;
                        }

                        argumentInfos.type = ps[i++];
                        argumentInfos.name = ps[i++];

                        foundParams.Add(argumentInfos);
                    }

                    var index = scopesInfos.FindIndex(x =>
                    {
                        return x.className == scopeInfos.className;
                    });

                    if (index < 0)
                    {
                        scopeInfos.arguments.Add(foundParams);
                        scopesInfos.Add(scopeInfos);
                    }
                    else
                    {
                        var other = scopesInfos[index];
                        other.arguments.Add(foundParams);
                        scopesInfos[index] = other;
                    }

                    beginMatch = beginMatch.NextMatch();
                }
            }
        }
    }

    private void BuildConstructors(ref string methods, ref string ctor, ScopeInfos infos)
    {
        var builtArguments = new List<BuiltArguments>();
        foreach (var argumentsList in infos.arguments)
        {
            var built = new BuiltArguments
            {
                methodCode = methodCode,
                ctorCode = ctorCode,
            };

            var argumentDecl = string.Empty;
            var argumentPass = string.Empty;
            foreach (var argument in argumentsList)
            {
                if (!string.IsNullOrEmpty(argumentDecl))
                {
                    argumentDecl += $"{ARGUMENT_SEPARATOR}{ARGUMENT_SPACE}";
                    argumentPass += $"{ARGUMENT_SEPARATOR}{ARGUMENT_SPACE}";
                }

                if (argument.isRef)
                {
                    argumentDecl += $"{ARGUMENT_REF}{ARGUMENT_SPACE}";
                    argumentPass += $"{ARGUMENT_REF}{ARGUMENT_SPACE}";
                }

                argumentDecl += $"{argument.type} {argument.name}";
                argumentPass += argument.name;
            }

            built.ctorCode = built.ctorCode.Replace(TAG_ARGUMENT_DECL, argumentDecl);
            built.ctorCode = built.ctorCode.Replace(TAG_ARGUMENT_PASS, argumentPass);

            built.methodCode = built.methodCode.Replace(TAG_ARGUMENT_DECL, argumentDecl);
            built.methodCode = built.methodCode.Replace(TAG_ARGUMENT_PASS, argumentPass);

            builtArguments.Add(built);
        }

        builtArguments.Sort((a, b) => { return string.Compare(a.ctorCode, b.ctorCode, StringComparison.InvariantCulture); });

        foreach (var builtArgument in builtArguments)
        {
            methods += builtArgument.methodCode;
            ctor += builtArgument.ctorCode;
        }
    }
    #endregion

    #region Nested type: Argument
    private struct Argument
    {
        public bool isRef;
        public string type;
        public string name;
    }
    #endregion

    #region Nested type: BuiltArguments
    private struct BuiltArguments
    {
        public string ctorCode;
        public string methodCode;
    }
    #endregion

    #region Nested type: DetectionBlock
    private struct DetectionBlock
    {
        public bool concatenateEnd;
        public string defaultName;
        public string namePrefix;
        public Regex begin;
        public string end;
    }
    #endregion

    #region Nested type: ScopeInfos
    private struct ScopeInfos
    {
        public string returnValue;
        public string originalBegin;
        public string originalEnd;
        public string className;
        public List<List<Argument>> arguments;
    }
    #endregion
}
#endif
