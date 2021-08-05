using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using OmniSharp.Models;
using OmniSharp.Models.SignatureHelp;
using OmniSharp.Models.v1.Completion;
using OmniSharp.Options;
using Nexus.Core;
using Nexus.Omnisharp;
using Nexus.Roslyn;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class MonacoService
    {
        /// <summary>
        /// Blazor plus Roslyn - strange behavior:
        /// 
        /// 1. it is required to divide completions and diagnostics in separate projects.
        /// 2. Whenever anything except the code property changes (like the list of requested projects),
        /// DO NOT UPDATE THE code in one of the previously mentioned projects. Only update the code 
        /// of other attached files (like the database code file).
        /// 
        /// Both issues lead to DEADLOCKS in Blazor. Blazor tasks are chained (task.ContinueWith(...)), 
        /// and suddenly one of these tasks never completes, and so does the rest of the chain. 
        /// I have debugged through the SignalR/Blazor code and found only that one tasks never completes.
        /// Whenever the Roslyn method Workspace.TryApplyChanges() is skipped (i.e. when the 'requested projects 
        /// modal' windows closes and the code is updated), Blazor continues to work.
        /// 
        /// I have tried to add locks and use separate threads to execute the "TryApplyChanges" method but
        /// nothing helps. Maybe a separate process helps. What could be the relation of Blazor and Roslyn?
        /// Maybe Roslyn modifies the thread or task the status and then Blazor is unable to complete the 
        /// current task? I thought the reason could be too many JS->Dotnet and Dotnet->JS calls, but 
        /// commenting out event callbacks to break that callback ping pong did not change anything.
        /// 
        /// Conclusion:
        ///     - Blazor stops executing user defined callbacks (i.e. "InvokeDotnetFromJS") because of tasks
        ///     that never complete.
        ///     - This is only ever caused when Roslyn's Workspace.TryApplyChanges() method is called.
        ///     
        /// Strange.
        /// 
        /// </summary>

        #region Fields

        private ILoggerFactory _loggerFactory;
        private FormattingOptions _formattingOptions;
        private IDatabaseManager _databaseManager;
        private RoslynProject _completionProject;
        private RoslynProject _diagnosticProject;
        private OmniSharpCompletionService _completionService;
        private OmniSharpSignatureHelpService _signatureService;
        private OmniSharpQuickInfoProvider _quickInfoProvider;

        #endregion

        #region Records

        public record Diagnostic()
        {
            public LinePosition Start { get; init; }
            public LinePosition End { get; init; }
            public string Message { get; init; }
            public int Severity { get; init; }
        }

        #endregion

        #region Constructors

        public MonacoService(IDatabaseManager databaseManager)
        {
            _databaseManager = databaseManager;
            _loggerFactory = LoggerFactory.Create(configure => { });
            _formattingOptions = new FormattingOptions();
        }

        #endregion

        #region Methods

        [JSInvokable]
        public async Task<CompletionResponse> GetCompletionAsync(string code, CompletionRequest completionRequest)
        {
            _completionProject.UpdateCode(_completionProject.DocumentId, code);

            var document = _completionProject.Workspace.CurrentSolution.GetDocument(_completionProject.DocumentId);
            var completionResponse = await _completionService.Handle(completionRequest, document);

            return completionResponse;
        }

        [JSInvokable]
        public async Task<CompletionResolveResponse> GetCompletionResolveAsync(CompletionResolveRequest completionResolveRequest)
        {
            var document = _completionProject.Workspace.CurrentSolution.GetDocument(_completionProject.DocumentId);
            var completionResponse = await _completionService.Handle(completionResolveRequest, document);

            return completionResponse;
        }

        [JSInvokable]
        public async Task<SignatureHelpResponse> GetSignatureHelpAsync(string code, SignatureHelpRequest signatureHelpRequest)
        {
            _completionProject.UpdateCode(_completionProject.DocumentId, code);

            var document = _completionProject.Workspace.CurrentSolution.GetDocument(_completionProject.DocumentId);
            var signatureHelpResponse = await _signatureService.Handle(signatureHelpRequest, document);

            return signatureHelpResponse;
        }

        [JSInvokable]
        public async Task<QuickInfoResponse> GetQuickInfoAsync(QuickInfoRequest quickInfoRequest)
        {
            var document = _diagnosticProject.Workspace.CurrentSolution.GetDocument(_diagnosticProject.DocumentId);
            var quickInfoResponse = await _quickInfoProvider.Handle(quickInfoRequest, document);

            return quickInfoResponse;
        }

        public async Task<List<Diagnostic>> GetDiagnosticsAsync(string code = null)
        {
            _diagnosticProject.UpdateCode(_diagnosticProject.DocumentId, code);

            var compilation = await _diagnosticProject.Workspace.CurrentSolution.Projects.First().GetCompilationAsync();
            var dotnetDiagnostics = compilation.GetDiagnostics();

            var diagnostics = dotnetDiagnostics.Select(current =>
            {
                var lineSpan = current.Location.GetLineSpan();

                return new Diagnostic()
                {
                    Start = lineSpan.StartLinePosition,
                    End = lineSpan.EndLinePosition,
                    Message = current.GetMessage(),
                    Severity = this.GetSeverity(current.Severity)
                };
            }).ToList();

            // remove warnings
            diagnostics = diagnostics
                .Where(diagnostic => diagnostic.Severity > 1)
                .ToList();

            return diagnostics;
        }

        public async Task CreateProjectForEditorAsync(CodeDefinition filter, List<string> additionalCodeFiles)
        {
            await Task.Run(() =>
            {
                _completionProject = new RoslynProject(filter, additionalCodeFiles, _databaseManager.Database);
                _diagnosticProject = new RoslynProject(filter, additionalCodeFiles, _databaseManager.Database);

                _completionService = new OmniSharpCompletionService(_completionProject.Workspace, _formattingOptions, _loggerFactory);
                _signatureService = new OmniSharpSignatureHelpService(_completionProject.Workspace);
                _quickInfoProvider = new OmniSharpQuickInfoProvider(_completionProject.Workspace, _formattingOptions, _loggerFactory);
            });
        }

        private int GetSeverity(DiagnosticSeverity severity)
        {
            return severity switch
            {
                DiagnosticSeverity.Hidden => 1,
                DiagnosticSeverity.Info => 2,
                DiagnosticSeverity.Warning => 4,
                DiagnosticSeverity.Error => 8,
                _ => throw new Exception("Unknown diagnostic severity.")
            };
        }

        #endregion
    }
}