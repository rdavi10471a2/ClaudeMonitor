using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Services;

[AIFileContext("RazorCompanionSplitter.cs", "Splits user-written Razor markup and @code into a .razor file plus sibling .razor.cs partial-class companion without writing files directly.")]
[FileVersion("1.0")]
public static partial class RazorCompanionSplitter
{
    public static RazorCompanionSplitResult Split(
        string observedRoot,
        string razorRelativePath,
        string razorText,
        RazorCompanionSplitOptions options)
    {
        if (string.IsNullOrWhiteSpace(observedRoot))
        {
            throw new ArgumentException("Observed root is required.", nameof(observedRoot));
        }

        if (string.IsNullOrWhiteSpace(razorRelativePath))
        {
            throw new ArgumentException("Razor relative path is required.", nameof(razorRelativePath));
        }

        bool inputIsRazor = razorRelativePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
        bool inputIsHybridCompanion = razorRelativePath.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase);
        if (!inputIsRazor && !inputIsHybridCompanion)
        {
            throw new InvalidOperationException("Only .razor files or legacy hybrid .razor.cs files can be split.");
        }

        string fullPath = Path.GetFullPath(Path.Combine(observedRoot, razorRelativePath));
        string razorOutputRelativePath = inputIsHybridCompanion
            ? razorRelativePath[..^3]
            : razorRelativePath;
        string companionRelativePath = inputIsHybridCompanion
            ? razorRelativePath
            : razorRelativePath + ".cs";
        string razorOutputFullPath = Path.GetFullPath(Path.Combine(observedRoot, razorOutputRelativePath));
        string companionFullPath = Path.GetFullPath(Path.Combine(observedRoot, companionRelativePath));
        if (inputIsRazor && File.Exists(companionFullPath))
        {
            throw new InvalidOperationException($"Refusing to split because companion file already exists: {companionRelativePath}");
        }

        if (inputIsHybridCompanion && File.Exists(razorOutputFullPath))
        {
            throw new InvalidOperationException($"Refusing to split because Razor markup file already exists: {razorOutputRelativePath}");
        }

        RazorCodeBlock[] blocks = FindCodeBlocks(razorText).ToArray();
        if (blocks.Length == 0)
        {
            throw new InvalidOperationException("No @code block was found.");
        }

        if (blocks.Any(block => block.ContentStart >= block.ContentEnd))
        {
            throw new InvalidOperationException("At least one @code block was empty.");
        }

        List<string> warnings = [];
        if (blocks.Length > 1)
        {
            warnings.Add($"Multiple @code blocks were extracted: {blocks.Length}.");
        }

        string className = Path.GetFileNameWithoutExtension(razorOutputRelativePath);
        string namespaceName = FirstNonWhiteSpace(
            options.OverrideNamespace,
            FindRazorNamespaceDirective(razorText),
            FindSiblingCompanionNamespace(observedRoot, razorRelativePath),
            BuildNamespaceFromProject(observedRoot, razorRelativePath))
            ?? throw new InvalidOperationException("Unable to infer a namespace for the companion file. Pass a namespace override.");

        string[] razorUsings = CollectCompanionUsings(observedRoot, razorRelativePath, razorText).ToArray();
        string companionCode = BuildCompanionCode(namespaceName, className, razorUsings, blocks.Select(block => razorText[block.ContentStart..block.ContentEnd]));
        ValidateCompanionCode(companionCode, companionRelativePath);

        string modifiedRazorText = RemoveCodeBlocks(razorText, blocks, options.LeaveEmptyCodeBlock);
        return new RazorCompanionSplitResult(
            razorOutputRelativePath.Replace('\\', '/'),
            modifiedRazorText,
            companionCode,
            companionRelativePath.Replace('\\', '/'),
            warnings);
    }

    private static string BuildCompanionCode(
        string namespaceName,
        string className,
        IReadOnlyList<string> razorUsings,
        IEnumerable<string> codeBlocks)
    {
        StringBuilder builder = new();
        foreach (string @using in razorUsings.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            builder.Append("using ").Append(@using).AppendLine(";");
        }

        if (razorUsings.Count > 0)
        {
            builder.AppendLine();
        }

        builder.Append("namespace ").Append(namespaceName).AppendLine(";");
        builder.AppendLine();
        builder.Append("public partial class ").Append(className).AppendLine();
        builder.AppendLine("{");

        bool wroteMember = false;
        foreach (string codeBlock in codeBlocks)
        {
            string normalized = Dedent(codeBlock).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (wroteMember)
            {
                builder.AppendLine();
            }

            foreach (string line in NormalizeNewLines(normalized).Split('\n'))
            {
                builder.Append("    ").AppendLine(line);
            }

            wroteMember = true;
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void ValidateCompanionCode(string companionCode, string companionRelativePath)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(companionCode, path: companionRelativePath);
        Diagnostic? firstError = tree.GetDiagnostics().FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (firstError is null)
        {
            return;
        }

        FileLinePositionSpan span = firstError.Location.GetLineSpan();
        throw new InvalidOperationException(
            $"Generated companion C# has syntax errors at {span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}: {firstError.Id} {firstError.GetMessage()}");
    }

    private static string RemoveCodeBlocks(string razorText, IReadOnlyList<RazorCodeBlock> blocks, bool leaveEmptyCodeBlock)
    {
        string updated = razorText;
        foreach (RazorCodeBlock block in blocks.OrderByDescending(block => block.Start))
        {
            string replacement = leaveEmptyCodeBlock ? "@code { }" : string.Empty;
            updated = updated[..block.Start] + replacement + updated[block.End..];
        }

        return CollapseExcessBlankLines(updated).TrimEnd() + DetectDominantNewLine(razorText);
    }

    private static IEnumerable<RazorCodeBlock> FindCodeBlocks(string text)
    {
        int searchStart = 0;
        while (searchStart < text.Length)
        {
            int at = text.IndexOf("@code", searchStart, StringComparison.Ordinal);
            if (at < 0)
            {
                yield break;
            }

            int afterKeyword = at + "@code".Length;
            if (afterKeyword < text.Length && IsIdentifierPart(text[afterKeyword]))
            {
                searchStart = afterKeyword;
                continue;
            }

            int openBrace = SkipWhitespace(text, afterKeyword);
            if (openBrace >= text.Length || text[openBrace] != '{')
            {
                searchStart = afterKeyword;
                continue;
            }

            int closeBrace = FindMatchingBrace(text, openBrace);
            if (closeBrace < 0)
            {
                throw new InvalidOperationException("A @code block has no matching closing brace.");
            }

            int start = ExtendStartToLineStart(text, at);
            int end = ExtendEndThroughLineBreak(text, closeBrace + 1);
            yield return new RazorCodeBlock(start, end, openBrace + 1, closeBrace);
            searchStart = closeBrace + 1;
        }
    }

    private static int FindMatchingBrace(string text, int openBrace)
    {
        int depth = 0;
        for (int i = openBrace; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                i = SkipLineComment(text, i + 2);
                continue;
            }

            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i = SkipBlockComment(text, i + 2);
                continue;
            }

            if (ch == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                i = SkipVerbatimString(text, i + 2);
                continue;
            }

            if (ch == '"')
            {
                i = SkipString(text, i + 1);
                continue;
            }

            if (ch == '\'')
            {
                i = SkipCharLiteral(text, i + 1);
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int SkipLineComment(string text, int start)
    {
        int newline = text.IndexOf('\n', start);
        return newline < 0 ? text.Length - 1 : newline;
    }

    private static int SkipBlockComment(string text, int start)
    {
        int end = text.IndexOf("*/", start, StringComparison.Ordinal);
        return end < 0 ? text.Length - 1 : end + 1;
    }

    private static int SkipString(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }

            if (text[i] == '"')
            {
                return i;
            }
        }

        return text.Length - 1;
    }

    private static int SkipVerbatimString(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
                continue;
            }

            if (text[i] == '"')
            {
                return i;
            }
        }

        return text.Length - 1;
    }

    private static int SkipCharLiteral(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }

            if (text[i] == '\'')
            {
                return i;
            }
        }

        return text.Length - 1;
    }

    private static string Dedent(string text)
    {
        string normalized = NormalizeNewLines(text).Trim('\n');
        string[] lines = normalized.Split('\n');
        int? minIndent = null;
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int indent = line.TakeWhile(ch => ch == ' ' || ch == '\t').Count();
            minIndent = minIndent is null ? indent : Math.Min(minIndent.Value, indent);
        }

        int trim = minIndent ?? 0;
        return string.Join(Environment.NewLine, lines.Select(line => line.Length >= trim ? line[trim..] : line));
    }

    private static IEnumerable<string> FindRazorUsings(string razorText)
    {
        foreach (Match match in RazorUsingRegex().Matches(razorText))
        {
            string value = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value.TrimEnd(';');
            }
        }
    }

    private static IEnumerable<string> CollectCompanionUsings(string observedRoot, string razorRelativePath, string razorText)
    {
        // Razor's compiler implicitly adds Microsoft.AspNetCore.Components to every generated component, so the
        // detached companion needs it spelled out to resolve RenderFragment, ParameterAttribute, ComponentBase, etc.
        yield return "Microsoft.AspNetCore.Components";

        foreach (string @using in FindImportsRazorUsings(observedRoot, razorRelativePath))
        {
            yield return @using;
        }

        foreach (string @using in FindRazorUsings(razorText))
        {
            yield return @using;
        }
    }

    private static IEnumerable<string> FindImportsRazorUsings(string observedRoot, string razorRelativePath)
    {
        string rootFull = Path.GetFullPath(observedRoot);
        string? folder = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(observedRoot, razorRelativePath)));
        while (!string.IsNullOrWhiteSpace(folder))
        {
            string importsPath = Path.Combine(folder, "_Imports.razor");
            if (File.Exists(importsPath))
            {
                string text = File.ReadAllText(importsPath);
                foreach (string @using in FindRazorUsings(text))
                {
                    yield return @using;
                }
            }

            if (string.Equals(folder, rootFull, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            folder = Path.GetDirectoryName(folder);
        }
    }

    private static string? FindRazorNamespaceDirective(string razorText)
    {
        Match match = RazorNamespaceRegex().Match(razorText);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? FindSiblingCompanionNamespace(string observedRoot, string razorRelativePath)
    {
        string? folder = Path.GetDirectoryName(Path.Combine(observedRoot, razorRelativePath));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        foreach (string sibling in Directory.EnumerateFiles(folder, "*.razor.cs", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(sibling);
            Match fileScoped = FileScopedNamespaceRegex().Match(text);
            if (fileScoped.Success)
            {
                return fileScoped.Groups[1].Value.Trim();
            }

            Match blockScoped = BlockScopedNamespaceRegex().Match(text);
            if (blockScoped.Success)
            {
                return blockScoped.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private static string? BuildNamespaceFromProject(string observedRoot, string razorRelativePath)
    {
        string? projectPath = Directory.EnumerateFiles(observedRoot, "*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        string rootNamespace = projectPath is null
            ? new DirectoryInfo(observedRoot).Name.Replace(" ", string.Empty, StringComparison.Ordinal)
            : ReadProjectRootNamespace(projectPath);
        string? folder = Path.GetDirectoryName(razorRelativePath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return rootNamespace;
        }

        string suffix = string.Join(
            ".",
            folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(SanitizeIdentifier));
        return string.IsNullOrWhiteSpace(suffix) ? rootNamespace : rootNamespace + "." + suffix;
    }

    private static string ReadProjectRootNamespace(string projectPath)
    {
        string text = File.ReadAllText(projectPath);
        Match rootNamespace = RootNamespaceRegex().Match(text);
        if (rootNamespace.Success)
        {
            return rootNamespace.Groups[1].Value.Trim();
        }

        Match assemblyName = AssemblyNameRegex().Match(text);
        if (assemblyName.Success)
        {
            return assemblyName.Groups[1].Value.Trim();
        }

        return Path.GetFileNameWithoutExtension(projectPath).Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string SanitizeIdentifier(string value)
    {
        StringBuilder builder = new();
        foreach (char ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        return builder.Length == 0 || char.IsDigit(builder[0]) ? "_" + builder : builder.ToString();
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static int SkipWhitespace(string text, int start)
    {
        int index = start;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static int ExtendStartToLineStart(string text, int start)
    {
        int index = start;
        while (index > 0 && text[index - 1] is not '\r' and not '\n')
        {
            index--;
        }

        return index;
    }

    private static int ExtendEndThroughLineBreak(string text, int end)
    {
        if (end < text.Length && text[end] == '\r')
        {
            end++;
        }

        if (end < text.Length && text[end] == '\n')
        {
            end++;
        }

        return end;
    }

    private static string CollapseExcessBlankLines(string text)
    {
        return Regex.Replace(text, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
    }

    private static string NormalizeNewLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static string DetectDominantNewLine(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private static bool IsIdentifierPart(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_';
    }

    [GeneratedRegex(@"^\s*@using\s+([^\r\n;]+);?\s*$", RegexOptions.Multiline)]
    private static partial Regex RazorUsingRegex();

    [GeneratedRegex(@"^\s*@namespace\s+([^\r\n]+)\s*$", RegexOptions.Multiline)]
    private static partial Regex RazorNamespaceRegex();

    [GeneratedRegex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;", RegexOptions.Multiline)]
    private static partial Regex FileScopedNamespaceRegex();

    [GeneratedRegex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*\{", RegexOptions.Multiline)]
    private static partial Regex BlockScopedNamespaceRegex();

    [GeneratedRegex(@"<RootNamespace>\s*([^<]+)\s*</RootNamespace>", RegexOptions.IgnoreCase)]
    private static partial Regex RootNamespaceRegex();

    [GeneratedRegex(@"<AssemblyName>\s*([^<]+)\s*</AssemblyName>", RegexOptions.IgnoreCase)]
    private static partial Regex AssemblyNameRegex();

    private sealed record RazorCodeBlock(int Start, int End, int ContentStart, int ContentEnd);
}

public sealed record RazorCompanionSplitOptions(
    bool LeaveEmptyCodeBlock = false,
    string? OverrideNamespace = null);

public sealed record RazorCompanionSplitResult(
    string RazorRelativePath,
    string OriginalRazorText,
    string CompanionCsText,
    string CompanionRelativePath,
    IReadOnlyList<string> Warnings);
