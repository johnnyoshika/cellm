﻿using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.NetworkInformation;
using Cellm.AddIn.Exceptions;

namespace Cellm.Models.Local;

internal class LocalUtilities(HttpClient httpClient)
{
    public async Task<string> DownloadFile(Uri uri, string filePath)
    {
        if (File.Exists(filePath))
        {
            return filePath;
        }

        var filePathPart = $"{filePath}.part";

        if (File.Exists(filePathPart))
        {
            File.Delete(filePathPart);
        }

        var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using (var fileStream = File.Create(filePathPart))
        using (var httpStream = await response.Content.ReadAsStreamAsync())
        {

            await httpStream.CopyToAsync(fileStream);
        }

        File.Move(filePathPart, filePath);

        return filePath;
    }

    public async Task WaitForServer(Uri endpoint, Process process)
    {
        var startTime = DateTime.UtcNow;

        // Wait max 30 seconds to load model
        while ((DateTime.UtcNow - startTime).TotalSeconds < 30)
        {
            if (process.HasExited)
            {
                throw new CellmException($"Failed to run Llamafile, process exited. Exit code: {process.ExitCode}");
            }

            try
            {
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var response = await httpClient.GetAsync(endpoint, cancellationTokenSource.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Server is ready
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            // Wait before next attempt
            await Task.Delay(500);
        }

        process.Kill();

        throw new CellmException("Failed to run Llamafile, timeout waiting for Llamafile server to start");
    }

    public string CreateCellmDirectory(params string[] subFolders)
    {
        var folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), nameof(Cellm));

        if (subFolders.Length > 0)
        {
            folderPath = Path.Combine(subFolders.Prepend(folderPath).ToArray());
        }

        Directory.CreateDirectory(folderPath);
        return folderPath;
    }

    public string CreateCellmFilePath(string fileName)
    {
        return Path.Combine(CreateCellmDirectory(), fileName);
    }

    public int FindPort(ushort min = 49152, ushort max = 65535)
    {
        if (max < min)
        {
            throw new ArgumentException("Max port must be larger than min port.");
        }

        var ipProperties = IPGlobalProperties.GetIPGlobalProperties();

        var activePorts = ipProperties.GetActiveTcpConnections()
            .Where(connection => connection.State != TcpState.Closed)
            .Select(connection => connection.LocalEndPoint)
            .Concat(ipProperties.GetActiveTcpListeners())
            .Concat(ipProperties.GetActiveUdpListeners())
            .Select(endpoint => endpoint.Port)
            .ToArray();

        var firstInactivePort = Enumerable.Range(min, max)
            .Where(port => !activePorts.Contains(port))
            .FirstOrDefault();

        if (firstInactivePort == default)
        {
            throw new CellmException($"All local TCP ports between {min} and {max} are currently in use.");
        }

        return firstInactivePort;
    }

    public string ExtractFile(string zipFilePath, string targetDirectory)
    {
        using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.Combine(targetDirectory, entry.FullName);

                if (!File.Exists(destinationPath) || !Directory.Exists(destinationPath))
                {
                    ZipFile.ExtractToDirectory(zipFilePath, targetDirectory, true);
                    return targetDirectory;
                }
            }
        }

        return targetDirectory;
    }
}
