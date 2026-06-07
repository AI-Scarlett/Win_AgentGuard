using System.Diagnostics;
using AgentGuard.Core.Models;

namespace AgentGuard.Core.Services;

public sealed class AgentRegistryService
{
    private readonly AppPaths _paths;
    private readonly HookInstaller _hookInstaller;

    public AgentRegistryService(AppPaths paths, HookInstaller hookInstaller)
    {
        _paths = paths;
        _hookInstaller = hookInstaller;
    }

    public IReadOnlyList<AgentIntegrationProfile> Profiles => AgentCatalog.Profiles;

    public async Task<List<AgentAdapterState>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var states = new List<AgentAdapterState>();
        foreach (var profile in Profiles)
        {
            var configPath = profile.FullConfigurationPath(_paths.UserProfile);
            try
            {
                var configExists = File.Exists(configPath) || Directory.Exists(configPath);
                var executableExists = await ExecutableExistsAsync(profile.Executable, cancellationToken);
                var hookHealthy = profile.SupportsHookInstall && _hookInstaller.CheckHealth(profile);

                var status = hookHealthy
                    ? AdapterStatus.Active
                    : configExists || executableExists
                        ? AdapterStatus.Installed
                        : AdapterStatus.Unavailable;

                states.Add(new AgentAdapterState
                {
                    AgentId = profile.Id,
                    DisplayName = profile.DisplayName,
                    Executable = profile.Executable,
                    ConfigurationPath = configPath,
                    SupportsHookInstall = profile.SupportsHookInstall,
                    Status = status
                });
            }
            catch
            {
                states.Add(new AgentAdapterState
                {
                    AgentId = profile.Id,
                    DisplayName = profile.DisplayName,
                    Executable = profile.Executable,
                    ConfigurationPath = configPath,
                    SupportsHookInstall = profile.SupportsHookInstall,
                    Status = AdapterStatus.Error
                });
            }
        }

        return states.OrderBy(item => item.DisplayName).ToList();
    }

    public async Task InstallHooksAsync(string agentId, string? bridgeExecutablePath = null, CancellationToken cancellationToken = default)
    {
        var profile = Profiles.FirstOrDefault(item => item.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown agent: {agentId}");
        await _hookInstaller.InstallAsync(profile, bridgeExecutablePath, cancellationToken);
    }

    public async Task InstallAllAvailableHooksAsync(string? bridgeExecutablePath = null, CancellationToken cancellationToken = default)
    {
        foreach (var profile in Profiles.Where(item => item.SupportsHookInstall))
        {
            try
            {
                await _hookInstaller.InstallAsync(profile, bridgeExecutablePath, cancellationToken);
            }
            catch
            {
                // Keep batch install resilient. The UI refresh shows the exact remaining status.
            }
        }
    }

    public Task RemoveHooksAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var profile = Profiles.FirstOrDefault(item => item.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown agent: {agentId}");
        return _hookInstaller.RemoveAsync(profile, cancellationToken);
    }

    private static async Task<bool> ExecutableExistsAsync(string executable, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = executable,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
