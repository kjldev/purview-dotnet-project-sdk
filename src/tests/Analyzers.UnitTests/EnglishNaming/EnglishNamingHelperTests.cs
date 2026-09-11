using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.DotNetProjectSdk.Analyzers.EnglishNaming;

/// <summary>
/// Unit tests for <see cref="EnglishNamingHelper"/> acronym-capitalization logic and
/// <see cref="EnglishNamingConfig"/> .editorconfig-driven customisation.
/// </summary>
[Category("Unit")]
public sealed class EnglishNamingHelperTests
{
	static readonly EnglishNamingConfig Defaults = EnglishNamingConfig.Create(
		EnglishNamingHelper.DefaultAcronymMap,
		EnglishNamingHelper.DefaultAllowedWords
	);

	[Test]
	[Arguments("Api", "API", DisplayName = "Single acronym")]
	[Arguments("Ai", "AI", DisplayName = "Two-letter acronym")]
	[Arguments("OpenApi", "OpenAPI", DisplayName = "Acronym after leading word")]
	[Arguments("ApiClient", "APIClient", DisplayName = "Acronym prefix")]
	[Arguments("IApiClient", "IAPIClient", DisplayName = "Interface I prefix preserved")]
	[Arguments("ApiController", "APIController", DisplayName = "Common MVC pattern")]
	[Arguments("CpuService", "CPUService", DisplayName = "CPU acronym")]
	[Arguments("SqlConnection", "SQLConnection", DisplayName = "SQL acronym")]
	[Arguments("UrlBuilder", "URLBuilder", DisplayName = "URL acronym")]
	[Arguments("Cli", "CLI", DisplayName = "CLI acronym")]
	[Arguments("Uuid", "UUID", DisplayName = "UUID acronym")]
	[Arguments("GetApiToken", "GetAPIToken", DisplayName = "Acronym mid-method-name")]
	public async Task CorrectIdentifier_ReturnsCorrectedName(string name, string corrected)
	{
		var result = EnglishNamingHelper.CorrectIdentifier(name, Defaults);
		await Assert.That(result).IsEqualTo(corrected);
	}

	[Test]
	[Arguments("HttpClient", DisplayName = "Http is exempt")]
	[Arguments("XmlReader", DisplayName = "Xml is exempt")]
	[Arguments("JsonSerializer", DisplayName = "Json is exempt")]
	[Arguments("GetId", DisplayName = "Id is exempt")]
	[Arguments("SdkClient", DisplayName = "Sdk is exempt (too prevalent)")]
	[Arguments("DotNetProjectSdk", DisplayName = "Sdk embedded mid-name is exempt")]
	[Arguments("OpenAPI", DisplayName = "Already correct acronym")]
	[Arguments("HTTPClient", DisplayName = "Already correct acronym run")]
	[Arguments("Client", DisplayName = "No acronyms present")]
	public async Task CorrectIdentifier_ReturnsNull_WhenAlreadyCorrect(string name)
	{
		var result = EnglishNamingHelper.CorrectIdentifier(name, Defaults);
		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task CorrectIdentifier_AllowedWordsWin_OverAcronymMap()
	{
		var config = EnglishNamingConfig.Create(
			ImmutableDictionary.CreateRange(StringComparer.Ordinal, new Dictionary<string, string> { ["Sdk"] = "SDK" }),
			EnglishNamingHelper.DefaultAllowedWords
		);

		// 'Sdk' is in the map but also in the default allowed words, so it stays unchanged.
		var result = EnglishNamingHelper.CorrectIdentifier("SdkClient", config);
		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task CorrectIdentifier_HonoursCustomAllowedWords()
	{
		var config = EnglishNamingConfig.Create(
			EnglishNamingHelper.DefaultAcronymMap,
			ImmutableHashSet.Create(StringComparer.Ordinal, "Api")
		);

		// 'Api' allowed via config, so it is not corrected even though the default map contains it.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsNull();
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("CpuApi", config)).IsEqualTo("CPUApi");
	}

	[Test]
	public async Task CorrectIdentifier_HonoursReplacementAcronymMap()
	{
		var config = EnglishNamingConfig.Create(
			ImmutableDictionary.CreateRange(StringComparer.Ordinal, new Dictionary<string, string> { ["Foo"] = "FOO" }),
			EnglishNamingHelper.DefaultAllowedWords
		);

		// The replacement map no longer contains 'Api', so only 'Foo' is corrected.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsNull();
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("FooClient", config)).IsEqualTo("FOOClient");
	}

	[Test]
	public async Task FromOptions_Defaults_AreUsed_WhenNoOverridesPresent()
	{
		var config = EnglishNamingConfig.FromOptions(new TestOptions(new Dictionary<string, string>()));
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsEqualTo("API");
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("SdkClient", config)).IsNull();
	}

	[Test]
	public async Task FromOptions_AllowedWordsOption_ReplacesDefaults()
	{
		var options = new TestOptions(
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				[EnglishNamingHelper.AllowedWordsOptionKey] = "Http;Xml;Json;Id;Sdk;Api",
			}
		);

		var config = EnglishNamingConfig.FromOptions(options);
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsNull();
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("OpenApi", config)).IsNull();
		// Other acronyms are still corrected when the option only adds to the allowed words.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Cpu", config)).IsEqualTo("CPU");
	}

	[Test]
	public async Task FromOptions_AcronymMapOption_ReplacesDefaults()
	{
		var options = new TestOptions(
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				[EnglishNamingHelper.AcronymMapOptionKey] = "Sdk:SDK",
			}
		);

		var config = EnglishNamingConfig.FromOptions(options);
		// 'Api' is no longer mapped, so it is left alone.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsNull();
		// 'Sdk' is mapped, but the default allowed words still win.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("SdkClient", config)).IsNull();
	}

	sealed class TestOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
	{
		public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
	}
}
