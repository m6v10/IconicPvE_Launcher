using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.ViewModels;

public sealed partial class AddServerDialogViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _suppressPortSync;
    private bool _queryPortEdited;
    private string? _probedName;

    public AddServerDialogViewModel(MainViewModel owner)
    {
        _owner = owner;
    }

    [ObservableProperty]
    private string address = "";

    [ObservableProperty]
    private string gamePortText = "2302";

    [ObservableProperty]
    private string queryPortText = "2303";

    [ObservableProperty]
    private string serverName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorText;

    [ObservableProperty]
    private string? probeText;

    [ObservableProperty]
    private bool probeOk;

    [ObservableProperty]
    private bool probeFailed;

    partial void OnAddressChanged(string value)
    {
        ErrorText = null;
        if (_suppressPortSync || !value.Contains(':'))
        {
            return;
        }
        // Lets a pasted "1.2.3.4:2302" (or a steam://connect link) fill the whole form.
        if (!ServerInput.TrySplitAddress(value, out var host, out var gamePort, out var queryPort))
        {
            return;
        }
        _suppressPortSync = true;
        try
        {
            Address = host;
            if (gamePort is { } game)
            {
                GamePortText = game.ToString(CultureInfo.InvariantCulture);
                if (!_queryPortEdited)
                {
                    QueryPortText = (queryPort ?? game + 1).ToString(CultureInfo.InvariantCulture);
                }
            }
            if (queryPort is { } query)
            {
                _queryPortEdited = true;
                QueryPortText = query.ToString(CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _suppressPortSync = false;
        }
    }

    partial void OnGamePortTextChanged(string value)
    {
        ErrorText = null;
        if (_queryPortEdited || !ServerInput.TryParsePort(value, out var port) || port >= ServerInput.MaxPort)
        {
            return;
        }
        // DayZ servers answer queries on game port + 1 unless the host says otherwise.
        _suppressPortSync = true;
        try
        {
            QueryPortText = (port + 1).ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _suppressPortSync = false;
        }
    }

    partial void OnQueryPortTextChanged(string value)
    {
        ErrorText = null;
        if (!_suppressPortSync)
        {
            _queryPortEdited = true;
        }
    }

    private bool CanRun => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task TestConnectionAsync()
    {
        if (!TryReadInput(out var host, out var gamePort, out var queryPort))
        {
            return;
        }
        IsBusy = true;
        ProbeOk = false;
        ProbeFailed = false;
        ProbeText = $"Contacting {host}:{queryPort}...";
        try
        {
            var ip = await ResolveAsync(host);
            if (ip is null)
            {
                ProbeFailed = true;
                ProbeText = $"{host} could not be resolved. Check the address for typos.";
                return;
            }
            var status = await _owner.A2S.QueryAsync(ip, queryPort);
            if (!status.Online)
            {
                ProbeFailed = true;
                ProbeText = $"No answer from {host}:{queryPort}. The server may be down, or that is not its query port - it is usually the game port + 1.";
                return;
            }
            _probedName = status.Name;
            if (string.IsNullOrWhiteSpace(ServerName) && status.Name is { Length: > 0 })
            {
                ServerName = status.Name;
            }
            var mods = await _owner.ServerModQuery.QueryModListAsync(ip, queryPort);
            var modText = mods is null ? "mod list unavailable" : $"{mods.Count} mods";
            ProbeOk = true;
            ProbeText = $"{status.Name} - {status.Players}/{status.MaxPlayers} players - {status.PingMs} ms - {modText}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Add-server probe failed for {Host}:{Port}", host, queryPort);
            ProbeFailed = true;
            ProbeText = "The check failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task AddAsync()
    {
        if (!TryReadInput(out var host, out var gamePort, out var queryPort))
        {
            return;
        }
        IsBusy = true;
        try
        {
            var ip = await ResolveAsync(host);
            if (ip is null)
            {
                ErrorText = $"{host} could not be resolved. Check the address for typos.";
                return;
            }
            var name = ServerName.Trim();
            if (name.Length == 0)
            {
                name = string.IsNullOrWhiteSpace(_probedName)
                    ? $"{host}:{gamePort}"
                    : _probedName!.Trim();
            }
            var entry = new ServerEntry
            {
                Id = ServerInput.BuildCustomId(ip, gamePort),
                Name = name,
                Ip = ip,
                GamePort = gamePort,
                QueryPort = queryPort,
                // Player-added servers always take their mod list from the server itself -
                // there is no config to carry one, and it stays right when the server changes.
                ModsFromQuery = true,
                RestartIntervalHours = 0,
                Mods = new List<ModEntry>()
            };
            var error = _owner.AddCustomServer(entry);
            if (error != null)
            {
                ErrorText = error;
                return;
            }
            _owner.CloseDialogCommand.Execute(null);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _owner.CloseDialogCommand.Execute(null);
    }

    private bool TryReadInput(out string host, out int gamePort, out int queryPort)
    {
        host = Address.Trim();
        gamePort = 0;
        queryPort = 0;
        if (!ServerInput.IsValidHost(host))
        {
            ErrorText = "Enter the server IP, for example 193.25.252.68";
            return false;
        }
        if (!ServerInput.TryParsePort(GamePortText, out gamePort))
        {
            ErrorText = $"The game port must be a number between {ServerInput.MinPort} and {ServerInput.MaxPort}.";
            return false;
        }
        if (!ServerInput.TryParsePort(QueryPortText, out queryPort))
        {
            ErrorText = $"The query port must be a number between {ServerInput.MinPort} and {ServerInput.MaxPort}.";
            return false;
        }
        ErrorText = null;
        return true;
    }

    private static async Task<string?> ResolveAsync(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
        {
            return parsed.ToString();
        }
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            var v4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return (v4 ?? addresses.FirstOrDefault())?.ToString();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Resolving {Host} failed", host);
            return null;
        }
    }
}
