using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProjectIntrospector
{
    public class CodeInspector
    {
        private readonly string rootPath;
        private readonly string? entityPath;
        private readonly StringBuilder output;
        private readonly Regex filterRegex;

        public CodeInspector(string rootPath, string? entityPath, string filterPattern)
        {
            this.rootPath = rootPath;
            this.entityPath = entityPath;
            this.output = new StringBuilder();
            this.filterRegex = new Regex($"^({filterPattern})$", RegexOptions.IgnoreCase);
        }

        public void Run()
        {
            output.AppendLine("# 项目目录结构（已过滤无关目录）");
            AppendDirectoryTree(new DirectoryInfo(rootPath), "", true);
            output.AppendLine("\n# 类与方法信息\n");

            ProcessDirectory(rootPath, extractEntities: false);

            if (!string.IsNullOrWhiteSpace(entityPath) && Directory.Exists(entityPath))
            {
                output.AppendLine("\n## 实体类字段与属性信息");
                ProcessDirectory(entityPath, extractEntities: true);
            }

            File.WriteAllText(Path.Combine(rootPath, "project_structure.md"), output.ToString());
        }

        private void AppendDirectoryTree(DirectoryInfo dir, string indent, bool isLast)
        {
            if (ShouldIgnore(dir.Name)) return;

            output.AppendLine($"{indent}{(isLast ? "└──" : "├──")}{dir.Name}/");

            var files = dir.GetFiles("*.cs").OrderBy(f => f.Name).ToArray();
            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                output.AppendLine($"{indent}{(isLast ? "    " : "│   ")}{(i == files.Length - 1 ? "└──" : "├──")}{file.Name}");
            }

            var subDirs = dir.GetDirectories().Where(d => !ShouldIgnore(d.Name)).OrderBy(d => d.Name).ToArray();
            for (int i = 0; i < subDirs.Length; i++)
            {
                AppendDirectoryTree(subDirs[i], indent + (isLast ? "    " : "│   "), i == subDirs.Length - 1);
            }
        }

        private bool ShouldIgnore(string dirName)
        {
            return filterRegex.IsMatch(dirName);
        }

        private void ProcessDirectory(string path, bool extractEntities)
        {
            var csFiles = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => filterRegex.IsMatch(part)))
                .ToArray();

            foreach (var file in csFiles)
            {
                var code = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();

                var relativePath = Path.GetRelativePath(rootPath, file);
                output.AppendLine($"## 文件: {relativePath}\n```csharp");

                var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
                foreach (var type in types)
                {
                    string kind = type.Kind().ToString().Replace("Declaration", "");
                    string rootModifiers = string.Join(" ", type.Modifiers.Select(m => m.Text));
                    string summary = GetSummaryComment(type);
                    output.AppendLine($"{rootModifiers} {kind.ToLower()} {type.Identifier.Text}" + (string.IsNullOrWhiteSpace(summary) ? "" : $" // {summary}"));

                    foreach (var ctor in type.Members.OfType<ConstructorDeclarationSyntax>())
                    {
                        output.AppendLine("    " + FormatSignature(ctor.Modifiers, ctor.Identifier.Text, ctor.ParameterList.Parameters, null, GetSummaryComment(ctor)));
                    }

                    foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                    {
                        output.AppendLine("    " + FormatSignature(method.Modifiers, method.Identifier.Text, method.ParameterList.Parameters, method.ReturnType.ToString(), GetSummaryComment(method)));
                    }

                    if (extractEntities)
                    {
                        foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
                        {
                            string fieldType = field.Declaration.Type.ToString();
                            string extractEntitiesFieldModifiers = string.Join(" ", field.Modifiers.Select(m => m.Text));
                            foreach (var v in field.Declaration.Variables)
                            {
                                output.AppendLine($"    {extractEntitiesFieldModifiers} {fieldType} {v.Identifier.Text};");
                            }
                        }

                        foreach (var prop in type.Members.OfType<PropertyDeclarationSyntax>())
                        {
                            string extractEntitiesPropertyModifiers = string.Join(" ", prop.Modifiers.Select(m => m.Text));
                            string summaryProp = GetSummaryComment(prop);
                            output.AppendLine($"    {extractEntitiesPropertyModifiers} {prop.Type} {prop.Identifier.Text} {{ get; set; }};" + (string.IsNullOrWhiteSpace(summaryProp) ? "" : $" // {summaryProp}"));
                        }
                    }
                }

                output.AppendLine("```\n");
            }
        }

        private string FormatSignature(SyntaxTokenList modifiers, string name, SeparatedSyntaxList<ParameterSyntax> parameters, string? returnType, string? summary)
        {
            string mod = string.Join(" ", modifiers.Select(m => m.Text));
            string paramList = string.Join(", ", parameters.Select(p => p.ToString()));
            return $"{mod} {(returnType != null ? returnType + " " : "")}{name}({paramList});" + (string.IsNullOrWhiteSpace(summary) ? "" : $" // {summary}");
        }

        private static string GetSummaryComment(MemberDeclarationSyntax member)
        {
            var trivia = member.GetLeadingTrivia()
                .Select(t => t.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();

            var summaryElement = trivia?.Content
                .OfType<XmlElementSyntax>()
                .FirstOrDefault(e => e.StartTag.Name.ToString() == "summary");

            return summaryElement?.Content.ToFullString().Trim().Replace("\n", " ").Replace("  ", " ") ?? "";
        }
    }
}