using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.DotNetProjectSdk.Analyzers.EnglishNaming;

/// <summary>
/// Correct-English acronym capitalization rules backing <see cref="EnglishNamingAnalyzer"/> (PDS0004).
/// The defaults are deliberately curated: .NET naming guidance recommends spellings such as
/// <c>Api</c>/<c>Xml</c>/<c>Http</c>, but correct English writes them as <c>API</c>/<c>XML</c>/<c>HTTP</c>.
/// Per-repo customisation is available through <c>.editorconfig</c>:
/// <list type="bullet">
/// <item><c>dotnet_analyzer_configuration.pds0004.allowed_words</c> — semicolon-separated segments that
/// must never be flagged (replaces the default list when set).</item>
/// <item><c>dotnet_analyzer_configuration.pds0004.acronym_map</c> — semicolon-separated
/// <c>Key:Value</c> pairs overriding the default map (replaces it when set).</item>
/// </list>
/// </summary>
static class EnglishNamingHelper
{
	/// <summary>
	/// Segments that are exempt from acronym capitalization even though they look like acronyms.
	/// These follow the official .NET guidance (<c>Http</c>, <c>Xml</c>, <c>Json</c>, <c>Id</c>) plus
	/// <c>Sdk</c>, which is too prevalent across the ecosystem to flag.
	/// </summary>
	internal static readonly ImmutableHashSet<string> DefaultAllowedWords = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		"Http",
		"Xml",
		"Json",
		"Id",
		"Sdk"
	);

	/// <summary>
	/// Segment spelling corrections. Keys are the .NET-recommended (but English-incorrect) spellings;
	/// values are the correct-English acronym capitalization.
	/// </summary>
	internal static readonly ImmutableDictionary<string, string> DefaultAcronymMap = ImmutableDictionary.CreateRange(
		StringComparer.Ordinal,
		new Dictionary<string, string>
		{
			["Api"] = "API",
			["Ai"] = "AI",
			["Ui"] = "UI",
			["Gui"] = "GUI",
			["Cpu"] = "CPU",
			["Dns"] = "DNS",
			["Tcp"] = "TCP",
			["Udp"] = "UDP",
			["Sql"] = "SQL",
			["Url"] = "URL",
			["Cli"] = "CLI",
			["Csv"] = "CSV",
			["Pdf"] = "PDF",
			["Html"] = "HTML",
			["Css"] = "CSS",
			["Ftp"] = "FTP",
			["Ssh"] = "SSH",
			["Os"] = "OS",
			["Io"] = "IO",
			["Uuid"] = "UUID",
			["Ram"] = "RAM",
			["Gpu"] = "GPU",
			["Smtp"] = "SMTP",
			["Imap"] = "IMAP",
		}
	);

	internal const string AllowedWordsOptionKey = "dotnet_analyzer_configuration.pds0004.allowed_words";
	internal const string AcronymMapOptionKey = "dotnet_analyzer_configuration.pds0004.acronym_map";

	internal static readonly char[] SemicolonSeparators = [';'];

	/// <summary>
	/// Returns the corrected identifier when any segment is not already correct English, otherwise <c>null</c>.
	/// A leading <c>I</c> interface prefix (e.g. <c>IApiClient</c>) is preserved around the correction.
	/// </summary>
	internal static string? CorrectIdentifier(string name, EnglishNamingConfig config)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}

		var stripInterfacePrefix = name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);
		var body = stripInterfacePrefix ? name.Substring(1) : name;

		var words = SplitIntoWords(body);
		var changed = false;
		for (var i = 0; i < words.Count; i++)
		{
			var word = words[i];
			if (config.AllowedWords.Contains(word))
			{
				continue;
			}

			if (config.AcronymMap.TryGetValue(word, out var replacement))
			{
				words[i] = replacement;
				changed = true;
			}
		}

		if (!changed)
		{
			return null;
		}

		var corrected = string.Concat(words);
		return stripInterfacePrefix ? "I" + corrected : corrected;
	}

	/// <summary>
	/// Splits an identifier into words at case boundaries, keeping acronym runs intact
	/// (<c>HTTPClient</c> → <c>HTTP</c>|<c>Client</c>, <c>OpenApi</c> → <c>Open</c>|<c>Api</c>).
	/// </summary>
	static List<string> SplitIntoWords(string name)
	{
		var words = new List<string>();
		var start = 0;
		for (var i = 1; i < name.Length; i++)
		{
			var current = name[i];
			if (!char.IsUpper(current))
			{
				continue;
			}

			var previous = name[i - 1];
			var isLowerBoundary = char.IsLower(previous) || char.IsDigit(previous);
			var isAcronymBoundary =
				char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1]) && i - start > 1;

			if (isLowerBoundary || isAcronymBoundary)
			{
				words.Add(name.Substring(start, i - start));
				start = i;
			}
		}

		words.Add(name.Substring(start));
		return words;
	}
}

/// <summary>
/// Effective PDS0004 configuration for a compilation, composed from the shipped defaults plus any
/// <c>.editorconfig</c> overrides. When an override option is present it replaces the corresponding default.
/// </summary>
sealed class EnglishNamingConfig
{
	EnglishNamingConfig(ImmutableDictionary<string, string> acronymMap, ImmutableHashSet<string> allowedWords)
	{
		AcronymMap = acronymMap;
		AllowedWords = allowedWords;
	}

	public ImmutableDictionary<string, string> AcronymMap { get; }

	public ImmutableHashSet<string> AllowedWords { get; }

	public static EnglishNamingConfig FromOptions(AnalyzerConfigOptions options)
	{
		var allowedWords = EnglishNamingHelper.DefaultAllowedWords;
		if (options.TryGetValue(EnglishNamingHelper.AllowedWordsOptionKey, out var allowedRaw))
		{
			var parsed = SplitList(allowedRaw);
			if (parsed is not null)
			{
				allowedWords = parsed;
			}
		}

		var acronymMap = EnglishNamingHelper.DefaultAcronymMap;
		if (options.TryGetValue(EnglishNamingHelper.AcronymMapOptionKey, out var mapRaw))
		{
			var parsed = ParseMap(mapRaw);
			if (parsed is not null)
			{
				acronymMap = parsed;
			}
		}

		return new EnglishNamingConfig(acronymMap, allowedWords);
	}

	internal static EnglishNamingConfig Create(
		ImmutableDictionary<string, string> acronymMap,
		ImmutableHashSet<string> allowedWords
	)
	{
		return new EnglishNamingConfig(acronymMap, allowedWords);
	}

	static ImmutableHashSet<string>? SplitList(string value)
	{
		var segments = value
			.Split(EnglishNamingHelper.SemicolonSeparators, StringSplitOptions.RemoveEmptyEntries)
			.Select(static s => s.Trim())
			.Where(static s => s.Length > 0)
			.ToArray();

		return segments.Length > 0 ? ImmutableHashSet.Create(StringComparer.Ordinal, segments) : null;
	}

	static ImmutableDictionary<string, string>? ParseMap(string value)
	{
		var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
		foreach (
			var pair in value.Split(EnglishNamingHelper.SemicolonSeparators, StringSplitOptions.RemoveEmptyEntries)
		)
		{
			var index = pair.IndexOf(':');
			if (index <= 0 || index >= pair.Length - 1)
			{
				continue;
			}

			var key = pair.Substring(0, index).Trim();
			var replacement = pair.Substring(index + 1).Trim();
			if (key.Length > 0 && replacement.Length > 0)
			{
				builder[key] = replacement;
			}
		}

		return builder.Count > 0 ? builder.ToImmutable() : null;
	}
}
