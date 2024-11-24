﻿using System.Diagnostics;
using Cellm.AddIn;
using Cellm.AddIn.Exceptions;
using Cellm.Models.Local;
using Cellm.Models.OpenAi;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cellm.Models.Llamafile;

internal class LlamafileRequestHandler : IProviderRequestHandler<LlamafileRequest, LlamafileResponse>
{
    private record Llamafile(string ModelPath, Uri BaseAddress, Process Process);

    private readonly AsyncLazy<string> _llamafileExePath;
    private readonly Dictionary<string, AsyncLazy<Llamafile>> _llamafiles;
    private readonly ProcessManager _processManager;

    private readonly CellmConfiguration _cellmConfiguration;
    private readonly LlamafileConfiguration _llamafileConfiguration;

    private readonly ISender _sender;
    private readonly HttpClient _httpClient;
    private readonly LocalUtilities _localUtilities;
    private readonly ILogger<LlamafileRequestHandler> _logger;

    public LlamafileRequestHandler(IOptions<CellmConfiguration> cellmConfiguration,
        IOptions<LlamafileConfiguration> llamafileConfiguration,
        ISender sender,
        HttpClient httpClient,
        LocalUtilities localUtilities,
        ProcessManager processManager,
        ILogger<LlamafileRequestHandler> logger)
    {
        _cellmConfiguration = cellmConfiguration.Value;
        _llamafileConfiguration = llamafileConfiguration.Value;
        _sender = sender;
        _httpClient = httpClient;
        _localUtilities = localUtilities;
        _processManager = processManager;
        _logger = logger;

        _llamafileExePath = new AsyncLazy<string>(async () =>
        {
            var llamafileName = Path.GetFileName(_llamafileConfiguration.LlamafileUrl.Segments.Last());
            return await _localUtilities.DownloadFile(_llamafileConfiguration.LlamafileUrl, $"{llamafileName}.exe");
        });

        _llamafiles = _llamafileConfiguration.Models.ToDictionary(x => x.Key, x => new AsyncLazy<Llamafile>(async () =>
        {
            // Download model
            var modelPath = await _localUtilities.DownloadFile(x.Value, _localUtilities.CreateCellmFilePath(CreateModelFileName(x.Key)));

            // Start server
            var baseAddress = new UriBuilder(
                _llamafileConfiguration.BaseAddress.Scheme,
                _llamafileConfiguration.BaseAddress.Host,
                _localUtilities.FindPort(),
                _llamafileConfiguration.BaseAddress.AbsolutePath).Uri;

            var process = await StartProcess(modelPath, baseAddress);

            return new Llamafile(modelPath, baseAddress, process);
        }));
    }

    public async Task<LlamafileResponse> Handle(LlamafileRequest request, CancellationToken cancellationToken)
    {
        // Start server on first call
        var llamafile = await _llamafiles[request.Prompt.Options.ModelId ?? _llamafileConfiguration.DefaultModel];

        var openAiResponse = await _sender.Send(new OpenAiRequest(request.Prompt, nameof(Llamafile), llamafile.BaseAddress), cancellationToken);

        return new LlamafileResponse(openAiResponse.Prompt);
    }

    private async Task<Process> StartProcess(string modelPath, Uri baseAddress)
    {
        var processStartInfo = new ProcessStartInfo(await _llamafileExePath);

        processStartInfo.ArgumentList.Add("--server");
        processStartInfo.ArgumentList.Add("--nobrowser");
        processStartInfo.ArgumentList.Add("-m");
        processStartInfo.ArgumentList.Add(modelPath);
        processStartInfo.ArgumentList.Add("--host");
        processStartInfo.ArgumentList.Add(baseAddress.Host);
        processStartInfo.ArgumentList.Add("--port");
        processStartInfo.ArgumentList.Add(baseAddress.Port.ToString());

        if (_llamafileConfiguration.Gpu)
        {
            processStartInfo.Arguments += $"-ngl {_llamafileConfiguration.GpuLayers} ";
        }

        processStartInfo.UseShellExecute = false;
        processStartInfo.CreateNoWindow = true;
        processStartInfo.RedirectStandardError = _cellmConfiguration.Debug;
        processStartInfo.RedirectStandardOutput = _cellmConfiguration.Debug;

        var process = Process.Start(processStartInfo) ?? throw new CellmException("Failed to run Llamafile");

        if (_cellmConfiguration.Debug)
        {
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogDebug(e.Data);
                }
            };
            
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        
        var uriBuilder = new UriBuilder(baseAddress.Scheme, baseAddress.Host, baseAddress.Port, "/health");
        await _localUtilities.WaitForServer(uriBuilder.Uri, process);

        // Kill Llamafile when Excel exits or dies
        _processManager.AssignProcessToExcel(process);

        return process;
    }

    private static string CreateModelFileName(string modelName)
    {
        return $"Llamafile-{modelName}";
    }
}

