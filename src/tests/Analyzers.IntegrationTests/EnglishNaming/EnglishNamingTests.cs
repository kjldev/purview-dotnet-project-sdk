using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Purview.DotNetProjectSdk.CodeFixers.EnglishNaming;

namespace Purview.DotNetProjectSdk.Analyzers.EnglishNaming;

/// <summary>
/// Integration tests for <see cref="EnglishNamingAnalyzer"/> (PDS0004) and
/// <see cref="EnglishNamingCodeFixProvider"/>.
/// </summary>
[Category("Integration")]
public sealed class EnglishNamingTests
{
	[Test]
	public async Task Analyzer_TypeNamedApi_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source = "class Api { }";
		var diagnostics = await GetDiagnosticsAsync(source, cancellationToken);

		await Assert.That(diagnostics).Count().IsEqualTo(1);
		await Assert.That(diagnostics[0].Id).IsEqualTo(EnglishNamingAnalyzer.DiagnosticId);
	}

	[Test]
	public async Task Analyzer_TypeNamedOpenApi_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source = "class OpenApi { }";
		var diagnostics = await GetDiagnosticsAsync(source, cancellationToken);

		await Assert.That(diagnostics).Count().IsEqualTo(1);
		await Assert.That(diagnostics[0].Id).IsEqualTo(EnglishNamingAnalyzer.DiagnosticId);
	}

	[Test]
	public async Task Analyzer_MethodNamedGetApi_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source = "class C { void GetApi() { } }";
		var diagnostics = await GetDiagnosticsAsync(source, cancellationToken);

		await Assert.That(diagnostics).Count().IsEqualTo(1);
		await Assert.That(diagnostics[0].Id).IsEqualTo(EnglishNamingAnalyzer.DiagnosticId);
	}

	[Test]
	public async Task Analyzer_AllowedWords_DoNotReportDiagnostic(CancellationToken cancellationToken)
	{
		const string source = """
			class HttpClient { }
			class JsonSerializer { }
			class XmlReader { }
			class Widget { string GetId() => ""; }
			class SdkClient { }
			""";
		var diagnostics = await GetDiagnosticsAsync(source, cancellationToken);

		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	public async Task Analyzer_AlreadyCorrectAcronym_DoesNotReportDiagnostic(CancellationToken cancellationToken)
	{
		const string source = "class OpenAPI { }";
		var diagnostics = await GetDiagnosticsAsync(source, cancellationToken);

		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	public async Task Analyzer_RespectsEditorConfig_AllowedWords(CancellationToken cancellationToken)
	{
		const string source = "class Api { }";

		using var workspace = new AdhocWorkspace();
		var document = CreateDocument(
			workspace,
			source,
			AnalyzerTestInfrastructure.NormalizeFakePath(@"C:\FakeProject\Test.cs")
		);
		var solution = document.Project.Solution.AddAnalyzerConfigDocument(
			DocumentId.CreateNewId(document.Project.Id),
			".editorconfig",
			SourceText.From("is_root = true\n[*.cs]\ndotnet_analyzer_configuration.pds0004.allowed_words = Api\n"),
			filePath: AnalyzerTestInfrastructure.NormalizeFakePath(@"C:\FakeProject\.editorconfig")
		);

		var diagnostics = await GetDiagnosticsAsync(solution.GetDocument(document.Id)!, cancellationToken);

		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	public async Task CodeFixProvider_IsExported_ForVisualStudioDiscovery(CancellationToken cancellationToken)
	{
		_ = cancellationToken;

		await Assert.That(typeof(EnglishNamingAnalyzer).IsPublic).IsTrue();
		await Assert.That(typeof(EnglishNamingCodeFixProvider).IsPublic).IsTrue();

		var attributes = Attribute.GetCustomAttributes(typeof(EnglishNamingCodeFixProvider), inherit: false);
		await Assert.That(attributes.OfType<ExportCodeFixProviderAttribute>().Any()).IsTrue();
		await Assert.That(attributes.OfType<SharedAttribute>().Any()).IsTrue();
	}

	[Test]
	public async Task FixableDiagnosticIds_ContainsEnglishNamingDiagnosticId(CancellationToken cancellationToken)
	{
		_ = cancellationToken;

		var provider = new EnglishNamingCodeFixProvider();

		await Assert.That(provider.FixableDiagnosticIds).Contains(EnglishNamingAnalyzer.DiagnosticId);
	}

	[Test]
	public async Task CodeFix_TypeNamedOpenApi_IsRenamed_AcrossReferences(CancellationToken cancellationToken)
	{
		const string before = """
			class OpenApi
			{
			}

			class Consumer
			{
				OpenApi value = new OpenApi();
			}
			""";

		const string after = """
			class OpenAPI
			{
			}

			class Consumer
			{
				OpenAPI value = new OpenAPI();
			}
			""";

		using var workspace = new AdhocWorkspace();
		var document = CreateDocument(workspace, before);
		var diagnostic = (await GetDiagnosticsAsync(document, cancellationToken)).Single();
		var provider = new EnglishNamingCodeFixProvider();
		var actions = new List<CodeAction>();
		var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);

		await provider.RegisterCodeFixesAsync(context);
		var operations = await actions.Single().GetOperationsAsync(cancellationToken);
		var fixedDocument = ((ApplyChangesOperation)operations.Single()).ChangedSolution.GetDocument(document.Id)!;
		var fixedSource = (await fixedDocument.GetTextAsync(cancellationToken)).ToString();

		await Assert.That(fixedSource).IsEqualTo(after);
	}

	[Test]
	public async Task CodeFix_MethodNamedGetApi_IsRenamed(CancellationToken cancellationToken)
	{
		const string before = "class C { void GetApi() { } }";
		const string after = "class C { void GetAPI() { } }";

		using var workspace = new AdhocWorkspace();
		var document = CreateDocument(workspace, before);
		var diagnostic = (await GetDiagnosticsAsync(document, cancellationToken)).Single();
		var provider = new EnglishNamingCodeFixProvider();
		var actions = new List<CodeAction>();
		var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);

		await provider.RegisterCodeFixesAsync(context);
		var operations = await actions.Single().GetOperationsAsync(cancellationToken);
		var fixedDocument = ((ApplyChangesOperation)operations.Single()).ChangedSolution.GetDocument(document.Id)!;
		var fixedSource = (await fixedDocument.GetTextAsync(cancellationToken)).ToString();

		await Assert.That(fixedSource).IsEqualTo(after);
	}

	[Test]
	public async Task CodeFix_TypeNamedOpenApi_IsRenamed_AcrossMultipleDocuments(CancellationToken cancellationToken)
	{
		const string declarationSource = "class OpenApi\n{\n}\n";
		const string consumerSource = "class Consumer\n{\n\tOpenApi value = new OpenApi();\n}\n";

		const string expectedDeclaration = "class OpenAPI\n{\n}\n";
		const string expectedConsumer = "class Consumer\n{\n\tOpenAPI value = new OpenAPI();\n}\n";

		using var workspace = new AdhocWorkspace();
		var solution = CreateMultiDocumentSolution(
			workspace,
			declarationSource,
			consumerSource,
			out var declarationDocumentId,
			out var consumerDocumentId
		);
		var declarationDocument = solution.GetDocument(declarationDocumentId)!;

		var diagnostic = (await GetDiagnosticsAsync(declarationDocument, cancellationToken)).Single();
		var provider = new EnglishNamingCodeFixProvider();
		var actions = new List<CodeAction>();
		var context = new CodeFixContext(
			declarationDocument,
			diagnostic,
			(action, _) => actions.Add(action),
			cancellationToken
		);

		await provider.RegisterCodeFixesAsync(context);
		var operations = await actions.Single().GetOperationsAsync(cancellationToken);
		var changedSolution = ((ApplyChangesOperation)operations.Single()).ChangedSolution;

		var fixedDeclaration = await changedSolution
			.GetDocument(declarationDocumentId)!
			.GetTextAsync(cancellationToken);
		var fixedConsumer = await changedSolution.GetDocument(consumerDocumentId)!.GetTextAsync(cancellationToken);

		await Assert.That(fixedDeclaration.ToString()).IsEqualTo(expectedDeclaration);
		await Assert.That(fixedConsumer.ToString()).IsEqualTo(expectedConsumer);
	}

	static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
		string source,
		CancellationToken cancellationToken
	)
	{
		using var workspace = new AdhocWorkspace();
		return await GetDiagnosticsAsync(CreateDocument(workspace, source), cancellationToken);
	}

	static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
		Document document,
		CancellationToken cancellationToken
	)
	{
		var compilation = (await document.Project.GetCompilationAsync(cancellationToken))!;
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new EnglishNamingAnalyzer());
		return await compilation
			.WithAnalyzers(analyzers, document.Project.AnalyzerOptions)
			.GetAnalyzerDiagnosticsAsync(cancellationToken);
	}

	static Document CreateDocument(AdhocWorkspace workspace, string source, string? filePath = null)
	{
		var projectId = ProjectId.CreateNewId();
		var documentId = DocumentId.CreateNewId(projectId);
		var project = ProjectInfo
			.Create(
				projectId,
				VersionStamp.Create(),
				"TestProject",
				"TestProject",
				LanguageNames.CSharp,
				parseOptions: CSharpParseOptions.Default,
				compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
			)
			.WithMetadataReferences(AnalyzerTestInfrastructure.BuildBclReferences());

		return workspace
			.CurrentSolution.AddProject(project)
			.AddDocument(documentId, "Test.cs", SourceText.From(source), filePath: filePath ?? "Test.cs")
			.GetDocument(documentId)!;
	}

	static Solution CreateMultiDocumentSolution(
		AdhocWorkspace workspace,
		string declarationSource,
		string consumerSource,
		out DocumentId declarationDocumentId,
		out DocumentId consumerDocumentId
	)
	{
		var projectId = ProjectId.CreateNewId();
		var project = ProjectInfo
			.Create(
				projectId,
				VersionStamp.Create(),
				"TestProject",
				"TestProject",
				LanguageNames.CSharp,
				parseOptions: CSharpParseOptions.Default,
				compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
			)
			.WithMetadataReferences(AnalyzerTestInfrastructure.BuildBclReferences());

		declarationDocumentId = DocumentId.CreateNewId(projectId);
		consumerDocumentId = DocumentId.CreateNewId(projectId);
		return workspace
			.CurrentSolution.AddProject(project)
			.AddDocument(declarationDocumentId, "OpenApi.cs", SourceText.From(declarationSource))
			.AddDocument(consumerDocumentId, "Consumer.cs", SourceText.From(consumerSource));
	}
}
